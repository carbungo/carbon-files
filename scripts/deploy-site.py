#!/usr/bin/env python3
"""Sync a static directory to a CarbonFiles bucket using a scoped upload token."""

import argparse
import hashlib
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path


def request(url, method="GET", body=None, content_type=None):
    headers = {"Accept": "application/json"}
    if content_type:
        headers["Content-Type"] = content_type
    if hasattr(body, "fileno"):
        headers["Content-Length"] = str(os.fstat(body.fileno()).st_size)
    req = urllib.request.Request(url, data=body, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=120) as response:
            return json.load(response)
    except urllib.error.HTTPError as exc:
        # Never print the URL: upload tokens are passed in its query string.
        message = exc.read(4096).decode("utf-8", "replace")
        raise RuntimeError(f"HTTP {exc.code}: {message}") from None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="Static export directory")
    parser.add_argument("--url", default=os.getenv("CF_URL"), help="API URL (or CF_URL)")
    parser.add_argument("--bucket", default=os.getenv("CF_BUCKET"), help="Bucket ID (or CF_BUCKET)")
    args = parser.parse_args()
    token = os.getenv("CF_TOKEN")
    if not args.url or not args.bucket or not token:
        parser.error("CF_URL, CF_BUCKET, and CF_TOKEN are required")
    if not args.directory.is_dir():
        parser.error("directory does not exist")

    root = args.directory.resolve()
    local = {}
    for file in root.rglob("*"):
        if file.is_symlink():
            parser.error("symlinks are not supported")
        if file.is_file():
            relative = file.relative_to(root).as_posix()
            hasher = hashlib.sha256()
            with file.open("rb") as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                    hasher.update(chunk)
            digest = hasher.hexdigest()
            local[relative] = (file, digest)
    if not local or len(local) > 10000:
        parser.error("directory must contain 1 to 10000 files")

    base = args.url.rstrip("/") + "/api/buckets/" + urllib.parse.quote(args.bucket, safe="")
    remote = {}
    offset = 0
    while True:
        page = request(f"{base}/files?limit=1000&offset={offset}")
        remote.update((f["path"], f.get("sha256")) for f in page["items"])
        offset += len(page["items"])
        if offset >= page["total"]:
            break

    changed = [path for path in local if remote.get(path) != local[path][1]]
    changed.sort(key=lambda path: (path.endswith(".html"), path))
    token_query = urllib.parse.urlencode({"token": token})
    for path in changed:
        file, _ = local[path]
        query = urllib.parse.urlencode({"filename": path, "token": token})
        with file.open("rb") as stream:
            request(f"{base}/upload/stream?{query}", "PUT", stream, "application/octet-stream")
        print(f"uploaded {path}")

    manifest = {"files": [{"path": path, "sha256": digest} for path, (_, digest) in sorted(local.items())]}
    result = request(f"{base}/deploy/sync?{token_query}", "POST",
                     json.dumps(manifest).encode("utf-8"), "application/json")
    print(f"synced {result['kept']} files; pruned {result['deleted']} stale files")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, urllib.error.URLError) as exc:
        # URLError only includes the host or network error, not the request URL.
        print(f"deploy failed: {exc}", file=sys.stderr)
        sys.exit(1)
