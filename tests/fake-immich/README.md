# Fake Immich server

A small stand-in for the Immich server, for checking ImmichDrive in a test VM (`windows10-test` or `windows-dev`, from windows-build-tools' app-test recipe) without a real server. A real Immich needs about 4 GB even without machine learning, which doesn't fit beside a test VM on the development machine; this needs about 45 MB.

## What it serves

Only the endpoints ImmichDrive calls ([docs/immich-api.md](../../docs/immich-api.md)), in the shapes `ImmichClient` parses:

- **Library:** 41 of the test user's photos over September to November 2026, including one 12 MB photo (`IMG_LARGE.png`, random noise) so that opening it takes several Cloud Files fetches; every fifth photo is a favourite; one album ("Test Album", the first five); one partner ("Test Partner") sharing 4 photos.
- **Timeline:** month buckets, newest first, filtered by `userId`, `isFavorite` and `albumId`; month contents in the newer columnar shape, without `originalFileName`, so ImmichDrive's enrich step (`GET /api/assets/{id}`) runs.
- **Assets:** metadata with `exifInfo.fileSizeInByte`, a small PNG thumbnail, and the original with HTTP Range support (206).
- **Upload:** `POST /api/assets` multipart; the file joins the newest month.
- **Failures on purpose:** the thumbnail of `asset-0007` returns 500, so the thumbnail provider logs a failure to `thumbnail-provider.log` (FIX-063). `--deny-area albums partners` answers those lists with Immich's 403, as for a key without that permission. Restart it with another `--key` to see a rejected key (401). Stop the container to test the offline state and recovery.

Everything is generated in memory at start: synthetic images, a test user and partner on `example.invalid` addresses, and the API key `test-key`, which is a test value, not a secret. Nothing is written to disk.

## Sample library

For the Store screenshots, `--library DIR` serves real photos instead of the synthetic set, through the same endpoints and shapes. [sample-library.json](sample-library.json) lists them: 48 CC0 photos from Wikimedia Commons (landscapes, a seaside trip, a garden, cats and a dog, food), none with recognisable people, dated June to November 2026, with three albums ("Summer Trip", "Garden", "Pets"), ten favourites, and a partner ("Jordan Lee") sharing six. The user is "Alex Rivera". Both people are synthetic, on `example.invalid` addresses. Each photo records its Commons page, the download URL, the licence, the author and the file's SHA-256.

The photos are never committed. Download them, then start the fake with the override file:

```bash
python3 -I tests/fake-immich/fetch_sample_library.py          # into .artifacts/sample-library, hashes checked
FAKE_IMMICH_NETWORK=windows-dev_default docker compose -f tests/fake-immich/compose.yml -f tests/fake-immich/compose.sample.yml up -d
```

A photo whose download no longer matches its recorded hash fails the fetch, so a changed upstream file is noticed. The fake keeps the library in memory (about 32 MB, inside the same 128 MB cap), and each photo's JPEG is also its thumbnail, as Immich's "preview" size is a JPEG of about that size. No thumbnail fails on purpose in this mode.

`library.json`, which the fetch writes beside the photos, is the format `--library` reads: `user` and `partner` (`name`, `email`), `albums` (names), and `assets`, each with `file`, `name` (the original file name ImmichDrive shows), `created` (ISO UTC), and optional `favorite`, `albums` (names) and `owner` (`user`, the default, or `partner`). The fake ignores `source`. The contract check runs the fake in this mode too, against a small generated library.

To change the set, edit `sample-library.json` (a new photo needs CC0 or public domain, no recognisable people, and its source fields), delete the photo's local copy, and fetch again; the fetch prints the SHA-256 it got when it differs.

## Contract with real Immich

`contract_test.py` (`npm run check:fake-immich`, part of `validate`) holds one spec of what ImmichDrive reads from each endpoint: fields and their JSON types, response shapes, status codes and the month-key format. It checks the recorded responses of a real Immich release (`contract/immich-v3.4.1.json`) against the spec, then starts the fake in-process and checks it against the same spec, so the fake cannot drift from Immich silently. It needs no server or Docker.

Recorded 2026-11-17 from Immich v3.4.1. What the recording changed in the fake: month keys are `YYYY-MM-01` (not midnight UTC timestamps), and error bodies are `{"message": ...}` with Immich's wording. Immich serves JPEG thumbnails and the fake PNG; ImmichDrive decodes both.

To re-record for a new Immich release, when no test VM is running and at least 7 GB is free:

```bash
cd tests/fake-immich/contract
docker compose -f compose.yml up -d            # throwaway Immich, capped, loopback only, no machine learning
python3 -I record_immich.py http://127.0.0.1:22283 /tmp/immich-recording
docker compose -f compose.yml down -v          # removes the server and all its data
```

Update the image tag in `compose.yml` first, copy the new `responses.json` over `immich-v<version>.json` (and the path in `contract_test.py`), and fix the fake until `npm run check:fake-immich` passes. The recording seeds only synthetic data: generated images, users on `example.invalid`, and a throwaway password used nowhere else.

## What it does not prove

It is not Immich. It proves ImmichDrive's Windows-side behaviour (placeholders, thumbnails, hydration, upload, the drive's states) against the shapes in docs/immich-api.md, not compatibility with a real Immich release. The contract check above, against recorded responses from a real Immich release, does that.

## Use

Start the test VM first, since its compose project creates the network the fake joins. Then, from the repo root:

```bash
FAKE_IMMICH_NETWORK=windows10-test_default docker compose -f tests/fake-immich/compose.yml up -d
docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' fake-immich
```

Use `windows-dev_default` for the Windows 11 VM. In the guest, connect ImmichDrive to `http://<that address>:2283` with the API key `test-key`. The address is plain `http://`, so the Connection page also shows the unencrypted-connection warning (FIX-064), and connecting still works. The guest reaches the fake through the VM's NAT; no port is published on the host.

Afterwards:

```bash
docker compose -f tests/fake-immich/compose.yml down
```

The container runs `python:3.15.2-slim` with a read-only root filesystem and a 128 MB memory cap with no swap. Run it only beside a VM when the host has the memory: the VM's cap plus about 0.2 GB, and 2.5 GB left over.

Run the script directly for a quick check on Linux: `python3 -I tests/fake-immich/fake_immich.py --port 22830`.
