#!/usr/bin/env python3
"""Download the sample library the fake Immich server serves with --library (README.md, "Sample library").

Usage:
    python3 -I tests/fake-immich/fetch_sample_library.py [OUT]     OUT defaults to .artifacts/sample-library

Reads sample-library.json beside this script: the synthetic user, partner and albums, and for each
photo its file name, the date and folders it appears under, and where it came from (its Wikimedia
Commons page, the download URL, the licence, the author and the SHA-256 of the file). Downloads each
photo once, checks its hash, and writes OUT/library.json for the fake to read. A file whose hash
does not match is deleted and fails the run, so a changed upstream file is noticed rather than
served. Photos are CC0 or public domain only, with no recognisable people; they are never committed.

Standard library only. Requests are paced and retried on HTTP 429, as Wikimedia asks of clients.
"""
from __future__ import annotations

import hashlib
import json
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
MANIFEST = HERE / "sample-library.json"
DEFAULT_OUT = HERE.parent.parent / ".artifacts" / "sample-library"
ALLOWED_LICENCES = {"CC0", "Public domain"}

# A generic client name: Wikimedia asks for a descriptive User-Agent, not a personal contact.
USER_AGENT = "immich-drive-sample-library/1.0 (test fixture downloader)"


def download(url: str) -> bytes:
    """The bytes at `url`, retrying a few times when the server says to slow down (429)."""
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                return response.read()
        except urllib.error.HTTPError as error:
            if error.code != 429 or attempt == 4:
                raise
            time.sleep(int(error.headers.get("Retry-After") or 20))
    raise AssertionError("unreachable")


def fetch(entry: dict, out: Path) -> str | None:
    """Make sure one photo is in `out` with the recorded hash. Returns a problem, or None."""
    source = entry["source"]
    if source["licence"] not in ALLOWED_LICENCES:
        return f"{entry['file']}: licence {source['licence']!r} is not CC0 or public domain"

    path = out / entry["file"]
    if path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() == source["sha256"]:
        return None

    data = download(source["download"])
    digest = hashlib.sha256(data).hexdigest()
    if digest != source["sha256"]:
        path.unlink(missing_ok=True)
        return f"{entry['file']}: downloaded SHA-256 {digest} does not match the recorded {source['sha256']}"

    path.write_bytes(data)
    time.sleep(1)  # Pace requests to upload.wikimedia.org.
    return None


def main() -> int:
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_OUT
    out.mkdir(parents=True, exist_ok=True)
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))

    problems = [problem for entry in manifest["assets"] if (problem := fetch(entry, out))]
    for problem in problems:
        print("error:", problem, file=sys.stderr)
    if problems:
        return 1

    (out / "library.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"fetch-sample-library: {len(manifest['assets'])} photos in {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
