# Python Client SDK Design

## Overview

Handcrafted Python SDK for CarbonFiles replacing the auto-generated openapi-python-client. Full C# client parity with idiomatic Python conventions.

## Architecture

```
CarbonFiles (sync)  ──┐
                      ├──► Transport (httpx wrapper, auth, errors)
AsyncCarbonFiles    ──┘         │
                                ▼
                         Resource classes (generic over transport)
                                │
                                ▼
                         Pydantic v2 models
```

### Transport Layer

- `SyncTransport` wraps `httpx.Client`, `AsyncTransport` wraps `httpx.AsyncClient`
- Shared interface: `get()`, `post()`, `patch()`, `delete()`, `put_stream()`, `get_stream()`, `get_string()`, `send_raw()`
- Auth: Bearer token injection on all requests
- Error handling: Parse JSON error responses into `CarbonFilesError`
- Query building: Skip None params, URL-encode values

### Resource Pattern

Resource classes are generic over transport type to avoid duplicating logic. Each resource takes a transport reference and builds URLs relative to its scope.

```python
class BucketsResource(Generic[T]):
    def __init__(self, transport: T): ...
    def __getitem__(self, bucket_id: str) -> BucketResource[T]: ...

class BucketResource(Generic[T]):
    def __init__(self, transport: T, bucket_id: str): ...
    @property
    def files(self) -> FilesResource[T]: ...
    @property
    def tokens(self) -> UploadTokensResource[T]: ...
```

Sync client instantiates resources with `SyncTransport`, async with `AsyncTransport`. Methods return models directly (sync) or coroutines (async).

## Full API Surface

### Client

```python
cf = CarbonFiles(base_url, api_key?)
cf = AsyncCarbonFiles(base_url, api_key?)
```

Properties: `buckets`, `keys`, `stats`, `short_urls`, `dashboard`, `events`
Method: `health()`

### Buckets

```python
cf.buckets.create(name, description?, expires?)        # → Bucket
cf.buckets.list(limit?, offset?, sort?, order?)        # → PaginatedResponse[Bucket]
cf.buckets.list_all(limit?)                            # → Generator yielding pages
cf.buckets["id"].get(include_files?)                   # → BucketDetail
cf.buckets["id"].update(name?, description?, expires?) # → Bucket
cf.buckets["id"].delete()
cf.buckets["id"].summary()                             # → str
cf.buckets["id"].download_zip()                        # → bytes
```

### Files

```python
cf.buckets["id"].files.list(limit?, offset?, sort?, order?)  # → PaginatedResponse[BucketFile]
cf.buckets["id"].files.list_tree(delimiter?, prefix?, cursor?, limit?)  # → FileTreeResponse
cf.buckets["id"].files.list_directory(path?, limit?, offset?, sort?, order?)  # → DirectoryListingResponse
cf.buckets["id"].files.upload(source, filename?, progress?, upload_token?)  # → UploadResponse
cf.buckets["id"].files["path"].metadata()              # → BucketFile
cf.buckets["id"].files["path"].download()              # → bytes
cf.buckets["id"].files["path"].download_to(path)       # writes to disk
cf.buckets["id"].files["path"].delete()
cf.buckets["id"].files["path"].verify()                # → VerifyResponse
cf.buckets["id"].files["path"].append(data)            # → BucketFile
cf.buckets["id"].files["path"].patch(data, range_start, range_end, total_size)  # → BucketFile
```

Upload accepts: `str | Path` (file path), `bytes`, `BinaryIO`. Progress callback: `Callable[[int, int | None, float | None], None]`.

### Upload Tokens (nested under bucket)

```python
cf.buckets["id"].tokens.create(expires?, max_uploads?)  # → UploadTokenResponse
```

### API Keys (admin)

```python
cf.keys.create(name)                                   # → ApiKeyResponse
cf.keys.list(limit?, offset?, sort?, order?)           # → PaginatedResponse[ApiKeyListItem]
cf.keys["prefix"].delete()
cf.keys["prefix"].usage()                              # → ApiKeyUsageResponse
```

### Admin

```python
cf.stats.get()                                         # → StatsResponse
cf.dashboard.create_token(expires?)                    # → DashboardTokenResponse
cf.dashboard.current_user()                            # → DashboardTokenInfo
cf.short_urls["code"].delete()
cf.health()                                            # → HealthResponse
```

### Events (SignalR)

```python
cf.events.connect() / disconnect()
cf.events.subscribe_bucket(id) / unsubscribe_bucket(id)
cf.events.subscribe_file(bucket_id, path) / unsubscribe_file(...)
cf.events.subscribe_all() / unsubscribe_all()
cf.events.on_file_created(handler)    # returns unsubscribe callable
cf.events.on_file_updated(handler)
cf.events.on_file_deleted(handler)
cf.events.on_bucket_created(handler)
cf.events.on_bucket_updated(handler)
cf.events.on_bucket_deleted(handler)
```

## Models (Pydantic v2)

All models use `model_config = ConfigDict(populate_by_name=True, alias_generator=to_camel)` with snake_case field names matching the API's snake_case JSON.

### Bucket Models
- `Bucket` — id, name, owner, description?, created_at, expires_at?, last_used_at?, file_count, total_size
- `BucketDetail(Bucket)` — adds unique_content_count, unique_content_size, files?, has_more_files
- `CreateBucketRequest` — name, description?, expires_in?
- `UpdateBucketRequest` — name?, description?, expires_in?

### File Models
- `BucketFile` — path, name, size, mime_type, short_code?, short_url?, sha256?, created_at, updated_at
- `UploadResponse` — uploaded: list[UploadedFile]
- `UploadedFile` — path, name, size, mime_type, short_code?, short_url?, sha256?, deduplicated, created_at, updated_at
- `VerifyResponse` — path, stored_hash, computed_hash, valid
- `FileTreeResponse` — prefix?, delimiter, directories, files, total_files, total_directories, cursor?
- `DirectoryEntry` — path, file_count, total_size
- `DirectoryListingResponse` — files, folders, total_files, total_folders, limit, offset

### Key Models
- `ApiKeyResponse` — key, prefix, name, created_at
- `ApiKeyListItem` — prefix, name, created_at, last_used_at?, bucket_count, file_count, total_size
- `ApiKeyUsageResponse(ApiKeyListItem)` — adds total_downloads, buckets

### Token Models
- `UploadTokenResponse` — token, bucket_id, expires_at, max_uploads?, uploads_used
- `DashboardTokenResponse` — token, expires_at
- `DashboardTokenInfo` — scope, expires_at

### Admin Models
- `HealthResponse` — status, uptime_seconds, db
- `StatsResponse` — total_buckets, total_files, total_size, total_keys, total_downloads, storage_by_owner
- `OwnerStats` — owner, bucket_count, file_count, total_size

### Common
- `PaginatedResponse[T]` — items, total, limit, offset
- `ErrorResponse` — error, hint?

## Error Handling

```python
class CarbonFilesError(Exception):
    status_code: int
    error: str
    hint: str | None
```

Raised for all non-2xx responses. Attempts JSON parse of error body, falls back to raw text.

## Pagination

- `list()` returns single `PaginatedResponse[T]` page
- `list_all(limit?)` returns generator yielding `PaginatedResponse[T]` pages (auto-increments offset)
- Async `list_all()` returns `AsyncGenerator`

## Dependencies

- `httpx>=0.27,<1` — sync + async HTTP
- `pydantic>=2.0,<3` — model validation
- `signalr-async>=3.0,<4` — SignalR (optional extra: `pip install carbonfiles[events]`)

## Project Structure

```
clients/python/
├── pyproject.toml
├── README.md
├── src/carbonfiles/
│   ├── __init__.py
│   ├── client.py
│   ├── async_client.py
│   ├── transport.py
│   ├── exceptions.py
│   ├── models/
│   │   ├── __init__.py
│   │   ├── buckets.py
│   │   ├── files.py
│   │   ├── keys.py
│   │   ├── tokens.py
│   │   ├── stats.py
│   │   └── common.py
│   ├── resources/
│   │   ├── __init__.py
│   │   ├── buckets.py
│   │   ├── files.py
│   │   ├── keys.py
│   │   ├── stats.py
│   │   ├── short_urls.py
│   │   └── dashboard.py
│   └── events.py
└── tests/
    ├── conftest.py
    ├── test_transport.py
    ├── test_serialization.py
    ├── test_buckets.py
    ├── test_files.py
    ├── test_uploads.py
    ├── test_admin.py
    ├── test_events.py
    └── test_pagination.py
```
