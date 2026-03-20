# CarbonFiles Python SDK

Handcrafted Python client for the [CarbonFiles](https://github.com/carbungo/carbon-files) file-sharing API. Wraps `httpx` with a fluent, resource-scoped API featuring upload progress callbacks, multiple source types, and real-time SignalR events.

## Installation

```bash
pip install carbonfiles-client
```

For real-time event support via SignalR:

```bash
pip install carbonfiles-client[events]
```

Requires Python 3.10+.

## Quick Start

```python
from carbonfiles import CarbonFiles

cf = CarbonFiles("https://files.example.com", "cf4_your_api_key")

# Create a bucket
bucket = cf.buckets.create("my-project", description="Assets", expires="30d")
print(f"Created bucket: {bucket.id}")

# Upload a file
result = cf.buckets[bucket.id].files.upload("/path/to/photo.jpg")
print(f"Uploaded: {result.uploaded[0].name} ({result.uploaded[0].size} bytes)")

# List files
page = cf.buckets[bucket.id].files.list(limit=20)
for f in page.items:
    print(f"  {f.name} ({f.size} bytes)")

cf.close()
```

The client also supports context manager usage:

```python
with CarbonFiles("https://files.example.com", "cf4_your_api_key") as cf:
    buckets = cf.buckets.list()
```

## Authentication

Pass the token as the second argument when constructing the client:

```python
# API key
cf = CarbonFiles("https://files.example.com", "cf4_your_api_key")

# Admin key
cf = CarbonFiles("https://files.example.com", "your-admin-key")
```

CarbonFiles supports four token types, all passed as Bearer tokens:

| Type | Format | Scope |
|------|--------|-------|
| Admin key | Any string | Full access to all operations |
| API key | `cf4_` prefix | Scoped to own buckets |
| Dashboard JWT | JWT token | Admin-level access, 24h max lifetime |
| Upload token | `cfu_` prefix | Scoped to a single bucket, optional rate limit |

## Buckets

```python
# Create a bucket
bucket = cf.buckets.create("my-project", description="Assets", expires="30d")

# List buckets
page = cf.buckets.list(limit=20, sort="created_at", order="desc")

# List with expired buckets included
page = cf.buckets.list(include_expired=True)

# Get bucket details
detail = cf.buckets["bucket-id"].get()

# Get bucket details including files
detail = cf.buckets["bucket-id"].get(include_files=True)

# Update a bucket
updated = cf.buckets["bucket-id"].update(name="new-name", description="Updated")

# Delete a bucket
cf.buckets["bucket-id"].delete()

# Get plaintext summary
summary = cf.buckets["bucket-id"].summary()

# Download entire bucket as ZIP
zip_bytes = cf.buckets["bucket-id"].download_zip()
```

## Files

```python
bucket = cf.buckets["bucket-id"]

# List files (paginated)
page = bucket.files.list(limit=50, sort="name", order="asc")

# Tree listing (S3-style delimiter mode)
tree = bucket.files.list_tree(delimiter="/", prefix="docs/")
for d in tree.directories:
    print(f"  {d.path} ({d.file_count} files, {d.total_size} bytes)")

# Directory listing
listing = bucket.files.list_directory("docs/")

# Upload a file
result = bucket.files.upload("/path/to/file.txt")

# Download a file
data = bucket.files["readme.md"].download()

# Download to a local path
bucket.files["readme.md"].download_to("/tmp/readme.md")

# Get file metadata
meta = bucket.files["readme.md"].metadata()

# Verify file integrity
verify = bucket.files["readme.md"].verify()
print(f"Valid: {verify.valid}")

# Delete a file
bucket.files["old-file.txt"].delete()

# Append data to a file
bucket.files["log.txt"].append(b"new log line\n")

# Patch with byte range
bucket.files["data.bin"].patch(b"\x00" * 100, range_start=0, range_end=99, total_size=1000)
```

## Uploads

### From a file path

```python
result = bucket.files.upload("/path/to/photo.jpg")
```

The filename is derived from the path automatically. Override it if needed:

```python
result = bucket.files.upload("/path/to/photo.jpg", filename="renamed.jpg")
```

### From bytes

```python
result = bucket.files.upload(b"hello world", filename="hello.txt")
```

### From a file-like object (BinaryIO)

```python
with open("report.pdf", "rb") as f:
    result = bucket.files.upload(f, filename="report.pdf")
```

### With progress tracking

The progress callback receives `(bytes_sent, total_bytes, percentage)`:

```python
def on_progress(sent: int, total: int | None, pct: float | None):
    if pct is not None:
        print(f"{pct:.1f}% ({sent}/{total} bytes)")

result = bucket.files.upload("/path/to/large-file.zip", progress=on_progress)
```

### With an upload token

```python
result = bucket.files.upload("/path/to/file.txt", upload_token="cfu_your_upload_token")
```

## Pagination

All list methods return a `PaginatedResponse` with `items`, `total`, `limit`, and `offset` fields.

### Single page

```python
page = cf.buckets.list(limit=20, offset=0)
print(f"Showing {len(page.items)} of {page.total} buckets")
```

### Auto-pagination

Use `list_all()` to iterate through all pages automatically:

```python
for page in cf.buckets.list_all(limit=50):
    for bucket in page.items:
        print(bucket.name)
```

The same pattern works for files:

```python
for page in cf.buckets["bucket-id"].files.list_all(limit=100):
    for f in page.items:
        print(f.name)
```

## API Keys (Admin)

```python
# Create an API key
key = cf.keys.create("ci-agent")
print(f"Key: {key.key}")  # full key, only shown once
print(f"Prefix: {key.prefix}")

# List API keys
page = cf.keys.list(limit=20)
for k in page.items:
    print(f"  {k.prefix}: {k.name}")

# Get usage stats for a key
usage = cf.keys["cf4_prefix"].usage()

# Revoke an API key
cf.keys["cf4_prefix"].delete()
```

## Dashboard Tokens (Admin)

```python
# Create a dashboard token
token = cf.dashboard.create_token(expires="24h")
print(f"Token: {token.token}")

# Validate current token
info = cf.dashboard.current_user()
```

## Upload Tokens

Upload tokens are scoped to a single bucket and optionally rate-limited:

```python
# Create an upload token
token = cf.buckets["bucket-id"].tokens.create(expires="1h", max_uploads=10)
print(f"Token: {token.token}")

# Use the token when uploading
cf.buckets["bucket-id"].files.upload("/path/to/file.txt", upload_token=token.token)
```

## Stats (Admin)

```python
stats = cf.stats.get()
print(f"Total buckets: {stats.total_buckets}")
print(f"Total files: {stats.total_files}")
print(f"Total size: {stats.total_size}")
```

## Short URLs

```python
# Delete a short URL
cf.short_urls["abc123"].delete()
```

## Real-Time Events

Real-time events require the `events` extra:

```bash
pip install carbonfiles-client[events]
```

```python
from carbonfiles.events import CarbonFilesEvents

events = CarbonFilesEvents("https://files.example.com", "cf4_your_api_key")

# Register event handlers
events.on_file_created(lambda data: print(f"File created: {data}"))
events.on_file_deleted(lambda data: print(f"File deleted: {data}"))
events.on_bucket_created(lambda data: print(f"Bucket created: {data}"))

# Connect to the SignalR hub
events.connect()

# Subscribe to events for a specific bucket
events.subscribe_bucket("bucket-id")

# Subscribe to events for a specific file
events.subscribe_file("bucket-id", "path/to/file.txt")

# Subscribe to all events (admin only)
events.subscribe_all()

# Disconnect when done
events.disconnect()
```

Available events: `on_file_created`, `on_file_updated`, `on_file_deleted`, `on_bucket_created`, `on_bucket_updated`, `on_bucket_deleted`.

Each handler registration returns an unsubscribe callable:

```python
unsubscribe = events.on_file_created(lambda data: print(data))

# Later, remove the handler
unsubscribe()
```

## Error Handling

API errors raise `CarbonFilesError` with `status_code`, `error`, and optional `hint`:

```python
from carbonfiles import CarbonFiles, CarbonFilesError

cf = CarbonFiles("https://files.example.com", "cf4_your_api_key")

try:
    cf.buckets["nonexistent"].get()
except CarbonFilesError as e:
    print(f"HTTP {e.status_code}: {e.error}")
    if e.hint:
        print(f"Hint: {e.hint}")
```

## Async Usage

The `AsyncCarbonFiles` client provides the same API with `async`/`await`:

```python
import asyncio
from carbonfiles import AsyncCarbonFiles

async def main():
    async with AsyncCarbonFiles("https://files.example.com", "cf4_your_api_key") as cf:
        # Create a bucket
        bucket = await cf.buckets.create("my-project", description="Assets")

        # Upload a file
        result = await cf.buckets[bucket.id].files.upload("/path/to/photo.jpg")

        # List files
        page = await cf.buckets[bucket.id].files.list(limit=20)
        for f in page.items:
            print(f"  {f.name} ({f.size} bytes)")

        # Auto-paginate
        async for page in cf.buckets[bucket.id].files.list_all():
            for f in page.items:
                print(f.name)

        # Health check
        health = await cf.health()

asyncio.run(main())
```

## License

MIT
