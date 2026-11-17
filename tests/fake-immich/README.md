# Fake Immich server

A small stand-in for the Immich server, for checking ImmichDrive in a test VM (`windows10-test` or `windows-dev`, from windows-build-tools' app-test recipe) without a real server. A real Immich needs about 4 GB even without machine learning, which doesn't fit beside a test VM on the development machine; this needs about 45 MB.

## What it serves

Only the endpoints ImmichDrive calls ([docs/immich-api.md](../../docs/immich-api.md)), in the shapes `ImmichClient` parses:

- **Library:** 41 of the test user's photos over September to November 2026, including one 12 MB photo (`IMG_LARGE.png`, random noise) so that opening it takes several Cloud Files fetches; every fifth photo is a favourite; one album ("Test Album", the first five); one partner ("Test Partner") sharing 4 photos.
- **Timeline:** month buckets, newest first, filtered by `userId`, `isFavorite` and `albumId`; month contents in the newer columnar shape, without `originalFileName`, so ImmichDrive's enrich step (`GET /api/assets/{id}`) runs.
- **Assets:** metadata with `exifInfo.fileSizeInByte`, a small PNG thumbnail, and the original with HTTP Range support (206).
- **Upload:** `POST /api/assets` multipart; the file joins the newest month.
- **Failures on purpose:** the thumbnail of `asset-0007` returns 500, so the thumbnail provider logs a failure to `thumbnail-provider.log` (FIX-063). Stop the container to test the offline state and recovery.

Everything is generated in memory at start: synthetic images, a test user and partner on `example.invalid` addresses, and the API key `test-key`, which is a test value, not a secret. Nothing is written to disk.

## What it does not prove

It is not Immich. It proves ImmichDrive's Windows-side behaviour (placeholders, thumbnails, hydration, upload, the drive's states) against the shapes in docs/immich-api.md, not compatibility with a real Immich release. The contract check against recorded responses from a real Immich release does that, so the fake cannot drift from Immich silently.

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
