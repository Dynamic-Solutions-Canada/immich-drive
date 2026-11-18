#!/usr/bin/env python3
"""A small stand-in for the Immich server, for ImmichDrive's VM checks.

Serves only the endpoints ImmichDrive calls (docs/immich-api.md), in the shapes it parses, from
synthetic images generated in memory at start: no personal data, no disk writes, standard library
only. With --library, it serves a folder of sample photos instead (the Store screenshots use the
public-domain set that fetch_sample_library.py downloads); the shapes are the same. It is not Immich and proves nothing about compatibility with a real server; the contract test
against a recorded Immich release does that. See README.md.

Usage:
    python3 fake_immich.py [--port 2283] [--key test-key] [--fail-thumbnail ASSET_ID ...] [--deny-area albums|partners ...]
                           [--library DIR]

Endpoints (all under /api, all but /server/ping need the x-api-key header):
    GET  /users/me                       the user (the connection probe)
    GET  /server/ping                    {"res": "pong"}
    GET  /timeline/buckets               month buckets, newest first; filters userId, isFavorite, albumId
    GET  /timeline/bucket                one month, columnar (struct of arrays), no originalFileName
    GET  /assets/{id}                    name, type, created time and exifInfo.fileSizeInByte
    GET  /assets/{id}/thumbnail          a small PNG, or a sample photo's JPEG; 500 for ids passed to --fail-thumbnail
    GET  /assets/{id}/original           the original bytes, with HTTP Range support
    GET  /albums                         the albums (one, or the sample library's)
    GET  /partners?direction=shared-with one partner, whose library is reached with userId
    POST /assets                         multipart upload; the asset joins the newest month
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import struct
import sys
import threading
import zlib
from datetime import datetime, timezone
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

USER_ID = "user-0001"
PARTNER_ID = "partner-0001"
ALBUM_ID = "album-0001"

# Who the synthetic library belongs to. A sample library names its own people (also synthetic).
TEST_USER = {"name": "Test User", "email": "test.user@example.invalid"}
TEST_PARTNER = {"name": "Test Partner", "email": "test.partner@example.invalid"}


# ---------------------------------------------------------------------------------------------------
# Synthetic images
# ---------------------------------------------------------------------------------------------------

def png(width: int, height: int, rows: bytes | None = None, colour: tuple[int, int, int] = (90, 120, 160)) -> bytes:
    """A valid RGB PNG. `rows` is raw RGB data (height * width * 3); without it, a flat colour."""

    def chunk(kind: bytes, data: bytes) -> bytes:
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    if rows is None:
        rows = bytes(colour) * width * height
    stride = width * 3
    raw = b"".join(b"\x00" + rows[y * stride:(y + 1) * stride] for y in range(height))
    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b"")


def colour_for(index: int) -> tuple[int, int, int]:
    """A distinct, stable colour per asset, so thumbnails can be told apart in screenshots."""
    return ((index * 53) % 200 + 40, (index * 97) % 200 + 40, (index * 151) % 200 + 40)


# ---------------------------------------------------------------------------------------------------
# The library
# ---------------------------------------------------------------------------------------------------

class Asset:
    """One asset: what the timeline, the asset endpoint, the thumbnail and the original serve."""

    def __init__(self, asset_id: str, name: str, created: str, owner: str, favorite: bool, original: bytes, colour,
                 thumbnail: tuple[bytes, str] | None = None):
        """`thumbnail` is the image and its content type; without it, a small PNG in `colour`."""
        self.id = asset_id
        self.name = name
        self.created = created
        self.owner = owner
        self.favorite = favorite
        self.original = original
        self.thumbnail, self.thumbnail_type = thumbnail or (png(64, 48, colour=colour), "image/png")

    @property
    def month(self) -> str:
        """The month bucket key, as Immich v3.4.1 writes it ("2026-10-01")."""
        return self.created[:7] + "-01"


def build_library() -> list[Asset]:
    """About 40 of the user's photos over three months, a partner's 4, and one large photo.

    The large photo is random noise, so it stays about 12 MB compressed and hydrating it takes
    several Cloud Files fetches. Favourites are every fifth photo; the album holds the first five.
    """
    assets: list[Asset] = []
    months = ["2026-09", "2026-10", "2026-11"]
    for i in range(40):
        month = months[i % 3]
        day = 1 + (i % 27)
        created = f"{month}-{day:02d}T{(8 + i % 10):02d}:00:00.000Z"
        colour = colour_for(i)
        assets.append(Asset(f"asset-{i:04d}", f"IMG_{i:04d}.png", created, USER_ID, i % 5 == 0, png(640, 480, colour=colour), colour))

    noise = os.urandom(2048 * 2048 * 3)
    assets.append(Asset("asset-large", "IMG_LARGE.png", "2026-11-15T12:00:00.000Z", USER_ID, False, png(2048, 2048, rows=noise), (128, 128, 128)))

    for i in range(4):
        colour = colour_for(100 + i)
        assets.append(Asset(f"partner-asset-{i:02d}", f"PARTNER_{i:02d}.png", f"2026-10-{10 + i:02d}T09:00:00.000Z", PARTNER_ID, False, png(640, 480, colour=colour), colour))
    return assets


def load_sample_library(folder: Path) -> tuple[list[Asset], dict[str, tuple[str, set[str]]], dict, dict]:
    """A sample library from `folder`/library.json and the photos beside it.

    Returns the assets, the albums (id to name and asset ids), and the user and partner. The file
    format is described in README.md ("Sample library"); every photo is read into memory at start,
    and its JPEG is also its thumbnail (ImmichDrive asks for the "preview" size, which Immich serves as
    a JPEG about this size). Fails at start, naming the file, when a listed photo is missing.
    """
    spec = json.loads((folder / "library.json").read_text(encoding="utf-8"))
    owners = {"user": USER_ID, "partner": PARTNER_ID}
    albums = {f"album-{i + 1:04d}": (name, set()) for i, name in enumerate(spec["albums"])}
    album_by_name = {name: album_id for album_id, (name, _) in albums.items()}

    assets: list[Asset] = []
    for i, entry in enumerate(spec["assets"]):
        path = folder / entry["file"]
        if not path.is_file():
            raise SystemExit(f"fake-immich: {path} is missing; run fetch_sample_library.py first")
        data = path.read_bytes()
        asset = Asset(f"asset-{i:04d}", entry["name"], entry["created"], owners[entry.get("owner", "user")],
                      bool(entry.get("favorite")), data, colour_for(i), thumbnail=(data, "image/jpeg"))
        assets.append(asset)
        for name in entry.get("albums", []):
            albums[album_by_name[name]][1].add(asset.id)
    return assets, albums, spec["user"], spec["partner"]


class Library:
    """The in-memory library, safe to read and extend from the server's threads."""

    def __init__(self, folder: Path | None = None):
        """The synthetic library, or with `folder` the sample library in it (load_sample_library)."""
        self._lock = threading.Lock()
        if folder is None:
            self.assets = build_library()
            self.albums = {ALBUM_ID: ("Test Album", {a.id for a in self.assets[:5]})}
            self.user, self.partner = TEST_USER, TEST_PARTNER
        else:
            self.assets, self.albums, self.user, self.partner = load_sample_library(folder)
        self.uploads = 0

    def select(self, query: dict[str, list[str]]) -> list[Asset]:
        """The assets a timeline query covers: the user's, a partner's (userId), favourites, or an album."""
        album = first(query, "albumId")
        user = first(query, "userId") or USER_ID
        favourite = first(query, "isFavorite") == "true"
        with self._lock:
            if album is not None:
                members = self.albums.get(album, ("", set()))[1]
                chosen = [a for a in self.assets if a.id in members]
            else:
                chosen = [a for a in self.assets if a.owner == user]
        if favourite:
            chosen = [a for a in chosen if a.favorite]
        return chosen

    def find(self, asset_id: str) -> Asset | None:
        with self._lock:
            return next((a for a in self.assets if a.id == asset_id), None)

    def add_upload(self, name: str, data: bytes) -> Asset:
        """An uploaded file joins the newest month, created now."""
        with self._lock:
            self.uploads += 1
            created = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.000Z")
            asset = Asset(f"upload-{self.uploads:04d}", name, created, USER_ID, False, data, colour_for(200 + self.uploads))
            self.assets.append(asset)
            return asset


def asset_data_part(content_type: str, body: bytes) -> tuple[str, bytes] | None:
    """The file name and bytes of the multipart `assetData` part, or None.

    Split on the boundary from the Content-Type, because file bytes can contain any sequence. .NET
    writes `name=assetData; filename=x.png` without quotes, other clients quote them; accept both.
    """
    match = re.search(r'boundary="?([^";]+)"?', content_type)
    if not match:
        return None
    for part in body.split(b"--" + match.group(1).encode()):
        head, sep, data = part.partition(b"\r\n\r\n")
        if not sep or not re.search(rb'name="?assetData"?(;|\r|$)', head):
            continue
        name = re.search(rb'filename="?([^";\r\n]+)"?', head)
        # The part ends with the CRLF that precedes the next boundary.
        return (name.group(1).decode("utf-8", "replace") if name else "upload.bin", data.removesuffix(b"\r\n"))
    return None


def first(query: dict[str, list[str]], key: str) -> str | None:
    values = query.get(key)
    return values[0] if values else None


# ---------------------------------------------------------------------------------------------------
# HTTP
# ---------------------------------------------------------------------------------------------------

class Handler(BaseHTTPRequestHandler):
    library: Library
    api_key: str
    failing_thumbnails: set[str]
    denied_areas: set[str]
    protocol_version = "HTTP/1.1"

    def do_GET(self):
        self.route("GET")

    def do_POST(self):
        self.route("POST")

    def route(self, method: str) -> None:
        url = urlparse(self.path)
        path = url.path.removeprefix("/api")
        query = parse_qs(url.query)

        if method == "GET" and path == "/server/ping":
            return self.send_json({"res": "pong"})
        if self.headers.get("x-api-key") != self.api_key:
            return self.send_json({"message": "Invalid API key"}, HTTPStatus.UNAUTHORIZED)

        if method == "GET" and path == "/users/me":
            return self.send_json({"id": USER_ID, **self.library.user})
        if method == "GET" and path == "/timeline/buckets":
            return self.send_json(self.buckets(query))
        if method == "GET" and path == "/timeline/bucket":
            return self.send_json(self.bucket(query))
        for area, permission in (("albums", "album.read"), ("partners", "partner.read")):
            if path == f"/{area}" and area in self.denied_areas:
                return self.send_json({"message": f"Missing required permission: {permission}"}, HTTPStatus.FORBIDDEN)
        if method == "GET" and path == "/albums":
            return self.send_json([
                {"id": album_id, "albumName": name, "assetCount": len(members)}
                for album_id, (name, members) in self.library.albums.items()
            ])
        if method == "GET" and path == "/partners":
            return self.send_json([{"id": PARTNER_ID, **self.library.partner}])
        if method == "POST" and path == "/assets":
            return self.upload()

        match = re.fullmatch(r"/assets/([^/]+)(/thumbnail|/original)?", path)
        if method == "GET" and match:
            asset = self.library.find(match.group(1))
            if asset is None:
                return self.send_json({"message": "Not found"}, HTTPStatus.NOT_FOUND)
            if match.group(2) == "/thumbnail":
                if asset.id in self.failing_thumbnails:
                    return self.send_json({"message": "Thumbnail failed (fake)"}, HTTPStatus.INTERNAL_SERVER_ERROR)
                return self.send_bytes(asset.thumbnail, asset.thumbnail_type)
            if match.group(2) == "/original":
                return self.send_original(asset)
            return self.send_json({
                "id": asset.id,
                "type": "IMAGE",
                "originalFileName": asset.name,
                "fileCreatedAt": asset.created,
                "ownerId": asset.owner,
                "isFavorite": asset.favorite,
                "exifInfo": {"fileSizeInByte": len(asset.original)},
            })
        return self.send_json({"message": "Not found"}, HTTPStatus.NOT_FOUND)

    def buckets(self, query) -> list[dict]:
        """Month buckets for the query, newest first, keyed "YYYY-MM-01" like Immich v3.4.1."""
        counts: dict[str, int] = {}
        for asset in self.library.select(query):
            counts[asset.month] = counts.get(asset.month, 0) + 1
        return [{"timeBucket": key, "count": counts[key]} for key in sorted(counts, reverse=True)]

    def bucket(self, query) -> dict:
        """One month in the newer columnar shape. No originalFileName, so ImmichDrive enriches by id."""
        key = first(query, "timeBucket") or ""
        chosen = [a for a in self.library.select(query) if a.month == key]
        return {
            "id": [a.id for a in chosen],
            "fileCreatedAt": [a.created for a in chosen],
            "ownerId": [a.owner for a in chosen],
            "isImage": [True for _ in chosen],
            "isFavorite": [a.favorite for a in chosen],
            "isTrashed": [False for _ in chosen],
            "duration": [None for _ in chosen],
            "thumbhash": [None for _ in chosen],
            "ratio": [4 / 3 for _ in chosen],
            "livePhotoVideoId": [None for _ in chosen],
            "visibility": ["timeline" for _ in chosen],
        }

    def send_original(self, asset: Asset) -> None:
        """The whole original, or the byte range asked for (206), as ImmichDrive's hydration requests."""
        data = asset.original
        header = self.headers.get("Range")
        match = re.fullmatch(r"bytes=(\d+)-(\d*)", header or "")
        if not match:
            return self.send_bytes(data, "application/octet-stream")
        start = int(match.group(1))
        end = int(match.group(2)) if match.group(2) else len(data) - 1
        end = min(end, len(data) - 1)
        if start >= len(data) or start > end:
            self.send_response(HTTPStatus.REQUESTED_RANGE_NOT_SATISFIABLE)
            self.send_header("Content-Range", f"bytes */{len(data)}")
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        part = data[start:end + 1]
        self.send_response(HTTPStatus.PARTIAL_CONTENT)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Range", f"bytes {start}-{end}/{len(data)}")
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(len(part)))
        self.end_headers()
        self.wfile.write(part)

    def upload(self) -> None:
        """A multipart upload: the assetData part becomes a new asset (201 created)."""
        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length)
        part = asset_data_part(self.headers.get("Content-Type") or "", body)
        if part is None:
            return self.send_json({"message": "No assetData part"}, HTTPStatus.BAD_REQUEST)
        asset = self.library.add_upload(*part)
        self.send_json({"id": asset.id, "status": "created"}, HTTPStatus.CREATED)

    def send_json(self, value, status: HTTPStatus = HTTPStatus.OK) -> None:
        self.send_bytes(json.dumps(value).encode("utf-8"), "application/json", status)

    def send_bytes(self, data: bytes, content_type: str, status: HTTPStatus = HTTPStatus.OK) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, fmt, *args):
        # One line per request, without the API key (it is in a header, never in the path).
        print(f"fake-immich: {self.command} {self.path} -> {args[1] if len(args) > 1 else ''}", flush=True)


class Server(ThreadingHTTPServer):
    """The HTTP server, quiet when a client simply goes away."""

    def handle_error(self, request, client_address):
        # A client closing a kept-alive socket (Invoke-WebRequest, a cancelled hydration) is normal and
        # would print a traceback; every other error still does.
        error = sys.exc_info()[1]
        if isinstance(error, (ConnectionResetError, BrokenPipeError, ConnectionAbortedError)):
            return
        super().handle_error(request, client_address)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--port", type=int, default=2283)
    parser.add_argument("--key", default="test-key", help="The API key the fake accepts. A test value, not a secret.")
    parser.add_argument("--fail-thumbnail", nargs="*", default=[], help="Asset ids whose thumbnail returns 500.")
    parser.add_argument("--deny-area", nargs="*", default=[], choices=["albums", "partners"],
                        help="Areas answered with 403, as for a key without that permission.")
    parser.add_argument("--library", type=Path, default=None,
                        help="Serve the sample library in this folder (library.json and its photos) instead of the synthetic one.")
    args = parser.parse_args()

    Handler.library = Library(args.library)
    Handler.api_key = args.key
    Handler.failing_thumbnails = set(args.fail_thumbnail)
    Handler.denied_areas = set(args.deny_area)
    server = Server(("0.0.0.0", args.port), Handler)
    count = len(Handler.library.assets)
    print(f"fake-immich: serving {count} assets on port {args.port}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
