#!/usr/bin/env python3
"""Contract test: the fake Immich server against a recorded real Immich release.

One spec says what ImmichDrive reads from each endpoint (ImmichClient: the fields, their JSON types,
the response shapes, the status codes, the month-key format). The test checks the recorded real
responses (contract/immich-v<version>.json, from contract/record_immich.py) against the spec, which
shows the spec still describes Immich, and then starts the fake in-process and checks it against the
same spec, which shows the fake has not drifted. The fake is checked twice: with its synthetic library,
and with a small sample library in the --library format (written to a temporary folder, the photos
being placeholder bytes), so the Store screenshots' mode keeps the same shapes. Run by
`npm run validate` as `check:fake-immich`.

Standard library only; no server, no Docker. Exits 1 and lists every mismatch.
"""
from __future__ import annotations

import importlib.util
import json
import re
import sys
import tempfile
import threading
import urllib.error
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
RECORDING = HERE / "contract" / "immich-v3.4.1.json"

# What ImmichDrive reads. "?" after a type name means the field may be null; a list type means a
# columnar array whose elements have that type.
OBJECT = dict
SPEC: dict[str, dict] = {
    "users_me": {"status": 200, "fields": {"id": "str", "name": "str", "email": "str"}},
    "timeline_buckets": {"status": 200, "list_of": {"timeBucket": "month", "count": "int"}},
    "timeline_buckets_favorite": {"status": 200, "list_of": {"timeBucket": "month", "count": "int"}},
    "timeline_buckets_partner": {"status": 200, "list_of": {"timeBucket": "month", "count": "int"}},
    "timeline_buckets_album": {"status": 200, "list_of": {"timeBucket": "month", "count": "int"}},
    "timeline_bucket": {"status": 200, "columnar": {"id": "str", "fileCreatedAt": "str", "isImage": "bool", "duration": "str?"}},
    "timeline_bucket_album": {"status": 200, "columnar": {"id": "str", "fileCreatedAt": "str", "isImage": "bool", "duration": "str?"}},
    "partners": {"status": 200, "list_of": {"id": "str", "name": "str", "email": "str"}},
    "albums": {"status": 200, "list_of": {"id": "str", "albumName": "str", "assetCount": "int"}},
    "asset": {"status": 200, "fields": {"id": "str", "originalFileName": "str", "type": "str", "fileCreatedAt": "str"},
              "exif": {"fileSizeInByte": "int"}},
    "asset_thumbnail": {"status": 200, "image": True},
    "asset_original_range": {"status": 206, "range": True},
    "users_me_bad_key": {"status": 401, "fields": {"message": "str"}},
    "partners_without_permission": {"status": 403, "fields": {"message": "str"}},
    "albums_without_permission": {"status": 403, "fields": {"message": "str"}},
}

TYPES = {"str": str, "int": int, "bool": bool}


def type_ok(value, expected: str) -> bool:
    nullable = expected.endswith("?")
    name = expected.rstrip("?")
    if value is None:
        return nullable
    if name == "month":
        return isinstance(value, str) and re.fullmatch(r"\d{4}-\d{2}-01", value) is not None
    if name == "int":
        return isinstance(value, int) and not isinstance(value, bool)
    return isinstance(value, TYPES[name])


def check(source: str, name: str, entry: dict, problems: list[str]) -> None:
    """One response (status, content type, body or byte facts) against the spec."""
    spec = SPEC[name]
    where = f"{source} {name}"
    if entry["status"] != spec["status"]:
        problems.append(f"{where}: status {entry['status']}, expected {spec['status']}")
        return
    body = entry.get("body")
    if "fields" in spec or "exif" in spec:
        if not isinstance(body, OBJECT):
            problems.append(f"{where}: expected an object")
            return
        for field, kind in spec.get("fields", {}).items():
            if not type_ok(body.get(field), kind):
                problems.append(f"{where}: {field} is {body.get(field)!r}, expected {kind}")
        exif = body.get("exifInfo") if "exif" in spec else None
        for field, kind in spec.get("exif", {}).items():
            if not isinstance(exif, OBJECT) or not type_ok(exif.get(field), kind):
                problems.append(f"{where}: exifInfo.{field} missing or not {kind}")
    if "list_of" in spec:
        if not isinstance(body, list) or not body:
            problems.append(f"{where}: expected a non-empty array")
            return
        for item in body:
            for field, kind in spec["list_of"].items():
                if not isinstance(item, OBJECT) or not type_ok(item.get(field), kind):
                    problems.append(f"{where}: an item's {field} is {item.get(field)!r}, expected {kind}")
                    return
    if "columnar" in spec:
        if not isinstance(body, OBJECT):
            problems.append(f"{where}: expected a columnar object (struct of arrays)")
            return
        lengths = {len(body[f]) for f in spec["columnar"] if isinstance(body.get(f), list)}
        if len(lengths) != 1:
            problems.append(f"{where}: columnar arrays missing or of unequal length")
            return
        for field, kind in spec["columnar"].items():
            if not all(type_ok(v, kind) for v in body[field]):
                problems.append(f"{where}: {field}[] not all {kind}")
    if spec.get("image") and not entry["content_type"].startswith("image/"):
        problems.append(f"{where}: content type {entry['content_type']!r}, expected an image")
    if spec.get("range") and not re.fullmatch(r"bytes 0-99/\d+", entry.get("content_range") or ""):
        problems.append(f"{where}: Content-Range {entry.get('content_range')!r}, expected bytes 0-99/<size>")


def load_fake():
    spec = importlib.util.spec_from_file_location("fake_immich", HERE / "fake_immich.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def write_sample_library(folder: Path) -> None:
    """A minimal library in the --library format: two of the user's photos in an album, one partner's.

    The bytes are not real JPEGs; the contract covers response shapes, not image decoding.
    """
    assets = [
        {"file": "a.jpg", "name": "IMG_0001.jpg", "created": "2026-10-03T09:15:00.000Z", "favorite": True, "albums": ["Trip"]},
        {"file": "b.jpg", "name": "IMG_0002.jpg", "created": "2026-09-20T17:40:00.000Z", "albums": ["Trip"]},
        {"file": "c.jpg", "name": "IMG_0003.jpg", "created": "2026-10-11T12:00:00.000Z", "owner": "partner"},
    ]
    for entry in assets:
        (folder / entry["file"]).write_bytes(b"\xff\xd8\xff\xe0" + entry["name"].encode() * 20 + b"\xff\xd9")
    library = {
        "user": {"name": "Sample User", "email": "sample.user@example.invalid"},
        "partner": {"name": "Sample Partner", "email": "sample.partner@example.invalid"},
        "albums": ["Trip"],
        "assets": assets,
    }
    (folder / "library.json").write_text(json.dumps(library), encoding="utf-8")


def serve(fake, denied: set[str], library_folder: Path | None = None):
    """Start the fake on a free local port; returns its base URL."""
    handler = type("ContractHandler", (fake.Handler,), {
        "library": fake.Library(library_folder), "api_key": "test-key", "failing_thumbnails": set(), "denied_areas": denied,
        "log_message": lambda self, *args: None,
    })
    server = fake.Server(("127.0.0.1", 0), handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return f"http://127.0.0.1:{server.server_address[1]}/api", server


def fetch(base: str, path: str, key: str = "test-key", headers: dict | None = None, raw: bool = False) -> dict:
    request = urllib.request.Request(base + path, headers={"x-api-key": key, **(headers or {})})
    try:
        with urllib.request.urlopen(request) as response:
            status, content_type, content_range, data = response.status, response.headers.get("Content-Type", ""), response.headers.get("Content-Range"), response.read()
    except urllib.error.HTTPError as error:
        status, content_type, content_range, data = error.code, error.headers.get("Content-Type", ""), error.headers.get("Content-Range"), error.read()
    entry = {"status": status, "content_type": content_type, "content_range": content_range}
    if not raw:
        entry["body"] = json.loads(data) if data else None
    return entry


def fake_responses(fake, library_folder: Path | None = None) -> dict[str, dict]:
    """The fake's answers to the same requests the recording made, with the fake's own ids."""
    base, server = serve(fake, set(), library_folder)
    denied_base, denied_server = serve(fake, {"albums", "partners"}, library_folder)
    query = "size=MONTH&isTrashed=false&isArchived=false"
    try:
        buckets = fetch(base, f"/timeline/buckets?{query}")
        month = buckets["body"][0]["timeBucket"]
        album_buckets = fetch(base, f"/timeline/buckets?size=MONTH&albumId={fake.ALBUM_ID}")
        return {
            "users_me": fetch(base, "/users/me"),
            "timeline_buckets": buckets,
            "timeline_bucket": fetch(base, f"/timeline/bucket?{query}&timeBucket={month}"),
            "timeline_buckets_favorite": fetch(base, f"/timeline/buckets?{query}&isFavorite=true"),
            "timeline_buckets_partner": fetch(base, f"/timeline/buckets?{query}&userId={fake.PARTNER_ID}"),
            "partners": fetch(base, "/partners?direction=shared-with"),
            "albums": fetch(base, "/albums"),
            "timeline_buckets_album": album_buckets,
            "timeline_bucket_album": fetch(base, f"/timeline/bucket?size=MONTH&albumId={fake.ALBUM_ID}&timeBucket={album_buckets['body'][0]['timeBucket']}"),
            "asset": fetch(base, "/assets/asset-0000"),
            "asset_thumbnail": fetch(base, "/assets/asset-0000/thumbnail?size=preview", raw=True),
            "asset_original_range": fetch(base, "/assets/asset-0000/original", headers={"Range": "bytes=0-99"}, raw=True),
            "users_me_bad_key": fetch(base, "/users/me", key="not-a-key"),
            "partners_without_permission": fetch(denied_base, "/partners?direction=shared-with"),
            "albums_without_permission": fetch(denied_base, "/albums"),
        }
    finally:
        server.shutdown()
        denied_server.shutdown()


def main() -> int:
    recording = json.loads(RECORDING.read_text())
    real = recording["responses"]
    version = "{major}.{minor}.{patch}".format(**recording["server_version"])
    problems: list[str] = []

    missing = set(SPEC) - set(real)
    if missing:
        problems.append(f"recording lacks {sorted(missing)}; re-record with contract/record_immich.py")
    for name in sorted(set(SPEC) & set(real)):
        check(f"Immich {version}", name, real[name], problems)

    module = load_fake()
    fake = fake_responses(module)
    for name in sorted(SPEC):
        check("fake", name, fake[name], problems)

    with tempfile.TemporaryDirectory() as folder:
        write_sample_library(Path(folder))
        sample = fake_responses(module, Path(folder))
    for name in sorted(SPEC):
        check("fake --library", name, sample[name], problems)

    for problem in problems:
        print("error:", problem, file=sys.stderr)
    print(f"check:fake-immich: {'failed' if problems else 'passed'} ({len(SPEC)} endpoints against Immich {version}, {len(problems)} problem(s))")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
