# Python Client SDK Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the auto-generated Python client with a handcrafted SDK matching full C# client parity.

**Architecture:** Two client classes (sync/async) share a transport layer (httpx wrapper). Resource classes use generics over transport type. All models are Pydantic v2. SignalR events via optional dependency.

**Tech Stack:** Python 3.10+, httpx, pydantic v2, pytest, signalr-async (optional)

**Reference files:**
- C# transport: `clients/csharp/Internal/HttpTransport.cs`
- C# resources: `clients/csharp/Resources/*.cs`
- C# models: `clients/csharp/Models/*.cs`
- C# tests: `tests/CarbonFiles.Client.Tests/`
- Design doc: `docs/plans/2026-03-04-python-client-design.md`

---

### Task 1: Project scaffolding and pyproject.toml

**Files:**
- Create: `clients/python/pyproject.toml` (replace existing)
- Create: `clients/python/src/carbonfiles/__init__.py`
- Create: `clients/python/src/carbonfiles/py.typed`
- Delete: `clients/python/carbonfiles_client/` (old generated code)
- Delete: `clients/python/config.yml`, `clients/python/generate.sh`

**Step 1: Remove old generated client files**

```bash
rm -rf clients/python/carbonfiles_client/
rm -f clients/python/config.yml clients/python/generate.sh
```

**Step 2: Write pyproject.toml**

```toml
[project]
name = "carbonfiles"
version = "0.0.0"
description = "Python SDK for CarbonFiles API"
readme = "README.md"
requires-python = ">=3.10"
license = "MIT"
dependencies = [
    "httpx>=0.27,<1",
    "pydantic>=2.0,<3",
]

[project.optional-dependencies]
events = ["signalr-async>=3.0,<4"]
dev = [
    "pytest>=8.0",
    "pytest-asyncio>=0.24",
    "ruff>=0.4",
]

[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"

[tool.hatch.build.targets.wheel]
packages = ["src/carbonfiles"]

[tool.ruff]
line-length = 120

[tool.ruff.lint]
select = ["F", "I", "UP", "E", "W"]

[tool.pytest.ini_options]
asyncio_mode = "auto"
testpaths = ["tests"]
```

**Step 3: Create package init and py.typed marker**

`src/carbonfiles/__init__.py` — empty for now, will be populated in later tasks.

`src/carbonfiles/py.typed` — empty marker file.

**Step 4: Create empty test conftest**

Create `clients/python/tests/__init__.py` (empty) and `clients/python/tests/conftest.py` (empty for now).

**Step 5: Verify the project structure**

```bash
cd clients/python && pip install -e ".[dev]" && python -c "import carbonfiles; print('OK')"
```

**Step 6: Commit**

```bash
git add clients/python/
git commit -m "feat(py-client): scaffold handcrafted Python SDK project"
```

---

### Task 2: Pydantic models — common and buckets

**Files:**
- Create: `clients/python/src/carbonfiles/models/__init__.py`
- Create: `clients/python/src/carbonfiles/models/common.py`
- Create: `clients/python/src/carbonfiles/models/buckets.py`
- Create: `clients/python/tests/test_serialization.py`

**Step 1: Write common models**

`src/carbonfiles/models/common.py`:

```python
from __future__ import annotations
from typing import Generic, TypeVar
from pydantic import BaseModel

T = TypeVar("T")

class PaginatedResponse(BaseModel, Generic[T]):
    items: list[T]
    total: int
    limit: int
    offset: int

class ErrorResponse(BaseModel):
    error: str
    hint: str | None = None
```

**Step 2: Write bucket models**

`src/carbonfiles/models/buckets.py`:

```python
from __future__ import annotations
from datetime import datetime
from pydantic import BaseModel

class Bucket(BaseModel):
    id: str
    name: str
    owner: str
    description: str | None = None
    created_at: datetime
    expires_at: datetime | None = None
    last_used_at: datetime | None = None
    file_count: int
    total_size: int

class BucketDetail(Bucket):
    unique_content_count: int = 0
    unique_content_size: int = 0
    files: list[BucketFile] | None = None
    has_more_files: bool = False

# Forward ref — BucketFile defined in files.py, import at module level
from carbonfiles.models.files import BucketFile  # noqa: E402
BucketDetail.model_rebuild()
```

Note: The circular import between BucketDetail and BucketFile needs careful handling. Alternative: define BucketDetail without the `files` field type annotation using a forward ref string, then rebuild. The implementer should choose the cleanest approach — likely just importing BucketFile at the bottom of buckets.py or using `from __future__ import annotations` with a TYPE_CHECKING guard.

**Step 3: Write serialization tests for common and bucket models**

`tests/test_serialization.py`:

```python
import pytest
from carbonfiles.models.common import PaginatedResponse, ErrorResponse
from carbonfiles.models.buckets import Bucket

class TestErrorResponse:
    def test_deserialize_with_hint(self):
        data = {"error": "Not found", "hint": "Check the bucket ID"}
        resp = ErrorResponse.model_validate(data)
        assert resp.error == "Not found"
        assert resp.hint == "Check the bucket ID"

    def test_deserialize_without_hint(self):
        data = {"error": "Server error"}
        resp = ErrorResponse.model_validate(data)
        assert resp.hint is None

class TestBucket:
    def test_deserialize_full(self):
        data = {
            "id": "abc1234567",
            "name": "test-bucket",
            "owner": "cf4_test",
            "description": "A test bucket",
            "created_at": "2026-01-01T00:00:00Z",
            "expires_at": "2026-02-01T00:00:00Z",
            "last_used_at": "2026-01-15T00:00:00Z",
            "file_count": 5,
            "total_size": 1024,
        }
        bucket = Bucket.model_validate(data)
        assert bucket.id == "abc1234567"
        assert bucket.name == "test-bucket"
        assert bucket.file_count == 5

    def test_deserialize_minimal(self):
        data = {
            "id": "abc1234567",
            "name": "test",
            "owner": "cf4_test",
            "created_at": "2026-01-01T00:00:00Z",
            "file_count": 0,
            "total_size": 0,
        }
        bucket = Bucket.model_validate(data)
        assert bucket.description is None
        assert bucket.expires_at is None

class TestPaginatedResponse:
    def test_deserialize_bucket_page(self):
        data = {
            "items": [{"id": "b1", "name": "b", "owner": "o", "created_at": "2026-01-01T00:00:00Z", "file_count": 0, "total_size": 0}],
            "total": 1,
            "limit": 50,
            "offset": 0,
        }
        page = PaginatedResponse[Bucket].model_validate(data)
        assert len(page.items) == 1
        assert isinstance(page.items[0], Bucket)
        assert page.total == 1
```

**Step 4: Run tests**

```bash
cd clients/python && python -m pytest tests/test_serialization.py -v
```

**Step 5: Commit**

```bash
git add clients/python/src/carbonfiles/models/ clients/python/tests/test_serialization.py
git commit -m "feat(py-client): add common and bucket pydantic models"
```

---

### Task 3: Pydantic models — files, keys, tokens, stats

**Files:**
- Create: `clients/python/src/carbonfiles/models/files.py`
- Create: `clients/python/src/carbonfiles/models/keys.py`
- Create: `clients/python/src/carbonfiles/models/tokens.py`
- Create: `clients/python/src/carbonfiles/models/stats.py`
- Modify: `clients/python/src/carbonfiles/models/__init__.py` (re-export all)
- Modify: `clients/python/tests/test_serialization.py` (add tests)

**Step 1: Write file models**

`src/carbonfiles/models/files.py`:

```python
from __future__ import annotations
from datetime import datetime
from pydantic import BaseModel

class BucketFile(BaseModel):
    path: str
    name: str
    size: int
    mime_type: str
    short_code: str | None = None
    short_url: str | None = None
    sha256: str | None = None
    created_at: datetime
    updated_at: datetime

class UploadedFile(BaseModel):
    path: str
    name: str
    size: int
    mime_type: str
    short_code: str | None = None
    short_url: str | None = None
    sha256: str | None = None
    deduplicated: bool = False
    created_at: datetime
    updated_at: datetime

class UploadResponse(BaseModel):
    uploaded: list[UploadedFile]

class VerifyResponse(BaseModel):
    path: str
    stored_hash: str
    computed_hash: str
    valid: bool

class DirectoryEntry(BaseModel):
    path: str
    file_count: int
    total_size: int

class FileTreeResponse(BaseModel):
    prefix: str | None = None
    delimiter: str
    directories: list[DirectoryEntry]
    files: list[BucketFile]
    total_files: int
    total_directories: int
    cursor: str | None = None

class DirectoryListingResponse(BaseModel):
    files: list[BucketFile]
    folders: list[str]
    total_files: int
    total_folders: int
    limit: int
    offset: int
```

**Step 2: Write key models**

`src/carbonfiles/models/keys.py`:

```python
from __future__ import annotations
from datetime import datetime
from pydantic import BaseModel

class ApiKeyResponse(BaseModel):
    key: str
    prefix: str
    name: str
    created_at: datetime

class ApiKeyListItem(BaseModel):
    prefix: str
    name: str
    created_at: datetime
    last_used_at: datetime | None = None
    bucket_count: int
    file_count: int
    total_size: int

class ApiKeyUsageResponse(ApiKeyListItem):
    total_downloads: int
    buckets: list  # list[Bucket] — use Any to avoid circular import
```

Note: `ApiKeyUsageResponse.buckets` should be `list[Bucket]`. Handle the import carefully — either import at bottom or use string forward ref with model_rebuild.

**Step 3: Write token models**

`src/carbonfiles/models/tokens.py`:

```python
from __future__ import annotations
from datetime import datetime
from pydantic import BaseModel

class UploadTokenResponse(BaseModel):
    token: str
    bucket_id: str
    expires_at: datetime
    max_uploads: int | None = None
    uploads_used: int

class DashboardTokenResponse(BaseModel):
    token: str
    expires_at: datetime

class DashboardTokenInfo(BaseModel):
    scope: str
    expires_at: datetime
```

**Step 4: Write stats models**

`src/carbonfiles/models/stats.py`:

```python
from __future__ import annotations
from pydantic import BaseModel

class OwnerStats(BaseModel):
    owner: str
    bucket_count: int
    file_count: int
    total_size: int

class StatsResponse(BaseModel):
    total_buckets: int
    total_files: int
    total_size: int
    total_keys: int
    total_downloads: int
    storage_by_owner: list[OwnerStats]

class HealthResponse(BaseModel):
    status: str
    uptime_seconds: int
    db: str
```

**Step 5: Write models/__init__.py re-exports**

```python
from carbonfiles.models.common import *
from carbonfiles.models.buckets import *
from carbonfiles.models.files import *
from carbonfiles.models.keys import *
from carbonfiles.models.tokens import *
from carbonfiles.models.stats import *
```

**Step 6: Add serialization tests for all new models**

Add to `tests/test_serialization.py`:

```python
from carbonfiles.models.files import BucketFile, UploadResponse, VerifyResponse, FileTreeResponse, DirectoryListingResponse
from carbonfiles.models.keys import ApiKeyResponse, ApiKeyListItem, ApiKeyUsageResponse
from carbonfiles.models.tokens import UploadTokenResponse, DashboardTokenResponse, DashboardTokenInfo
from carbonfiles.models.stats import StatsResponse, HealthResponse

class TestBucketFile:
    def test_deserialize_full(self):
        data = {
            "path": "docs/readme.txt", "name": "readme.txt", "size": 256,
            "mime_type": "text/plain", "short_code": "abc123", "short_url": "https://x/s/abc123",
            "sha256": "e3b0c44298fc", "created_at": "2026-01-01T00:00:00Z", "updated_at": "2026-01-01T00:00:00Z",
        }
        f = BucketFile.model_validate(data)
        assert f.path == "docs/readme.txt"
        assert f.sha256 == "e3b0c44298fc"

    def test_deserialize_minimal(self):
        data = {
            "path": "test.txt", "name": "test.txt", "size": 0,
            "mime_type": "text/plain", "created_at": "2026-01-01T00:00:00Z", "updated_at": "2026-01-01T00:00:00Z",
        }
        f = BucketFile.model_validate(data)
        assert f.short_code is None
        assert f.sha256 is None

class TestUploadResponse:
    def test_deserialize_with_dedup(self):
        data = {"uploaded": [{"path": "f.txt", "name": "f.txt", "size": 10, "mime_type": "text/plain",
                              "sha256": "abc", "deduplicated": True,
                              "created_at": "2026-01-01T00:00:00Z", "updated_at": "2026-01-01T00:00:00Z"}]}
        r = UploadResponse.model_validate(data)
        assert r.uploaded[0].deduplicated is True
        assert r.uploaded[0].sha256 == "abc"

class TestVerifyResponse:
    def test_deserialize(self):
        data = {"path": "f.txt", "stored_hash": "abc", "computed_hash": "abc", "valid": True}
        r = VerifyResponse.model_validate(data)
        assert r.valid is True

class TestFileTreeResponse:
    def test_deserialize(self):
        data = {"prefix": "docs/", "delimiter": "/",
                "directories": [{"path": "docs/sub/", "file_count": 3, "total_size": 1024}],
                "files": [{"path": "docs/r.md", "name": "r.md", "size": 256, "mime_type": "text/markdown",
                           "created_at": "2026-01-01T00:00:00Z", "updated_at": "2026-01-01T00:00:00Z"}],
                "total_files": 1, "total_directories": 1, "cursor": "next"}
        r = FileTreeResponse.model_validate(data)
        assert r.directories[0].file_count == 3
        assert r.cursor == "next"

class TestDirectoryListingResponse:
    def test_deserialize(self):
        data = {"files": [], "folders": ["sub1", "sub2"], "total_files": 0, "total_folders": 2, "limit": 200, "offset": 0}
        r = DirectoryListingResponse.model_validate(data)
        assert r.folders == ["sub1", "sub2"]

class TestApiKeyModels:
    def test_api_key_response(self):
        data = {"key": "cf4_abc_secret", "prefix": "cf4_abc", "name": "test", "created_at": "2026-01-01T00:00:00Z"}
        r = ApiKeyResponse.model_validate(data)
        assert r.prefix == "cf4_abc"

    def test_api_key_list_item(self):
        data = {"prefix": "cf4_abc", "name": "test", "created_at": "2026-01-01T00:00:00Z",
                "bucket_count": 2, "file_count": 10, "total_size": 5000}
        r = ApiKeyListItem.model_validate(data)
        assert r.bucket_count == 2
        assert r.last_used_at is None

class TestTokenModels:
    def test_upload_token(self):
        data = {"token": "cfu_abc", "bucket_id": "b1", "expires_at": "2026-02-01T00:00:00Z", "max_uploads": 10, "uploads_used": 0}
        r = UploadTokenResponse.model_validate(data)
        assert r.max_uploads == 10

    def test_dashboard_token(self):
        data = {"token": "eyJ...", "expires_at": "2026-01-02T00:00:00Z"}
        r = DashboardTokenResponse.model_validate(data)
        assert r.token.startswith("eyJ")

    def test_dashboard_info(self):
        data = {"scope": "admin", "expires_at": "2026-01-02T00:00:00Z"}
        r = DashboardTokenInfo.model_validate(data)
        assert r.scope == "admin"

class TestStatsModels:
    def test_stats_response(self):
        data = {"total_buckets": 5, "total_files": 100, "total_size": 50000, "total_keys": 3,
                "total_downloads": 200, "storage_by_owner": [{"owner": "cf4_abc", "bucket_count": 2, "file_count": 50, "total_size": 25000}]}
        r = StatsResponse.model_validate(data)
        assert r.total_buckets == 5
        assert r.storage_by_owner[0].owner == "cf4_abc"

    def test_health_response(self):
        data = {"status": "healthy", "uptime_seconds": 3600, "db": "ok"}
        r = HealthResponse.model_validate(data)
        assert r.status == "healthy"
```

**Step 7: Run tests**

```bash
cd clients/python && python -m pytest tests/test_serialization.py -v
```

**Step 8: Commit**

```bash
git add clients/python/src/carbonfiles/models/ clients/python/tests/test_serialization.py
git commit -m "feat(py-client): add file, key, token, and stats models"
```

---

### Task 4: Exceptions and transport layer

**Files:**
- Create: `clients/python/src/carbonfiles/exceptions.py`
- Create: `clients/python/src/carbonfiles/transport.py`
- Create: `clients/python/tests/test_transport.py`

**Step 1: Write exceptions**

`src/carbonfiles/exceptions.py`:

```python
class CarbonFilesError(Exception):
    def __init__(self, status_code: int, error: str, hint: str | None = None):
        self.status_code = status_code
        self.error = error
        self.hint = hint
        super().__init__(f"{status_code}: {error}" + (f" ({hint})" if hint else ""))
```

**Step 2: Write transport layer**

`src/carbonfiles/transport.py` — two classes:

- `SyncTransport`: wraps `httpx.Client`. Methods: `get`, `get_string`, `get_bytes`, `get_stream`, `post`, `patch`, `delete`, `put_stream`, `send_raw`. All take `path: str` and return parsed JSON via pydantic or raw content.
- `AsyncTransport`: wraps `httpx.AsyncClient`. Same methods but async.

Both share:
- `_build_url(path, query)` static helper (mirrors C# `BuildUrl`)
- `_handle_error(response)` that parses `{"error": "...", "hint": "..."}` into `CarbonFilesError`
- Bearer auth header injection
- JSON serialization/deserialization helpers

Key transport methods (mirroring C# HttpTransport):

```python
# Sync
def get(self, path: str, model_type: type[T], query: dict | None = None) -> T
def get_string(self, path: str) -> str
def get_bytes(self, path: str) -> bytes
def post(self, path: str, body: BaseModel, model_type: type[T]) -> T
def post_no_response(self, path: str, body: BaseModel) -> None
def patch(self, path: str, body: BaseModel, model_type: type[T]) -> T
def delete(self, path: str) -> None
def put_stream(self, path: str, content: bytes | BinaryIO, model_type: type[T], headers: dict | None = None) -> T
def send_raw(self, method: str, path: str, content: bytes | BinaryIO | None = None, headers: dict | None = None) -> httpx.Response

# build_url is static
@staticmethod
def build_url(path: str, query: dict[str, str | None] | None) -> str
```

Upload progress: For `put_stream`, wrap the content in a custom generator that reports progress via callback. httpx supports streaming request bodies via iterators.

**Step 3: Write transport tests**

`tests/test_transport.py` — use httpx `MockTransport`:

```python
import httpx
import pytest
from carbonfiles.transport import SyncTransport, AsyncTransport
from carbonfiles.exceptions import CarbonFilesError
from carbonfiles.models.buckets import Bucket

def make_response(status_code=200, json_body=None, text_body=None):
    """Helper to create mock httpx responses."""
    ...

class TestSyncTransport:
    def test_get_deserializes_json(self):
        # Mock returns bucket JSON, transport.get should return Bucket model
        ...

    def test_get_with_query_params(self):
        # Verify query string is built correctly
        ...

    def test_auth_header_injected(self):
        # Verify Authorization: Bearer header is set
        ...

    def test_auth_header_omitted_when_no_key(self):
        # No api_key means no Authorization header
        ...

    def test_error_response_raises_carbon_files_error(self):
        # 404 with {"error": "...", "hint": "..."} raises CarbonFilesError
        ...

    def test_error_without_json_body(self):
        # Non-JSON error body still raises CarbonFilesError with raw text
        ...

    def test_get_string_returns_text(self):
        ...

    def test_get_bytes_returns_content(self):
        ...

    def test_post_serializes_body_and_deserializes_response(self):
        ...

    def test_delete_sends_delete_request(self):
        ...

    def test_patch_sends_patch_request(self):
        ...

    def test_put_stream_sends_put_with_content(self):
        ...

    def test_build_url_with_no_query(self):
        assert SyncTransport.build_url("/api/buckets", None) == "/api/buckets"

    def test_build_url_skips_none_values(self):
        assert SyncTransport.build_url("/api/buckets", {"limit": "10", "offset": None}) == "/api/buckets?limit=10"

    def test_build_url_encodes_values(self):
        url = SyncTransport.build_url("/path", {"q": "hello world"})
        assert "hello+world" in url or "hello%20world" in url

class TestAsyncTransport:
    async def test_get_deserializes_json(self):
        # Same tests but async
        ...

    async def test_error_response_raises(self):
        ...
```

Use httpx's `MockTransport` to intercept requests:

```python
def mock_transport(handler):
    return httpx.MockTransport(handler)

def test_get_sends_auth_header():
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.headers["authorization"] == "Bearer test-key"
        return httpx.Response(200, json={"id": "b1", ...})

    transport = SyncTransport("https://example.com", "test-key",
                              http_client=httpx.Client(transport=mock_transport(handler)))
    ...
```

**Step 4: Run tests**

```bash
cd clients/python && python -m pytest tests/test_transport.py -v
```

**Step 5: Commit**

```bash
git add clients/python/src/carbonfiles/exceptions.py clients/python/src/carbonfiles/transport.py clients/python/tests/test_transport.py
git commit -m "feat(py-client): add transport layer and exceptions"
```

---

### Task 5: Bucket resources

**Files:**
- Create: `clients/python/src/carbonfiles/resources/__init__.py`
- Create: `clients/python/src/carbonfiles/resources/buckets.py`
- Create: `clients/python/tests/test_buckets.py`

**Step 1: Write bucket resources**

`src/carbonfiles/resources/buckets.py`:

Two classes:
- `BucketsResource` — collection operations: `create()`, `list()`, `list_all()`, `__getitem__(id)`
- `BucketResource` — single bucket: `get()`, `update()`, `delete()`, `summary()`, `download_zip()`, plus `files` and `tokens` properties

```python
class BucketsResource:
    def __init__(self, transport):
        self._transport = transport

    def __getitem__(self, bucket_id: str) -> BucketResource:
        return BucketResource(self._transport, bucket_id)

    def create(self, name: str, *, description: str | None = None, expires: str | None = None) -> Bucket:
        body = {"name": name}
        if description is not None: body["description"] = description
        if expires is not None: body["expires_in"] = expires
        return self._transport.post("/api/buckets", body, Bucket)

    def list(self, *, limit: int | None = None, offset: int | None = None,
             sort: str | None = None, order: str | None = None,
             include_expired: bool | None = None) -> PaginatedResponse[Bucket]:
        query = {}
        if limit is not None: query["limit"] = str(limit)
        if offset is not None: query["offset"] = str(offset)
        if sort is not None: query["sort"] = sort
        if order is not None: query["order"] = order
        if include_expired is not None: query["include_expired"] = str(include_expired).lower()
        return self._transport.get(self._transport.build_url("/api/buckets", query), PaginatedResponse[Bucket])

    def list_all(self, *, limit: int = 50) -> Generator[PaginatedResponse[Bucket], None, None]:
        offset = 0
        while True:
            page = self.list(limit=limit, offset=offset)
            yield page
            if offset + limit >= page.total:
                break
            offset += limit

class BucketResource:
    def __init__(self, transport, bucket_id: str):
        self._transport = transport
        self._bucket_id = bucket_id
        self._base = f"/api/buckets/{urllib.parse.quote(bucket_id, safe='')}"

    @property
    def files(self) -> FilesResource:
        return FilesResource(self._transport, self._bucket_id)

    @property
    def tokens(self) -> UploadTokensResource:
        return UploadTokensResource(self._transport, self._bucket_id)

    def get(self, *, include_files: bool = False) -> BucketDetail:
        query = {"include": "files"} if include_files else None
        return self._transport.get(self._transport.build_url(self._base, query), BucketDetail)

    def update(self, *, name: str | None = None, description: str | None = None, expires: str | None = None) -> Bucket:
        body = {}
        if name is not None: body["name"] = name
        if description is not None: body["description"] = description
        if expires is not None: body["expires_in"] = expires
        return self._transport.patch(self._base, body, Bucket)

    def delete(self) -> None:
        self._transport.delete(self._base)

    def summary(self) -> str:
        return self._transport.get_string(f"{self._base}/summary")

    def download_zip(self) -> bytes:
        return self._transport.get_bytes(f"{self._base}/zip")
```

**Step 2: Write bucket tests**

`tests/test_buckets.py`:

```python
class TestBucketsResource:
    def test_create_sends_post(self): ...
    def test_create_with_all_options(self): ...
    def test_list_with_pagination(self): ...
    def test_list_default_params(self): ...
    def test_list_all_auto_paginates(self): ...

class TestBucketResource:
    def test_get_returns_detail(self): ...
    def test_get_with_include_files(self): ...
    def test_update_sends_patch(self): ...
    def test_delete_sends_delete(self): ...
    def test_summary_returns_string(self): ...
    def test_download_zip_returns_bytes(self): ...
    def test_files_property_returns_files_resource(self): ...
    def test_tokens_property_returns_tokens_resource(self): ...
    def test_special_chars_in_bucket_id_escaped(self): ...
```

Use httpx MockTransport pattern from Task 4 tests.

**Step 3: Run tests**

```bash
cd clients/python && python -m pytest tests/test_buckets.py -v
```

**Step 4: Commit**

```bash
git add clients/python/src/carbonfiles/resources/ clients/python/tests/test_buckets.py
git commit -m "feat(py-client): add bucket resources with tests"
```

---

### Task 6: File resources

**Files:**
- Create: `clients/python/src/carbonfiles/resources/files.py`
- Create: `clients/python/tests/test_files.py`
- Create: `clients/python/tests/test_uploads.py`

**Step 1: Write file resources**

`src/carbonfiles/resources/files.py`:

Two classes:
- `FilesResource` — collection: `list()`, `list_tree()`, `list_directory()`, `upload()`, `__getitem__(path)`
- `FileResource` — single file: `metadata()`, `download()`, `download_to()`, `delete()`, `verify()`, `append()`, `patch()`

Upload implementation:
- Accept `str | Path | bytes | BinaryIO` as source
- If str/Path: open file, derive filename from path if not provided
- If bytes: wrap in BytesIO
- If BinaryIO: use directly, require filename param
- Progress: wrap content in a generator that calls callback after each chunk
- Upload token: pass as `?token=` query param
- HTTP method: PUT to `/api/buckets/{id}/upload/stream?filename={name}`

Append implementation:
- PATCH to `/api/buckets/{id}/files/{path}/content` with `X-Append: true` header
- Accept `bytes | BinaryIO`

Patch (byte-range) implementation:
- PATCH to `/api/buckets/{id}/files/{path}/content` with `Content-Range: bytes start-end/total` header
- Accept `bytes | BinaryIO`

download_to implementation:
- Call download() to get bytes, write to file path
- Accept str or Path

**Step 2: Write file tests**

`tests/test_files.py`:

```python
class TestFilesResource:
    def test_list_with_pagination(self): ...
    def test_list_tree_with_delimiter(self): ...
    def test_list_tree_with_cursor(self): ...
    def test_list_directory_with_path(self): ...

class TestFileResource:
    def test_metadata_returns_bucket_file(self): ...
    def test_download_returns_bytes(self): ...
    def test_download_to_writes_file(self): ...  # use tmp_path fixture
    def test_delete_sends_delete(self): ...
    def test_verify_returns_verify_response(self): ...
    def test_verify_throws_on_not_found(self): ...
    def test_append_sends_patch_with_x_append(self): ...
    def test_patch_sends_content_range_header(self): ...
    def test_patch_throws_on_not_found(self): ...
    def test_special_chars_in_path_escaped(self): ...
    def test_metadata_deserializes_sha256(self): ...
```

`tests/test_uploads.py`:

```python
class TestUpload:
    def test_upload_bytes_sends_put(self): ...
    def test_upload_file_path_derives_filename(self): ...  # use tmp_path
    def test_upload_file_path_overrides_filename(self): ...
    def test_upload_binary_io(self): ...
    def test_upload_with_progress_reports(self): ...
    def test_upload_with_upload_token_in_query(self): ...
    def test_upload_dedup_fields_deserialized(self): ...
```

**Step 3: Run tests**

```bash
cd clients/python && python -m pytest tests/test_files.py tests/test_uploads.py -v
```

**Step 4: Commit**

```bash
git add clients/python/src/carbonfiles/resources/files.py clients/python/tests/test_files.py clients/python/tests/test_uploads.py
git commit -m "feat(py-client): add file resources with upload and download"
```

---

### Task 7: Admin resources (keys, stats, short URLs, dashboard)

**Files:**
- Create: `clients/python/src/carbonfiles/resources/keys.py`
- Create: `clients/python/src/carbonfiles/resources/stats.py`
- Create: `clients/python/src/carbonfiles/resources/short_urls.py`
- Create: `clients/python/src/carbonfiles/resources/dashboard.py`
- Create: `clients/python/tests/test_admin.py`

**Step 1: Write key resources**

`src/carbonfiles/resources/keys.py`:

- `KeysResource` — `create(name)`, `list(...)`, `__getitem__(prefix)`
- `KeyResource` — `delete()`, `usage()`

**Step 2: Write stats, short URL, dashboard resources**

- `StatsResource` — `get()`
- `ShortUrlsResource` — `__getitem__(code)` → `ShortUrlResource`
- `ShortUrlResource` — `delete()`
- `DashboardResource` — `create_token(expires?)`, `current_user()`

**Step 3: Write upload tokens resource**

Add to `src/carbonfiles/resources/buckets.py` or a separate file:

- `UploadTokensResource` — `create(expires?, max_uploads?)`

**Step 4: Write admin tests**

`tests/test_admin.py`:

```python
class TestKeysResource:
    def test_create_sends_post(self): ...
    def test_list_with_pagination(self): ...

class TestKeyResource:
    def test_delete_sends_delete(self): ...
    def test_usage_returns_response(self): ...

class TestStatsResource:
    def test_get_returns_stats(self): ...

class TestShortUrlResource:
    def test_delete_sends_delete(self): ...

class TestDashboardResource:
    def test_create_token(self): ...
    def test_create_token_with_expiry(self): ...
    def test_current_user(self): ...

class TestUploadTokensResource:
    def test_create_sends_post(self): ...
    def test_create_with_options(self): ...
```

**Step 5: Run tests**

```bash
cd clients/python && python -m pytest tests/test_admin.py -v
```

**Step 6: Commit**

```bash
git add clients/python/src/carbonfiles/resources/ clients/python/tests/test_admin.py
git commit -m "feat(py-client): add admin resources (keys, stats, dashboard, short URLs)"
```

---

### Task 8: Sync and async client classes

**Files:**
- Create: `clients/python/src/carbonfiles/client.py`
- Create: `clients/python/src/carbonfiles/async_client.py`
- Modify: `clients/python/src/carbonfiles/__init__.py`

**Step 1: Write sync client**

`src/carbonfiles/client.py`:

```python
class CarbonFiles:
    def __init__(self, base_url: str, api_key: str | None = None):
        self._transport = SyncTransport(base_url, api_key)

    @property
    def buckets(self) -> BucketsResource: ...
    @property
    def keys(self) -> KeysResource: ...
    @property
    def stats(self) -> StatsResource: ...
    @property
    def short_urls(self) -> ShortUrlsResource: ...
    @property
    def dashboard(self) -> DashboardResource: ...
    @property
    def events(self) -> CarbonFilesEvents: ...

    def health(self) -> HealthResponse: ...

    def close(self) -> None:
        self._transport.close()

    def __enter__(self) -> CarbonFiles: return self
    def __exit__(self, *args) -> None: self.close()
```

**Step 2: Write async client**

`src/carbonfiles/async_client.py`:

```python
class AsyncCarbonFiles:
    def __init__(self, base_url: str, api_key: str | None = None):
        self._transport = AsyncTransport(base_url, api_key)

    # Same properties but using async transport
    # All resource methods become async (because transport methods are async)

    async def health(self) -> HealthResponse: ...

    async def close(self) -> None:
        await self._transport.close()

    async def __aenter__(self) -> AsyncCarbonFiles: return self
    async def __aexit__(self, *args) -> None: await self.close()
```

**Step 3: Update __init__.py exports**

```python
from carbonfiles.client import CarbonFiles
from carbonfiles.async_client import AsyncCarbonFiles
from carbonfiles.exceptions import CarbonFilesError
from carbonfiles.models import *

__all__ = ["CarbonFiles", "AsyncCarbonFiles", "CarbonFilesError"]
```

**Step 4: Run all tests**

```bash
cd clients/python && python -m pytest -v
```

**Step 5: Commit**

```bash
git add clients/python/src/carbonfiles/
git commit -m "feat(py-client): add sync and async client entry points"
```

---

### Task 9: Pagination helper (list_all)

**Files:**
- Modify: `clients/python/src/carbonfiles/resources/buckets.py` (if not already done)
- Modify: `clients/python/src/carbonfiles/resources/files.py`
- Create: `clients/python/tests/test_pagination.py`

**Step 1: Implement list_all on BucketsResource and FilesResource**

Both sync and async versions. Sync returns `Generator[PaginatedResponse[T]]`, async returns `AsyncGenerator[PaginatedResponse[T]]`.

Pattern:
```python
def list_all(self, *, limit: int = 50) -> Generator[PaginatedResponse[Bucket], None, None]:
    offset = 0
    while True:
        page = self.list(limit=limit, offset=offset)
        yield page
        if offset + limit >= page.total:
            break
        offset += limit
```

**Step 2: Write pagination tests**

`tests/test_pagination.py`:

```python
class TestPagination:
    def test_list_all_single_page(self):
        # total <= limit, yields one page
        ...

    def test_list_all_multiple_pages(self):
        # Mock returns 3 pages, verify all yielded
        ...

    def test_list_all_empty(self):
        # total=0, yields one empty page
        ...

    def test_list_all_custom_limit(self):
        # Verify limit param is respected
        ...
```

**Step 3: Run tests**

```bash
cd clients/python && python -m pytest tests/test_pagination.py -v
```

**Step 4: Commit**

```bash
git add clients/python/src/carbonfiles/resources/ clients/python/tests/test_pagination.py
git commit -m "feat(py-client): add auto-pagination with list_all generators"
```

---

### Task 10: SignalR events

**Files:**
- Create: `clients/python/src/carbonfiles/events.py`
- Create: `clients/python/tests/test_events.py`

**Step 1: Write events module**

`src/carbonfiles/events.py`:

```python
class CarbonFilesEvents:
    """SignalR real-time event handler for CarbonFiles.

    Requires the 'events' extra: pip install carbonfiles[events]
    """
    def __init__(self, base_url: str, api_key: str | None = None):
        self._base_url = base_url
        self._api_key = api_key
        self._handlers: dict[str, list[Callable]] = {}
        self._connection = None

    def connect(self) -> None: ...
    def disconnect(self) -> None: ...
    def subscribe_bucket(self, bucket_id: str) -> None: ...
    def unsubscribe_bucket(self, bucket_id: str) -> None: ...
    def subscribe_file(self, bucket_id: str, path: str) -> None: ...
    def unsubscribe_file(self, bucket_id: str, path: str) -> None: ...
    def subscribe_all(self) -> None: ...
    def unsubscribe_all(self) -> None: ...

    def on_file_created(self, handler: Callable) -> Callable:
        """Register handler. Can be used as decorator. Returns unsubscribe callable."""
        ...

    def on_file_updated(self, handler: Callable) -> Callable: ...
    def on_file_deleted(self, handler: Callable) -> Callable: ...
    def on_bucket_created(self, handler: Callable) -> Callable: ...
    def on_bucket_updated(self, handler: Callable) -> Callable: ...
    def on_bucket_deleted(self, handler: Callable) -> Callable: ...
```

If `signalr-async` is not installed, raise `ImportError` with helpful message on `connect()`.

**Step 2: Write event tests**

`tests/test_events.py`:

```python
class TestCarbonFilesEvents:
    def test_on_file_created_registers_handler(self): ...
    def test_on_file_created_as_decorator(self): ...
    def test_handler_returns_unsubscribe(self): ...
    def test_all_six_event_types_registrable(self): ...
    def test_connect_without_signalr_raises(self): ...  # mock import failure
```

**Step 3: Run tests**

```bash
cd clients/python && python -m pytest tests/test_events.py -v
```

**Step 4: Commit**

```bash
git add clients/python/src/carbonfiles/events.py clients/python/tests/test_events.py
git commit -m "feat(py-client): add SignalR event handling"
```

---

### Task 11: README

**Files:**
- Create: `clients/python/README.md` (replace existing)

**Step 1: Write README**

Match the quality of the C# client README. Sections:

1. Installation (`pip install carbonfiles`, `pip install carbonfiles[events]`)
2. Quick Start (sync + async examples)
3. Authentication (4 token types)
4. Buckets (CRUD, summary, zip)
5. Files (list, upload, download, metadata, delete, append, patch, verify)
6. Upload types (path, bytes, BinaryIO, progress callback)
7. Pagination (single page + list_all auto-pagination)
8. API Keys (admin)
9. Dashboard Tokens (admin)
10. Upload Tokens
11. Stats
12. Short URLs
13. Real-Time Events (SignalR)
14. Error Handling
15. Async Usage (full async example)

**Step 2: Commit**

```bash
git add clients/python/README.md
git commit -m "docs(py-client): add comprehensive README"
```

---

### Task 12: Update CI workflow and final cleanup

**Files:**
- Modify: `.github/workflows/publish-clients.yml` (lines 114-156)
- Modify: `clients/python/.gitignore`
- Modify: `CLAUDE.md` (update Python client entry in table)

**Step 1: Update CI publish job**

Replace the generate-based Python publish job with a simple build-and-publish:

```yaml
publish-python:
  needs: export-spec
  runs-on: ubuntu-latest
  permissions:
    contents: read
    id-token: write
  defaults:
    run:
      working-directory: clients/python
  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-python@v5
      with:
        python-version: '3.12'
    - name: Build
      run: |
        pip install build
        sed -i "s/^version = .*/version = \"${{ needs.export-spec.outputs.version }}\"/" pyproject.toml
        python -m build
    - name: Publish
      uses: pypa/gh-action-pypi-publish@release/v1
      with:
        packages-dir: clients/python/dist/
```

**Step 2: Update .gitignore**

```
__pycache__/
*.pyc
dist/
build/
*.egg-info/
.pytest_cache/
```

**Step 3: Update CLAUDE.md Python client entry**

Change from `openapi-python-client` generator to `Hand-crafted`.

**Step 4: Run full test suite**

```bash
cd clients/python && python -m pytest -v --tb=short
```

**Step 5: Commit**

```bash
git add .github/workflows/publish-clients.yml clients/python/.gitignore CLAUDE.md
git commit -m "chore(py-client): update CI workflow and cleanup for handcrafted SDK"
```

---

### Task 13: Final verification

**Step 1: Run full test suite and check count**

```bash
cd clients/python && python -m pytest -v --tb=short 2>&1 | tail -5
```

Target: 80+ tests passing.

**Step 2: Verify package builds**

```bash
cd clients/python && pip install build && python -m build
```

**Step 3: Verify import works**

```bash
python -c "from carbonfiles import CarbonFiles, AsyncCarbonFiles, CarbonFilesError; print('All exports OK')"
```

**Step 4: Lint**

```bash
cd clients/python && ruff check src/ tests/ && ruff format --check src/ tests/
```

**Step 5: Fix any issues found, commit if needed**

```bash
git add -A clients/python/ && git commit -m "fix(py-client): final lint and test fixes"
```
