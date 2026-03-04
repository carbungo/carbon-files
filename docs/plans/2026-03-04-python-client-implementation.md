# Handcrafted Python SDK Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the auto-generated Python client with a handcrafted, idiomatic SDK matching the C# client's API surface.

**Architecture:** Shared transport pattern — `SyncTransport` (httpx.Client) and `AsyncTransport` (httpx.AsyncClient) with paired resource classes. Pydantic v2 models. Fluent resource tree with `__getitem__` indexers.

**Tech Stack:** Python 3.10+, httpx, pydantic v2, pytest, pytest-asyncio, ruff

**Design doc:** `docs/plans/2026-03-04-python-client-design.md`

**Reference:** C# client at `clients/csharp/` — match its API surface exactly (minus SignalR events, deferred to v2).

---

## Task 0: Project Scaffold

**Files:**
- Delete: `clients/python/carbonfiles_client/` (entire auto-generated package)
- Delete: `clients/python/config.yml`, `clients/python/generate.sh`
- Create: `clients/python/pyproject.toml` (replace existing)
- Create: `clients/python/src/carbonfiles/__init__.py`
- Create: `clients/python/src/carbonfiles/py.typed`
- Create: `clients/python/tests/__init__.py`
- Create: `clients/python/tests/conftest.py`

**Step 1: Remove auto-generated code**

```bash
rm -rf clients/python/carbonfiles_client clients/python/config.yml clients/python/generate.sh
```

**Step 2: Create new pyproject.toml**

```toml
[project]
name = "carbonfiles"
version = "0.0.0"
description = "Handcrafted Python client for the CarbonFiles file-sharing API"
readme = "README.md"
requires-python = ">=3.10"
license = "MIT"
dependencies = [
    "httpx>=0.24.0,<1.0",
    "pydantic>=2.0,<3.0",
]

[project.optional-dependencies]
dev = [
    "pytest>=7.0",
    "pytest-asyncio>=0.23",
    "ruff>=0.4",
]

[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"

[tool.hatch.build.targets.wheel]
packages = ["src/carbonfiles"]

[tool.ruff]
line-length = 120
src = ["src"]

[tool.ruff.lint]
select = ["F", "I", "UP", "E", "W"]

[tool.pytest.ini_options]
asyncio_mode = "auto"
testpaths = ["tests"]
```

**Step 3: Create package skeleton**

Create `clients/python/src/carbonfiles/__init__.py`:
```python
"""CarbonFiles Python SDK."""

from carbonfiles._transport import CarbonFilesError

__all__ = ["CarbonFilesError"]
```

Create `clients/python/src/carbonfiles/py.typed` (empty file — PEP 561 marker).

Create `clients/python/tests/__init__.py` (empty file).

Create `clients/python/tests/conftest.py`:
```python
"""Shared test fixtures."""
```

**Step 4: Verify the scaffold builds**

```bash
cd clients/python && pip install -e ".[dev]" && cd ../..
```

Expected: Installs successfully (will fail on import until we create `_transport.py`).

**Step 5: Commit**

```bash
git add clients/python/
git commit -m "chore(python): scaffold handcrafted SDK, remove auto-generated client"
```

---

## Task 1: Transport Layer + Error Handling

**Files:**
- Create: `clients/python/src/carbonfiles/_transport.py`
- Create: `clients/python/tests/test_transport.py`

**Step 1: Write transport tests**

Create `clients/python/tests/test_transport.py`:

```python
"""Tests for the transport layer."""

import json

import httpx
import pytest

from carbonfiles._transport import AsyncTransport, CarbonFilesError, SyncTransport


def _mock_transport(responses: list[tuple[int, dict | str]]) -> httpx.MockTransport:
    """Create an httpx MockTransport that returns queued responses."""
    queue = list(responses)

    def handler(request: httpx.Request) -> httpx.Response:
        status, body = queue.pop(0)
        if isinstance(body, dict):
            return httpx.Response(status, json=body)
        return httpx.Response(status, text=body)

    return handler


class TestSyncTransport:
    def test_request_sends_auth_header(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.headers["authorization"] == "Bearer test-key"
            return httpx.Response(200, json={"status": "ok"})

        transport = SyncTransport("https://example.com", api_key="test-key", http_transport=handler)
        resp = transport.request("GET", "/healthz")
        assert resp.status_code == 200

    def test_request_no_auth_header_when_no_key(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert "authorization" not in request.headers
            return httpx.Response(200, json={"status": "ok"})

        transport = SyncTransport("https://example.com", http_transport=handler)
        transport.request("GET", "/healthz")

    def test_request_raises_on_4xx(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(404, json={"error": "Bucket not found", "hint": "Check the ID"})

        transport = SyncTransport("https://example.com", http_transport=handler)
        with pytest.raises(CarbonFilesError) as exc_info:
            transport.request("GET", "/api/buckets/bad")
        assert exc_info.value.status_code == 404
        assert exc_info.value.error == "Bucket not found"
        assert exc_info.value.hint == "Check the ID"

    def test_request_raises_on_5xx(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(500, json={"error": "Internal error"})

        transport = SyncTransport("https://example.com", http_transport=handler)
        with pytest.raises(CarbonFilesError) as exc_info:
            transport.request("GET", "/api/stats")
        assert exc_info.value.status_code == 500

    def test_request_raises_on_non_json_error(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(502, text="Bad Gateway")

        transport = SyncTransport("https://example.com", http_transport=handler)
        with pytest.raises(CarbonFilesError) as exc_info:
            transport.request("GET", "/api/stats")
        assert exc_info.value.status_code == 502
        assert "Bad Gateway" in exc_info.value.error

    def test_request_sends_json_body(self):
        def handler(request: httpx.Request) -> httpx.Response:
            body = json.loads(request.content)
            assert body == {"name": "test"}
            return httpx.Response(201, json={"id": "abc"})

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resp = transport.request("POST", "/api/buckets", json={"name": "test"})
        assert resp.json()["id"] == "abc"

    def test_request_sends_query_params(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.params["limit"] == "20"
            assert request.url.params["offset"] == "0"
            return httpx.Response(200, json={"items": [], "total": 0, "limit": 20, "offset": 0})

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        transport.request("GET", "/api/buckets", params={"limit": "20", "offset": "0"})

    def test_request_sends_custom_headers(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.headers["x-append"] == "true"
            return httpx.Response(200, json={})

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        transport.request("PATCH", "/api/files", headers={"X-Append": "true"})

    def test_close(self):
        transport = SyncTransport("https://example.com")
        transport.close()  # Should not raise

    def test_request_with_content_bytes(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.content == b"hello"
            return httpx.Response(200, json={"path": "test.txt"})

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        transport.request("PUT", "/upload", content=b"hello")

    def test_get_stream(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, content=b"file-bytes")

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resp = transport.request("GET", "/download")
        assert resp.content == b"file-bytes"

    def test_get_text(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, text="plain text summary")

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resp = transport.request("GET", "/summary")
        assert resp.text == "plain text summary"


class TestAsyncTransport:
    @pytest.mark.asyncio
    async def test_request_sends_auth_header(self):
        async def handler(request: httpx.Request) -> httpx.Response:
            assert request.headers["authorization"] == "Bearer test-key"
            return httpx.Response(200, json={"status": "ok"})

        transport = AsyncTransport("https://example.com", api_key="test-key", http_transport=handler)
        resp = await transport.request("GET", "/healthz")
        assert resp.status_code == 200

    @pytest.mark.asyncio
    async def test_request_raises_on_4xx(self):
        async def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(404, json={"error": "Not found"})

        transport = AsyncTransport("https://example.com", http_transport=handler)
        with pytest.raises(CarbonFilesError) as exc_info:
            await transport.request("GET", "/api/buckets/bad")
        assert exc_info.value.status_code == 404

    @pytest.mark.asyncio
    async def test_aclose(self):
        transport = AsyncTransport("https://example.com")
        await transport.aclose()


class TestCarbonFilesError:
    def test_str_with_hint(self):
        err = CarbonFilesError(404, "Not found", "Check ID")
        assert str(err) == "Not found (Check ID)"

    def test_str_without_hint(self):
        err = CarbonFilesError(500, "Server error")
        assert str(err) == "Server error"
```

**Step 2: Run tests to verify they fail**

```bash
cd clients/python && python -m pytest tests/test_transport.py -v
```

Expected: FAIL — `_transport` module doesn't exist yet.

**Step 3: Implement the transport layer**

Create `clients/python/src/carbonfiles/_transport.py`:

```python
"""HTTP transport layer for CarbonFiles SDK."""

from __future__ import annotations

import json as _json
from typing import Any

import httpx


class CarbonFilesError(Exception):
    """Error returned by the CarbonFiles API."""

    def __init__(self, status_code: int, error: str, hint: str | None = None) -> None:
        self.status_code = status_code
        self.error = error
        self.hint = hint
        msg = f"{error} ({hint})" if hint else error
        super().__init__(msg)


def _raise_for_status(resp: httpx.Response) -> None:
    """Raise CarbonFilesError if the response indicates an error."""
    if resp.status_code < 400:
        return
    try:
        body = resp.json()
        raise CarbonFilesError(
            resp.status_code,
            body.get("error", "Unknown error"),
            body.get("hint"),
        )
    except (_json.JSONDecodeError, UnicodeDecodeError):
        raise CarbonFilesError(resp.status_code, resp.text or "Unknown error")


class SyncTransport:
    """Synchronous HTTP transport wrapping httpx.Client."""

    def __init__(
        self,
        base_url: str,
        api_key: str | None = None,
        timeout: float = 30.0,
        http_transport: Any = None,
    ) -> None:
        headers: dict[str, str] = {}
        if api_key:
            headers["Authorization"] = f"Bearer {api_key}"
        kwargs: dict[str, Any] = {"base_url": base_url, "timeout": timeout, "headers": headers}
        if http_transport is not None:
            kwargs["transport"] = httpx.MockTransport(http_transport)
        self._client = httpx.Client(**kwargs)

    def request(
        self,
        method: str,
        path: str,
        *,
        json: Any = None,
        params: dict[str, str] | None = None,
        content: bytes | Any | None = None,
        headers: dict[str, str] | None = None,
    ) -> httpx.Response:
        resp = self._client.request(
            method, path, json=json, params=params, content=content, headers=headers or {},
        )
        _raise_for_status(resp)
        return resp

    def close(self) -> None:
        self._client.close()


class AsyncTransport:
    """Asynchronous HTTP transport wrapping httpx.AsyncClient."""

    def __init__(
        self,
        base_url: str,
        api_key: str | None = None,
        timeout: float = 30.0,
        http_transport: Any = None,
    ) -> None:
        headers: dict[str, str] = {}
        if api_key:
            headers["Authorization"] = f"Bearer {api_key}"
        kwargs: dict[str, Any] = {"base_url": base_url, "timeout": timeout, "headers": headers}
        if http_transport is not None:
            kwargs["transport"] = httpx.MockTransport(http_transport)
        self._client = httpx.AsyncClient(**kwargs)

    async def request(
        self,
        method: str,
        path: str,
        *,
        json: Any = None,
        params: dict[str, str] | None = None,
        content: bytes | Any | None = None,
        headers: dict[str, str] | None = None,
    ) -> httpx.Response:
        resp = await self._client.request(
            method, path, json=json, params=params, content=content, headers=headers or {},
        )
        _raise_for_status(resp)
        return resp

    async def aclose(self) -> None:
        await self._client.aclose()
```

**Step 4: Run tests to verify they pass**

```bash
cd clients/python && python -m pytest tests/test_transport.py -v
```

Expected: All PASS.

**Step 5: Commit**

```bash
git add clients/python/
git commit -m "feat(python): add transport layer with sync/async support and error handling"
```

---

## Task 2: Pydantic Models

**Files:**
- Create: `clients/python/src/carbonfiles/models/__init__.py`
- Create: `clients/python/src/carbonfiles/models/common.py`
- Create: `clients/python/src/carbonfiles/models/buckets.py`
- Create: `clients/python/src/carbonfiles/models/files.py`
- Create: `clients/python/src/carbonfiles/models/keys.py`
- Create: `clients/python/src/carbonfiles/models/tokens.py`
- Create: `clients/python/src/carbonfiles/models/stats.py`
- Create: `clients/python/tests/test_models.py`

**Step 1: Write model tests**

Create `clients/python/tests/test_models.py`:

```python
"""Tests for Pydantic models — serialization and validation."""

from datetime import datetime, timezone

from carbonfiles.models import (
    ApiKey,
    ApiKeyListItem,
    ApiKeyUsage,
    Bucket,
    BucketChanges,
    BucketDetail,
    BucketFile,
    DashboardToken,
    DashboardTokenInfo,
    DirectoryListing,
    HealthResponse,
    OwnerStats,
    PaginatedResponse,
    Stats,
    UploadResponse,
    UploadToken,
)


class TestBucketModels:
    def test_bucket_from_json(self):
        data = {
            "id": "abc123",
            "name": "test",
            "owner": "cf4_owner",
            "description": "desc",
            "created_at": "2026-01-01T00:00:00Z",
            "expires_at": "2026-02-01T00:00:00Z",
            "last_used_at": None,
            "file_count": 5,
            "total_size": 1024,
        }
        bucket = Bucket.model_validate(data)
        assert bucket.id == "abc123"
        assert bucket.name == "test"
        assert bucket.description == "desc"
        assert bucket.file_count == 5
        assert bucket.total_size == 1024
        assert bucket.expires_at is not None
        assert bucket.last_used_at is None

    def test_bucket_optional_fields(self):
        data = {
            "id": "abc",
            "name": "test",
            "owner": "o",
            "created_at": "2026-01-01T00:00:00Z",
            "file_count": 0,
            "total_size": 0,
        }
        bucket = Bucket.model_validate(data)
        assert bucket.description is None
        assert bucket.expires_at is None

    def test_bucket_detail(self):
        data = {
            "id": "abc",
            "name": "test",
            "owner": "o",
            "created_at": "2026-01-01T00:00:00Z",
            "file_count": 1,
            "total_size": 100,
            "files": [
                {
                    "path": "file.txt",
                    "name": "file.txt",
                    "size": 100,
                    "mime_type": "text/plain",
                    "created_at": "2026-01-01T00:00:00Z",
                    "updated_at": "2026-01-01T00:00:00Z",
                }
            ],
            "has_more_files": False,
        }
        detail = BucketDetail.model_validate(data)
        assert len(detail.files) == 1
        assert detail.has_more_files is False

    def test_bucket_changes(self):
        data = {"name": "new-name", "description": None, "expires_at": None}
        changes = BucketChanges.model_validate(data)
        assert changes.name == "new-name"


class TestFileModels:
    def test_bucket_file(self):
        data = {
            "path": "docs/readme.md",
            "name": "readme.md",
            "size": 256,
            "mime_type": "text/markdown",
            "short_code": "abc123",
            "short_url": "https://example.com/s/abc123",
            "created_at": "2026-01-01T00:00:00Z",
            "updated_at": "2026-01-02T00:00:00Z",
        }
        f = BucketFile.model_validate(data)
        assert f.path == "docs/readme.md"
        assert f.name == "readme.md"
        assert f.short_code == "abc123"

    def test_bucket_file_optional_fields(self):
        data = {
            "path": "file.txt",
            "name": "file.txt",
            "size": 0,
            "mime_type": "text/plain",
            "created_at": "2026-01-01T00:00:00Z",
            "updated_at": "2026-01-01T00:00:00Z",
        }
        f = BucketFile.model_validate(data)
        assert f.short_code is None
        assert f.short_url is None

    def test_upload_response(self):
        data = {
            "uploaded": [
                {
                    "path": "file.txt",
                    "name": "file.txt",
                    "size": 5,
                    "mime_type": "text/plain",
                    "created_at": "2026-01-01T00:00:00Z",
                    "updated_at": "2026-01-01T00:00:00Z",
                }
            ]
        }
        resp = UploadResponse.model_validate(data)
        assert len(resp.uploaded) == 1
        assert resp.uploaded[0].name == "file.txt"

    def test_directory_listing(self):
        data = {
            "files": [],
            "folders": ["src", "docs"],
            "total_files": 0,
            "total_folders": 2,
            "limit": 200,
            "offset": 0,
        }
        dl = DirectoryListing.model_validate(data)
        assert dl.folders == ["src", "docs"]
        assert dl.total_folders == 2


class TestPaginatedResponse:
    def test_paginated_buckets(self):
        data = {
            "items": [
                {
                    "id": "a",
                    "name": "b",
                    "owner": "o",
                    "created_at": "2026-01-01T00:00:00Z",
                    "file_count": 0,
                    "total_size": 0,
                }
            ],
            "total": 1,
            "limit": 50,
            "offset": 0,
        }
        page = PaginatedResponse[Bucket].model_validate(data)
        assert len(page.items) == 1
        assert page.total == 1
        assert isinstance(page.items[0], Bucket)

    def test_empty_page(self):
        data = {"items": [], "total": 0, "limit": 50, "offset": 0}
        page = PaginatedResponse[Bucket].model_validate(data)
        assert len(page.items) == 0


class TestKeyModels:
    def test_api_key_response(self):
        data = {
            "key": "cf4_full_secret_key",
            "prefix": "cf4_full",
            "name": "ci-agent",
            "created_at": "2026-01-01T00:00:00Z",
        }
        key = ApiKey.model_validate(data)
        assert key.key == "cf4_full_secret_key"
        assert key.prefix == "cf4_full"

    def test_api_key_list_item(self):
        data = {
            "prefix": "cf4_abc",
            "name": "test",
            "created_at": "2026-01-01T00:00:00Z",
            "last_used_at": None,
            "bucket_count": 3,
            "file_count": 10,
            "total_size": 2048,
        }
        item = ApiKeyListItem.model_validate(data)
        assert item.bucket_count == 3

    def test_api_key_usage(self):
        data = {
            "prefix": "cf4_abc",
            "name": "test",
            "created_at": "2026-01-01T00:00:00Z",
            "last_used_at": None,
            "bucket_count": 1,
            "file_count": 2,
            "total_size": 512,
            "total_downloads": 100,
            "buckets": [
                {
                    "id": "b1",
                    "name": "bucket",
                    "owner": "cf4_abc",
                    "created_at": "2026-01-01T00:00:00Z",
                    "file_count": 2,
                    "total_size": 512,
                }
            ],
        }
        usage = ApiKeyUsage.model_validate(data)
        assert usage.total_downloads == 100
        assert len(usage.buckets) == 1


class TestTokenModels:
    def test_upload_token(self):
        data = {
            "token": "cfu_abc123",
            "bucket_id": "b1",
            "expires_at": "2026-02-01T00:00:00Z",
            "max_uploads": 10,
            "uploads_used": 3,
        }
        t = UploadToken.model_validate(data)
        assert t.token == "cfu_abc123"
        assert t.max_uploads == 10

    def test_upload_token_unlimited(self):
        data = {
            "token": "cfu_abc",
            "bucket_id": "b1",
            "expires_at": "2026-02-01T00:00:00Z",
            "max_uploads": None,
            "uploads_used": 0,
        }
        t = UploadToken.model_validate(data)
        assert t.max_uploads is None

    def test_dashboard_token(self):
        data = {"token": "jwt_token", "expires_at": "2026-01-02T00:00:00Z"}
        t = DashboardToken.model_validate(data)
        assert t.token == "jwt_token"

    def test_dashboard_token_info(self):
        data = {"scope": "admin", "expires_at": "2026-01-02T00:00:00Z"}
        info = DashboardTokenInfo.model_validate(data)
        assert info.scope == "admin"


class TestStatsModels:
    def test_stats(self):
        data = {
            "total_buckets": 10,
            "total_files": 50,
            "total_size": 1048576,
            "total_keys": 3,
            "total_downloads": 200,
            "storage_by_owner": [
                {"owner": "cf4_abc", "bucket_count": 5, "file_count": 25, "total_size": 524288}
            ],
        }
        stats = Stats.model_validate(data)
        assert stats.total_buckets == 10
        assert len(stats.storage_by_owner) == 1
        assert stats.storage_by_owner[0].owner == "cf4_abc"


class TestHealthResponse:
    def test_health(self):
        data = {"status": "healthy", "uptime_seconds": 3600, "db": "ok"}
        h = HealthResponse.model_validate(data)
        assert h.status == "healthy"
        assert h.uptime_seconds == 3600
```

**Step 2: Run tests to verify they fail**

```bash
cd clients/python && python -m pytest tests/test_models.py -v
```

Expected: FAIL — models don't exist yet.

**Step 3: Implement all models**

Create `clients/python/src/carbonfiles/models/common.py`:

```python
"""Common models shared across resources."""

from __future__ import annotations

from typing import Generic, TypeVar

from pydantic import BaseModel

T = TypeVar("T")


class PaginatedResponse(BaseModel, Generic[T]):
    """Paginated API response."""

    items: list[T]
    total: int
    limit: int
    offset: int


class HealthResponse(BaseModel):
    """Health check response."""

    status: str
    uptime_seconds: int
    db: str


class ErrorResponse(BaseModel):
    """API error response."""

    error: str
    hint: str | None = None
```

Create `clients/python/src/carbonfiles/models/buckets.py`:

```python
"""Bucket models."""

from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel


class Bucket(BaseModel):
    """A storage bucket."""

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
    """Bucket with file listing."""

    files: list[BucketFile]
    has_more_files: bool


class BucketChanges(BaseModel):
    """Partial bucket update (used in events)."""

    name: str | None = None
    description: str | None = None
    expires_at: datetime | None = None


# Avoid circular import — BucketFile is defined in files.py
from carbonfiles.models.files import BucketFile  # noqa: E402

BucketDetail.model_rebuild()
```

Create `clients/python/src/carbonfiles/models/files.py`:

```python
"""File models."""

from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel


class BucketFile(BaseModel):
    """A file within a bucket."""

    path: str
    name: str
    size: int
    mime_type: str
    short_code: str | None = None
    short_url: str | None = None
    created_at: datetime
    updated_at: datetime


class UploadResponse(BaseModel):
    """Response from file upload."""

    uploaded: list[BucketFile]


class DirectoryListing(BaseModel):
    """Hierarchical directory listing."""

    files: list[BucketFile]
    folders: list[str]
    total_files: int
    total_folders: int
    limit: int
    offset: int
```

Create `clients/python/src/carbonfiles/models/keys.py`:

```python
"""API key models."""

from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel

from carbonfiles.models.buckets import Bucket


class ApiKey(BaseModel):
    """API key creation response (contains the full key, shown once)."""

    key: str
    prefix: str
    name: str
    created_at: datetime


class ApiKeyListItem(BaseModel):
    """API key summary in list responses."""

    prefix: str
    name: str
    created_at: datetime
    last_used_at: datetime | None = None
    bucket_count: int
    file_count: int
    total_size: int


class ApiKeyUsage(ApiKeyListItem):
    """API key usage details."""

    total_downloads: int
    buckets: list[Bucket]
```

Create `clients/python/src/carbonfiles/models/tokens.py`:

```python
"""Token models (upload tokens and dashboard tokens)."""

from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel


class UploadToken(BaseModel):
    """Upload token for scoped file uploads."""

    token: str
    bucket_id: str
    expires_at: datetime
    max_uploads: int | None = None
    uploads_used: int


class DashboardToken(BaseModel):
    """Dashboard JWT token."""

    token: str
    expires_at: datetime


class DashboardTokenInfo(BaseModel):
    """Dashboard token validation info."""

    scope: str
    expires_at: datetime
```

Create `clients/python/src/carbonfiles/models/stats.py`:

```python
"""System statistics models."""

from __future__ import annotations

from pydantic import BaseModel


class OwnerStats(BaseModel):
    """Per-owner storage statistics."""

    owner: str
    bucket_count: int
    file_count: int
    total_size: int


class Stats(BaseModel):
    """System-wide statistics."""

    total_buckets: int
    total_files: int
    total_size: int
    total_keys: int
    total_downloads: int
    storage_by_owner: list[OwnerStats]
```

Create `clients/python/src/carbonfiles/models/__init__.py`:

```python
"""CarbonFiles models."""

from carbonfiles.models.buckets import Bucket, BucketChanges, BucketDetail
from carbonfiles.models.common import ErrorResponse, HealthResponse, PaginatedResponse
from carbonfiles.models.files import BucketFile, DirectoryListing, UploadResponse
from carbonfiles.models.keys import ApiKey, ApiKeyListItem, ApiKeyUsage
from carbonfiles.models.stats import OwnerStats, Stats
from carbonfiles.models.tokens import DashboardToken, DashboardTokenInfo, UploadToken

__all__ = [
    "ApiKey",
    "ApiKeyListItem",
    "ApiKeyUsage",
    "Bucket",
    "BucketChanges",
    "BucketDetail",
    "BucketFile",
    "DashboardToken",
    "DashboardTokenInfo",
    "DirectoryListing",
    "ErrorResponse",
    "HealthResponse",
    "OwnerStats",
    "PaginatedResponse",
    "Stats",
    "UploadResponse",
    "UploadToken",
]
```

**Step 4: Run tests to verify they pass**

```bash
cd clients/python && python -m pytest tests/test_models.py -v
```

Expected: All PASS.

**Step 5: Commit**

```bash
git add clients/python/src/carbonfiles/models/
git add clients/python/tests/test_models.py
git commit -m "feat(python): add pydantic models for all API types"
```

**Note on circular import:** `BucketDetail` references `BucketFile`. The approach above uses a deferred import + `model_rebuild()`. If this causes issues, an alternative is to define `BucketFile` before `BucketDetail` in a single file, or use `TYPE_CHECKING` with string annotations. Fix whichever approach works cleanly.

---

## Task 3: Types Module

**Files:**
- Create: `clients/python/src/carbonfiles/_types.py`

**Step 1: Create types module**

```python
"""Type aliases for the CarbonFiles SDK."""

from __future__ import annotations

from typing import Callable

# Progress callback: (bytes_sent, total_bytes_or_none, percentage_or_none)
ProgressCallback = Callable[[int, int | None, float | None], None]
```

**Step 2: Commit**

```bash
git add clients/python/src/carbonfiles/_types.py
git commit -m "feat(python): add type aliases"
```

---

## Task 4: Bucket Resources

**Files:**
- Create: `clients/python/src/carbonfiles/resources/__init__.py`
- Create: `clients/python/src/carbonfiles/resources/buckets.py`
- Create: `clients/python/tests/test_buckets.py`

**Step 1: Write bucket tests**

Create `clients/python/tests/test_buckets.py`:

```python
"""Tests for bucket resources."""

import json

import httpx
import pytest

from carbonfiles._transport import CarbonFilesError, SyncTransport, AsyncTransport
from carbonfiles.models import Bucket, BucketDetail, PaginatedResponse
from carbonfiles.resources.buckets import (
    AsyncBucketResource,
    AsyncBucketsResource,
    BucketResource,
    BucketsResource,
)


def _handler(responses: dict[str, tuple[int, dict | str]]):
    """Create a mock handler keyed by (method, path)."""
    def handler(request: httpx.Request) -> httpx.Response:
        key = f"{request.method} {request.url.raw_path.decode()}"
        # Try without query string first
        path_only = key.split("?")[0]
        status, body = responses.get(key) or responses.get(path_only) or responses[key]
        if isinstance(body, dict):
            return httpx.Response(status, json=body)
        return httpx.Response(status, text=body)
    return handler


SAMPLE_BUCKET = {
    "id": "abc123",
    "name": "test",
    "owner": "cf4_key",
    "created_at": "2026-01-01T00:00:00Z",
    "file_count": 0,
    "total_size": 0,
}


class TestBucketsResource:
    def test_create(self):
        def handler(request: httpx.Request) -> httpx.Response:
            body = json.loads(request.content)
            assert body["name"] == "my-bucket"
            assert body["description"] == "desc"
            return httpx.Response(201, json=SAMPLE_BUCKET)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = BucketsResource(transport)
        bucket = resource.create("my-bucket", description="desc")
        assert isinstance(bucket, Bucket)
        assert bucket.id == "abc123"

    def test_create_minimal(self):
        def handler(request: httpx.Request) -> httpx.Response:
            body = json.loads(request.content)
            assert "description" not in body
            assert "expires_in" not in body
            return httpx.Response(201, json=SAMPLE_BUCKET)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = BucketsResource(transport)
        resource.create("my-bucket")

    def test_list(self):
        page_data = {"items": [SAMPLE_BUCKET], "total": 1, "limit": 50, "offset": 0}

        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json=page_data)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = BucketsResource(transport)
        page = resource.list()
        assert page.total == 1
        assert isinstance(page.items[0], Bucket)

    def test_list_with_pagination(self):
        page_data = {"items": [], "total": 0, "limit": 20, "offset": 10}

        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.params["limit"] == "20"
            assert request.url.params["offset"] == "10"
            assert request.url.params["sort"] == "name"
            assert request.url.params["order"] == "asc"
            return httpx.Response(200, json=page_data)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = BucketsResource(transport)
        resource.list(limit=20, offset=10, sort="name", order="asc")

    def test_list_with_include_expired(self):
        page_data = {"items": [], "total": 0, "limit": 50, "offset": 0}

        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.params["include_expired"] == "true"
            return httpx.Response(200, json=page_data)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = BucketsResource(transport)
        resource.list(include_expired=True)

    def test_getitem_returns_bucket_resource(self):
        transport = SyncTransport("https://example.com", api_key="k",
                                   http_transport=lambda r: httpx.Response(200))
        resource = BucketsResource(transport)
        bucket = resource["abc123"]
        assert isinstance(bucket, BucketResource)

    def test_list_all_pagination(self):
        """list_all() should auto-paginate through all pages."""
        call_count = 0

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal call_count
            offset = int(request.url.params.get("offset", "0"))
            if offset == 0:
                call_count += 1
                return httpx.Response(200, json={
                    "items": [SAMPLE_BUCKET], "total": 3, "limit": 2, "offset": 0
                })
            else:
                call_count += 1
                return httpx.Response(200, json={
                    "items": [SAMPLE_BUCKET], "total": 3, "limit": 2, "offset": 2
                })

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = BucketsResource(transport)
        pages = list(resource.list_all(limit=2))
        assert len(pages) == 2
        assert call_count == 2


class TestBucketResource:
    def test_get(self):
        detail = {
            **SAMPLE_BUCKET,
            "files": [],
            "has_more_files": False,
        }

        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.raw_path == b"/api/buckets/abc123"
            return httpx.Response(200, json=detail)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = BucketResource(transport, "abc123")
        result = resource.get()
        assert isinstance(result, BucketDetail)

    def test_update(self):
        def handler(request: httpx.Request) -> httpx.Response:
            body = json.loads(request.content)
            assert body["name"] == "renamed"
            assert request.method == "PATCH"
            return httpx.Response(200, json=SAMPLE_BUCKET)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = BucketResource(transport, "abc123")
        result = resource.update(name="renamed")
        assert isinstance(result, Bucket)

    def test_delete(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.method == "DELETE"
            return httpx.Response(204)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = BucketResource(transport, "abc123")
        resource.delete()  # Should not raise

    def test_summary(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, text="Bucket: test\nFiles: 5")

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = BucketResource(transport, "abc123")
        text = resource.summary()
        assert "Bucket: test" in text

    def test_download_zip(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, content=b"PK\x03\x04fake-zip")

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = BucketResource(transport, "abc123")
        data = resource.download_zip()
        assert data.startswith(b"PK")

    def test_url_escapes_bucket_id(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert b"a%2Fb" in request.url.raw_path
            return httpx.Response(200, json={**SAMPLE_BUCKET, "files": [], "has_more_files": False})

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = BucketResource(transport, "a/b")
        resource.get()
```

**Step 2: Run tests to verify they fail**

```bash
cd clients/python && python -m pytest tests/test_buckets.py -v
```

**Step 3: Implement bucket resources**

Create `clients/python/src/carbonfiles/resources/__init__.py` (empty).

Create `clients/python/src/carbonfiles/resources/buckets.py`:

```python
"""Bucket resources."""

from __future__ import annotations

from typing import TYPE_CHECKING, Generator
from urllib.parse import quote

from carbonfiles.models import Bucket, BucketDetail, PaginatedResponse

if TYPE_CHECKING:
    from carbonfiles._transport import AsyncTransport, SyncTransport
    from carbonfiles.resources.files import AsyncFilesResource, FilesResource
    from carbonfiles.resources.tokens import AsyncUploadTokensResource, UploadTokensResource


def _pagination_params(
    limit: int | None = None,
    offset: int | None = None,
    sort: str | None = None,
    order: str | None = None,
) -> dict[str, str]:
    params: dict[str, str] = {}
    if limit is not None:
        params["limit"] = str(limit)
    if offset is not None:
        params["offset"] = str(offset)
    if sort is not None:
        params["sort"] = sort
    if order is not None:
        params["order"] = order
    return params


class BucketsResource:
    """Collection-level bucket operations."""

    def __init__(self, transport: SyncTransport) -> None:
        self._transport = transport

    def create(
        self,
        name: str,
        *,
        description: str | None = None,
        expires_in: str | None = None,
    ) -> Bucket:
        body: dict = {"name": name}
        if description is not None:
            body["description"] = description
        if expires_in is not None:
            body["expires_in"] = expires_in
        resp = self._transport.request("POST", "/api/buckets", json=body)
        return Bucket.model_validate(resp.json())

    def list(
        self,
        *,
        limit: int | None = None,
        offset: int | None = None,
        sort: str | None = None,
        order: str | None = None,
        include_expired: bool | None = None,
    ) -> PaginatedResponse[Bucket]:
        params = _pagination_params(limit, offset, sort, order)
        if include_expired is not None:
            params["include_expired"] = str(include_expired).lower()
        resp = self._transport.request("GET", "/api/buckets", params=params)
        return PaginatedResponse[Bucket].model_validate(resp.json())

    def list_all(
        self,
        *,
        limit: int = 50,
        sort: str | None = None,
        order: str | None = None,
    ) -> Generator[PaginatedResponse[Bucket], None, None]:
        offset = 0
        while True:
            page = self.list(limit=limit, offset=offset, sort=sort, order=order)
            yield page
            if offset + limit >= page.total:
                break
            offset += limit

    def __getitem__(self, bucket_id: str) -> BucketResource:
        return BucketResource(self._transport, bucket_id)


class BucketResource:
    """Single-bucket operations."""

    def __init__(self, transport: SyncTransport, bucket_id: str) -> None:
        self._transport = transport
        self._id = bucket_id
        self._path = f"/api/buckets/{quote(bucket_id, safe='')}"

    @property
    def files(self) -> FilesResource:
        from carbonfiles.resources.files import FilesResource
        return FilesResource(self._transport, self._id)

    @property
    def tokens(self) -> UploadTokensResource:
        from carbonfiles.resources.tokens import UploadTokensResource
        return UploadTokensResource(self._transport, self._id)

    def get(self) -> BucketDetail:
        resp = self._transport.request("GET", self._path)
        return BucketDetail.model_validate(resp.json())

    def update(
        self,
        *,
        name: str | None = None,
        description: str | None = None,
        expires_in: str | None = None,
    ) -> Bucket:
        body: dict = {}
        if name is not None:
            body["name"] = name
        if description is not None:
            body["description"] = description
        if expires_in is not None:
            body["expires_in"] = expires_in
        resp = self._transport.request("PATCH", self._path, json=body)
        return Bucket.model_validate(resp.json())

    def delete(self) -> None:
        self._transport.request("DELETE", self._path)

    def summary(self) -> str:
        resp = self._transport.request("GET", f"{self._path}/summary")
        return resp.text

    def download_zip(self) -> bytes:
        resp = self._transport.request("GET", f"{self._path}/zip")
        return resp.content


class AsyncBucketsResource:
    """Async collection-level bucket operations."""

    def __init__(self, transport: AsyncTransport) -> None:
        self._transport = transport

    async def create(self, name: str, *, description: str | None = None, expires_in: str | None = None) -> Bucket:
        body: dict = {"name": name}
        if description is not None:
            body["description"] = description
        if expires_in is not None:
            body["expires_in"] = expires_in
        resp = await self._transport.request("POST", "/api/buckets", json=body)
        return Bucket.model_validate(resp.json())

    async def list(self, *, limit: int | None = None, offset: int | None = None,
                   sort: str | None = None, order: str | None = None,
                   include_expired: bool | None = None) -> PaginatedResponse[Bucket]:
        params = _pagination_params(limit, offset, sort, order)
        if include_expired is not None:
            params["include_expired"] = str(include_expired).lower()
        resp = await self._transport.request("GET", "/api/buckets", params=params)
        return PaginatedResponse[Bucket].model_validate(resp.json())

    async def list_all(self, *, limit: int = 50, sort: str | None = None, order: str | None = None):
        offset = 0
        while True:
            page = await self.list(limit=limit, offset=offset, sort=sort, order=order)
            yield page
            if offset + limit >= page.total:
                break
            offset += limit

    def __getitem__(self, bucket_id: str) -> AsyncBucketResource:
        return AsyncBucketResource(self._transport, bucket_id)


class AsyncBucketResource:
    """Async single-bucket operations."""

    def __init__(self, transport: AsyncTransport, bucket_id: str) -> None:
        self._transport = transport
        self._id = bucket_id
        self._path = f"/api/buckets/{quote(bucket_id, safe='')}"

    @property
    def files(self) -> AsyncFilesResource:
        from carbonfiles.resources.files import AsyncFilesResource
        return AsyncFilesResource(self._transport, self._id)

    @property
    def tokens(self) -> AsyncUploadTokensResource:
        from carbonfiles.resources.tokens import AsyncUploadTokensResource
        return AsyncUploadTokensResource(self._transport, self._id)

    async def get(self) -> BucketDetail:
        resp = await self._transport.request("GET", self._path)
        return BucketDetail.model_validate(resp.json())

    async def update(self, *, name: str | None = None, description: str | None = None,
                     expires_in: str | None = None) -> Bucket:
        body: dict = {}
        if name is not None:
            body["name"] = name
        if description is not None:
            body["description"] = description
        if expires_in is not None:
            body["expires_in"] = expires_in
        resp = await self._transport.request("PATCH", self._path, json=body)
        return Bucket.model_validate(resp.json())

    async def delete(self) -> None:
        await self._transport.request("DELETE", self._path)

    async def summary(self) -> str:
        resp = await self._transport.request("GET", f"{self._path}/summary")
        return resp.text

    async def download_zip(self) -> bytes:
        resp = await self._transport.request("GET", f"{self._path}/zip")
        return resp.content
```

**Step 4: Run tests**

```bash
cd clients/python && python -m pytest tests/test_buckets.py -v
```

Expected: All PASS.

**Step 5: Commit**

```bash
git add clients/python/src/carbonfiles/resources/ clients/python/tests/test_buckets.py
git commit -m "feat(python): add bucket resources (sync + async)"
```

---

## Task 5: File Resources

**Files:**
- Create: `clients/python/src/carbonfiles/resources/files.py`
- Create: `clients/python/tests/test_files.py`
- Create: `clients/python/tests/test_uploads.py`

**Step 1: Write file operation tests**

Create `clients/python/tests/test_files.py`:

```python
"""Tests for file resources (list, metadata, download, delete, patch, append)."""

import json

import httpx
import pytest

from carbonfiles._transport import SyncTransport
from carbonfiles.models import BucketFile, DirectoryListing, PaginatedResponse
from carbonfiles.resources.files import FileResource, FilesResource

SAMPLE_FILE = {
    "path": "docs/readme.md",
    "name": "readme.md",
    "size": 256,
    "mime_type": "text/markdown",
    "created_at": "2026-01-01T00:00:00Z",
    "updated_at": "2026-01-01T00:00:00Z",
}


class TestFilesResource:
    def test_list(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert b"/api/buckets/b1/files" in request.url.raw_path
            return httpx.Response(200, json={
                "items": [SAMPLE_FILE], "total": 1, "limit": 50, "offset": 0
            })

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FilesResource(transport, "b1")
        page = resource.list()
        assert len(page.items) == 1
        assert isinstance(page.items[0], BucketFile)

    def test_list_with_pagination(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.params["limit"] == "10"
            assert request.url.params["sort"] == "size"
            return httpx.Response(200, json={"items": [], "total": 0, "limit": 10, "offset": 0})

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FilesResource(transport, "b1")
        resource.list(limit=10, sort="size")

    def test_list_directory(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.params["path"] == "src/"
            return httpx.Response(200, json={
                "files": [SAMPLE_FILE],
                "folders": ["lib"],
                "total_files": 1,
                "total_folders": 1,
                "limit": 200,
                "offset": 0,
            })

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FilesResource(transport, "b1")
        listing = resource.list_directory("src/")
        assert isinstance(listing, DirectoryListing)
        assert listing.folders == ["lib"]

    def test_getitem_returns_file_resource(self):
        transport = SyncTransport("https://example.com", api_key="k",
                                   http_transport=lambda r: httpx.Response(200))
        resource = FilesResource(transport, "b1")
        f = resource["readme.md"]
        assert isinstance(f, FileResource)


class TestFileResource:
    def test_metadata(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.method == "GET"
            return httpx.Response(200, json=SAMPLE_FILE)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FileResource(transport, "b1", "docs/readme.md")
        meta = resource.metadata()
        assert isinstance(meta, BucketFile)
        assert meta.name == "readme.md"

    def test_download(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert b"/content" in request.url.raw_path
            return httpx.Response(200, content=b"file content here")

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FileResource(transport, "b1", "readme.md")
        data = resource.download()
        assert data == b"file content here"

    def test_download_to(self, tmp_path):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, content=b"saved content")

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FileResource(transport, "b1", "readme.md")
        dest = tmp_path / "readme.md"
        resource.download_to(str(dest))
        assert dest.read_bytes() == b"saved content"

    def test_delete(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.method == "DELETE"
            return httpx.Response(204)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FileResource(transport, "b1", "readme.md")
        resource.delete()

    def test_append(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.headers.get("x-append") == "true"
            assert request.method == "PATCH"
            return httpx.Response(200, json=SAMPLE_FILE)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FileResource(transport, "b1", "log.txt")
        result = resource.append(b"new line\n")
        assert isinstance(result, BucketFile)

    def test_patch(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.headers["content-range"] == "bytes 0-99/1000"
            assert request.method == "PATCH"
            return httpx.Response(200, json=SAMPLE_FILE)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FileResource(transport, "b1", "data.bin")
        result = resource.patch(b"x" * 100, range_start=0, range_end=99, total_size=1000)
        assert isinstance(result, BucketFile)

    def test_url_escapes_file_path(self):
        def handler(request: httpx.Request) -> httpx.Response:
            # "path/to file.txt" should be escaped
            assert b"path%2Fto%20file.txt" in request.url.raw_path
            return httpx.Response(200, json=SAMPLE_FILE)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FileResource(transport, "b1", "path/to file.txt")
        resource.metadata()
```

Create `clients/python/tests/test_uploads.py`:

```python
"""Tests for file upload functionality."""

import io
import json
from pathlib import Path

import httpx
import pytest

from carbonfiles._transport import SyncTransport
from carbonfiles.models import UploadResponse
from carbonfiles.resources.files import FilesResource

SAMPLE_UPLOAD_RESPONSE = {
    "uploaded": [
        {
            "path": "photo.jpg",
            "name": "photo.jpg",
            "size": 1024,
            "mime_type": "image/jpeg",
            "created_at": "2026-01-01T00:00:00Z",
            "updated_at": "2026-01-01T00:00:00Z",
        }
    ]
}


class TestUpload:
    def test_upload_from_path(self, tmp_path):
        test_file = tmp_path / "photo.jpg"
        test_file.write_bytes(b"fake-jpeg-data")

        def handler(request: httpx.Request) -> httpx.Response:
            assert request.method == "PUT"
            assert request.url.params["filename"] == "photo.jpg"
            return httpx.Response(201, json=SAMPLE_UPLOAD_RESPONSE)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FilesResource(transport, "b1")
        result = resource.upload(str(test_file))
        assert isinstance(result, UploadResponse)
        assert len(result.uploaded) == 1

    def test_upload_from_path_object(self, tmp_path):
        test_file = tmp_path / "photo.jpg"
        test_file.write_bytes(b"fake-jpeg-data")

        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.params["filename"] == "photo.jpg"
            return httpx.Response(201, json=SAMPLE_UPLOAD_RESPONSE)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FilesResource(transport, "b1")
        result = resource.upload(test_file)
        assert isinstance(result, UploadResponse)

    def test_upload_from_bytes(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.params["filename"] == "hello.txt"
            return httpx.Response(201, json=SAMPLE_UPLOAD_RESPONSE)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FilesResource(transport, "b1")
        result = resource.upload(b"hello world", filename="hello.txt")
        assert isinstance(result, UploadResponse)

    def test_upload_from_bytes_requires_filename(self):
        transport = SyncTransport("https://example.com", api_key="k",
                                   http_transport=lambda r: httpx.Response(201))
        resource = FilesResource(transport, "b1")
        with pytest.raises(ValueError, match="filename"):
            resource.upload(b"data")

    def test_upload_from_file_object(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.params["filename"] == "data.bin"
            return httpx.Response(201, json=SAMPLE_UPLOAD_RESPONSE)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FilesResource(transport, "b1")
        result = resource.upload(io.BytesIO(b"binary-data"), filename="data.bin")
        assert isinstance(result, UploadResponse)

    def test_upload_from_file_object_requires_filename(self):
        transport = SyncTransport("https://example.com", api_key="k",
                                   http_transport=lambda r: httpx.Response(201))
        resource = FilesResource(transport, "b1")
        with pytest.raises(ValueError, match="filename"):
            resource.upload(io.BytesIO(b"data"))

    def test_upload_with_token(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.params["token"] == "cfu_abc"
            return httpx.Response(201, json=SAMPLE_UPLOAD_RESPONSE)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FilesResource(transport, "b1")
        resource.upload(b"data", filename="f.txt", upload_token="cfu_abc")

    def test_upload_with_custom_filename(self, tmp_path):
        test_file = tmp_path / "original.jpg"
        test_file.write_bytes(b"data")

        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.params["filename"] == "renamed.jpg"
            return httpx.Response(201, json=SAMPLE_UPLOAD_RESPONSE)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FilesResource(transport, "b1")
        resource.upload(str(test_file), filename="renamed.jpg")

    def test_upload_with_progress(self, tmp_path):
        test_file = tmp_path / "big.bin"
        test_file.write_bytes(b"x" * 1000)

        progress_calls = []

        def on_progress(bytes_sent, total, percentage):
            progress_calls.append((bytes_sent, total, percentage))

        def handler(request: httpx.Request) -> httpx.Response:
            # Read the content to trigger progress
            _ = request.content
            return httpx.Response(201, json=SAMPLE_UPLOAD_RESPONSE)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = FilesResource(transport, "b1")
        resource.upload(str(test_file), progress=on_progress)
        # Progress should have been called at least once
        assert len(progress_calls) > 0
        # Last call should report all bytes sent
        assert progress_calls[-1][0] == 1000
```

**Step 2: Run tests to verify they fail**

```bash
cd clients/python && python -m pytest tests/test_files.py tests/test_uploads.py -v
```

**Step 3: Implement file resources**

Create `clients/python/src/carbonfiles/resources/files.py`:

```python
"""File resources."""

from __future__ import annotations

import io
from pathlib import Path
from typing import TYPE_CHECKING, BinaryIO, Generator
from urllib.parse import quote

from carbonfiles._types import ProgressCallback
from carbonfiles.models import BucketFile, DirectoryListing, PaginatedResponse, UploadResponse
from carbonfiles.resources.buckets import _pagination_params

if TYPE_CHECKING:
    from carbonfiles._transport import AsyncTransport, SyncTransport


class _ProgressReader:
    """Wraps a file-like object to report read progress."""

    def __init__(self, stream: BinaryIO, total: int | None, callback: ProgressCallback) -> None:
        self._stream = stream
        self._total = total
        self._callback = callback
        self._bytes_sent = 0

    def read(self, size: int = -1) -> bytes:
        data = self._stream.read(size)
        if data:
            self._bytes_sent += len(data)
            percentage = (self._bytes_sent / self._total * 100) if self._total else None
            self._callback(self._bytes_sent, self._total, percentage)
        return data


class FilesResource:
    """File operations within a bucket."""

    def __init__(self, transport: SyncTransport, bucket_id: str) -> None:
        self._transport = transport
        self._bucket_id = bucket_id
        self._path = f"/api/buckets/{quote(bucket_id, safe='')}"

    def list(
        self,
        *,
        limit: int | None = None,
        offset: int | None = None,
        sort: str | None = None,
        order: str | None = None,
    ) -> PaginatedResponse[BucketFile]:
        params = _pagination_params(limit, offset, sort, order)
        resp = self._transport.request("GET", f"{self._path}/files", params=params)
        return PaginatedResponse[BucketFile].model_validate(resp.json())

    def list_all(
        self, *, limit: int = 50, sort: str | None = None, order: str | None = None,
    ) -> Generator[PaginatedResponse[BucketFile], None, None]:
        offset = 0
        while True:
            page = self.list(limit=limit, offset=offset, sort=sort, order=order)
            yield page
            if offset + limit >= page.total:
                break
            offset += limit

    def list_directory(
        self,
        path: str | None = None,
        *,
        limit: int | None = None,
        offset: int | None = None,
        sort: str | None = None,
        order: str | None = None,
    ) -> DirectoryListing:
        params = _pagination_params(limit, offset, sort, order)
        if path is not None:
            params["path"] = path
        resp = self._transport.request("GET", f"{self._path}/ls", params=params)
        return DirectoryListing.model_validate(resp.json())

    def upload(
        self,
        source: str | bytes | BinaryIO | Path,
        *,
        filename: str | None = None,
        progress: ProgressCallback | None = None,
        upload_token: str | None = None,
    ) -> UploadResponse:
        if isinstance(source, (str, Path)):
            path = Path(source)
            filename = filename or path.name
            total = path.stat().st_size
            with open(path, "rb") as f:
                return self._upload_stream(f, filename, total, progress, upload_token)
        elif isinstance(source, bytes):
            if not filename:
                raise ValueError("filename is required when uploading bytes")
            stream = io.BytesIO(source)
            return self._upload_stream(stream, filename, len(source), progress, upload_token)
        else:
            if not filename:
                raise ValueError("filename is required when uploading a file object")
            total = None
            if hasattr(source, "seek") and hasattr(source, "tell"):
                pos = source.tell()
                source.seek(0, 2)
                total = source.tell()
                source.seek(pos)
            return self._upload_stream(source, filename, total, progress, upload_token)

    def _upload_stream(
        self,
        stream: BinaryIO,
        filename: str,
        total: int | None,
        progress: ProgressCallback | None,
        upload_token: str | None,
    ) -> UploadResponse:
        params: dict[str, str] = {"filename": filename}
        if upload_token:
            params["token"] = upload_token
        content: BinaryIO | _ProgressReader = stream
        if progress:
            content = _ProgressReader(stream, total, progress)
        resp = self._transport.request("PUT", f"{self._path}/upload/stream", content=content, params=params)
        return UploadResponse.model_validate(resp.json())

    def __getitem__(self, file_path: str) -> FileResource:
        return FileResource(self._transport, self._bucket_id, file_path)


class FileResource:
    """Operations on a single file."""

    def __init__(self, transport: SyncTransport, bucket_id: str, file_path: str) -> None:
        self._transport = transport
        escaped_bucket = quote(bucket_id, safe="")
        escaped_path = quote(file_path, safe="")
        self._path = f"/api/buckets/{escaped_bucket}/files/{escaped_path}"

    def metadata(self) -> BucketFile:
        resp = self._transport.request("GET", self._path)
        return BucketFile.model_validate(resp.json())

    def download(self) -> bytes:
        resp = self._transport.request("GET", f"{self._path}/content")
        return resp.content

    def download_to(self, dest: str | Path) -> None:
        data = self.download()
        Path(dest).write_bytes(data)

    def delete(self) -> None:
        self._transport.request("DELETE", self._path)

    def append(self, data: bytes) -> BucketFile:
        resp = self._transport.request(
            "PATCH", f"{self._path}/content", content=data, headers={"X-Append": "true"},
        )
        return BucketFile.model_validate(resp.json())

    def patch(self, data: bytes, *, range_start: int, range_end: int, total_size: int) -> BucketFile:
        resp = self._transport.request(
            "PATCH",
            f"{self._path}/content",
            content=data,
            headers={"Content-Range": f"bytes {range_start}-{range_end}/{total_size}"},
        )
        return BucketFile.model_validate(resp.json())


class AsyncFilesResource:
    """Async file operations within a bucket."""

    def __init__(self, transport: AsyncTransport, bucket_id: str) -> None:
        self._transport = transport
        self._bucket_id = bucket_id
        self._path = f"/api/buckets/{quote(bucket_id, safe='')}"

    async def list(self, *, limit: int | None = None, offset: int | None = None,
                   sort: str | None = None, order: str | None = None) -> PaginatedResponse[BucketFile]:
        params = _pagination_params(limit, offset, sort, order)
        resp = await self._transport.request("GET", f"{self._path}/files", params=params)
        return PaginatedResponse[BucketFile].model_validate(resp.json())

    async def list_all(self, *, limit: int = 50, sort: str | None = None, order: str | None = None):
        offset = 0
        while True:
            page = await self.list(limit=limit, offset=offset, sort=sort, order=order)
            yield page
            if offset + limit >= page.total:
                break
            offset += limit

    async def list_directory(self, path: str | None = None, *, limit: int | None = None,
                             offset: int | None = None, sort: str | None = None,
                             order: str | None = None) -> DirectoryListing:
        params = _pagination_params(limit, offset, sort, order)
        if path is not None:
            params["path"] = path
        resp = await self._transport.request("GET", f"{self._path}/ls", params=params)
        return DirectoryListing.model_validate(resp.json())

    async def upload(self, source: str | bytes | BinaryIO | Path, *, filename: str | None = None,
                     progress: ProgressCallback | None = None,
                     upload_token: str | None = None) -> UploadResponse:
        if isinstance(source, (str, Path)):
            path = Path(source)
            filename = filename or path.name
            total = path.stat().st_size
            with open(path, "rb") as f:
                return await self._upload_stream(f, filename, total, progress, upload_token)
        elif isinstance(source, bytes):
            if not filename:
                raise ValueError("filename is required when uploading bytes")
            stream = io.BytesIO(source)
            return await self._upload_stream(stream, filename, len(source), progress, upload_token)
        else:
            if not filename:
                raise ValueError("filename is required when uploading a file object")
            total = None
            if hasattr(source, "seek") and hasattr(source, "tell"):
                pos = source.tell()
                source.seek(0, 2)
                total = source.tell()
                source.seek(pos)
            return await self._upload_stream(source, filename, total, progress, upload_token)

    async def _upload_stream(self, stream, filename, total, progress, upload_token):
        params: dict[str, str] = {"filename": filename}
        if upload_token:
            params["token"] = upload_token
        content = stream
        if progress:
            content = _ProgressReader(stream, total, progress)
        resp = await self._transport.request("PUT", f"{self._path}/upload/stream", content=content, params=params)
        return UploadResponse.model_validate(resp.json())

    def __getitem__(self, file_path: str) -> AsyncFileResource:
        return AsyncFileResource(self._transport, self._bucket_id, file_path)


class AsyncFileResource:
    """Async operations on a single file."""

    def __init__(self, transport: AsyncTransport, bucket_id: str, file_path: str) -> None:
        self._transport = transport
        escaped_bucket = quote(bucket_id, safe="")
        escaped_path = quote(file_path, safe="")
        self._path = f"/api/buckets/{escaped_bucket}/files/{escaped_path}"

    async def metadata(self) -> BucketFile:
        resp = await self._transport.request("GET", self._path)
        return BucketFile.model_validate(resp.json())

    async def download(self) -> bytes:
        resp = await self._transport.request("GET", f"{self._path}/content")
        return resp.content

    async def download_to(self, dest: str | Path) -> None:
        data = await self.download()
        Path(dest).write_bytes(data)

    async def delete(self) -> None:
        await self._transport.request("DELETE", self._path)

    async def append(self, data: bytes) -> BucketFile:
        resp = await self._transport.request(
            "PATCH", f"{self._path}/content", content=data, headers={"X-Append": "true"},
        )
        return BucketFile.model_validate(resp.json())

    async def patch(self, data: bytes, *, range_start: int, range_end: int, total_size: int) -> BucketFile:
        resp = await self._transport.request(
            "PATCH", f"{self._path}/content", content=data,
            headers={"Content-Range": f"bytes {range_start}-{range_end}/{total_size}"},
        )
        return BucketFile.model_validate(resp.json())
```

**Step 4: Run tests**

```bash
cd clients/python && python -m pytest tests/test_files.py tests/test_uploads.py -v
```

Expected: All PASS.

**Step 5: Commit**

```bash
git add clients/python/src/carbonfiles/resources/files.py clients/python/tests/test_files.py clients/python/tests/test_uploads.py
git commit -m "feat(python): add file resources with upload, download, patch, append"
```

---

## Task 6: Admin Resources (Keys, Stats, Short URLs, Dashboard, Tokens)

**Files:**
- Create: `clients/python/src/carbonfiles/resources/keys.py`
- Create: `clients/python/src/carbonfiles/resources/stats.py`
- Create: `clients/python/src/carbonfiles/resources/short_urls.py`
- Create: `clients/python/src/carbonfiles/resources/dashboard.py`
- Create: `clients/python/src/carbonfiles/resources/tokens.py`
- Create: `clients/python/tests/test_keys.py`
- Create: `clients/python/tests/test_tokens.py`
- Create: `clients/python/tests/test_stats.py`
- Create: `clients/python/tests/test_short_urls.py`

**Step 1: Write tests for all admin resources**

Create `clients/python/tests/test_keys.py`:

```python
"""Tests for API key resources."""

import json

import httpx
import pytest

from carbonfiles._transport import SyncTransport
from carbonfiles.models import ApiKey, ApiKeyListItem, ApiKeyUsage, PaginatedResponse
from carbonfiles.resources.keys import KeyResource, KeysResource

SAMPLE_KEY = {
    "key": "cf4_full_secret",
    "prefix": "cf4_full",
    "name": "ci-agent",
    "created_at": "2026-01-01T00:00:00Z",
}

SAMPLE_KEY_LIST_ITEM = {
    "prefix": "cf4_abc",
    "name": "test",
    "created_at": "2026-01-01T00:00:00Z",
    "last_used_at": None,
    "bucket_count": 3,
    "file_count": 10,
    "total_size": 2048,
}


class TestKeysResource:
    def test_create(self):
        def handler(request: httpx.Request) -> httpx.Response:
            body = json.loads(request.content)
            assert body["name"] == "ci-agent"
            return httpx.Response(201, json=SAMPLE_KEY)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = KeysResource(transport)
        key = resource.create("ci-agent")
        assert isinstance(key, ApiKey)
        assert key.prefix == "cf4_full"

    def test_list(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json={
                "items": [SAMPLE_KEY_LIST_ITEM], "total": 1, "limit": 50, "offset": 0
            })

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = KeysResource(transport)
        page = resource.list()
        assert isinstance(page.items[0], ApiKeyListItem)

    def test_getitem(self):
        transport = SyncTransport("https://example.com", api_key="k",
                                   http_transport=lambda r: httpx.Response(200))
        resource = KeysResource(transport)
        key = resource["cf4_abc"]
        assert isinstance(key, KeyResource)


class TestKeyResource:
    def test_revoke(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.method == "DELETE"
            assert b"/api/keys/cf4_abc" in request.url.raw_path
            return httpx.Response(204)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = KeyResource(transport, "cf4_abc")
        resource.revoke()

    def test_usage(self):
        usage_data = {
            **SAMPLE_KEY_LIST_ITEM,
            "total_downloads": 100,
            "buckets": [],
        }

        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json=usage_data)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = KeyResource(transport, "cf4_abc")
        usage = resource.usage()
        assert isinstance(usage, ApiKeyUsage)
        assert usage.total_downloads == 100

    def test_url_escapes_prefix(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert b"cf4_a%2Fb" in request.url.raw_path
            return httpx.Response(204)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = KeyResource(transport, "cf4_a/b")
        resource.revoke()
```

Create `clients/python/tests/test_tokens.py`:

```python
"""Tests for upload token and dashboard token resources."""

import json

import httpx
import pytest

from carbonfiles._transport import SyncTransport
from carbonfiles.models import DashboardToken, DashboardTokenInfo, UploadToken
from carbonfiles.resources.dashboard import DashboardResource
from carbonfiles.resources.tokens import UploadTokensResource


class TestUploadTokensResource:
    def test_create(self):
        token_data = {
            "token": "cfu_abc",
            "bucket_id": "b1",
            "expires_at": "2026-02-01T00:00:00Z",
            "max_uploads": 10,
            "uploads_used": 0,
        }

        def handler(request: httpx.Request) -> httpx.Response:
            body = json.loads(request.content)
            assert body.get("expires_in") == "7d"
            assert body.get("max_uploads") == 10
            return httpx.Response(201, json=token_data)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = UploadTokensResource(transport, "b1")
        token = resource.create(expires_in="7d", max_uploads=10)
        assert isinstance(token, UploadToken)
        assert token.token == "cfu_abc"

    def test_create_minimal(self):
        token_data = {
            "token": "cfu_abc",
            "bucket_id": "b1",
            "expires_at": "2026-02-01T00:00:00Z",
            "max_uploads": None,
            "uploads_used": 0,
        }

        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(201, json=token_data)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = UploadTokensResource(transport, "b1")
        token = resource.create()
        assert token.max_uploads is None


class TestDashboardResource:
    def test_create_token(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(201, json={"token": "jwt_abc", "expires_at": "2026-01-02T00:00:00Z"})

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = DashboardResource(transport)
        token = resource.create_token()
        assert isinstance(token, DashboardToken)

    def test_create_token_with_expiry(self):
        def handler(request: httpx.Request) -> httpx.Response:
            body = json.loads(request.content)
            assert body["expires_in"] == "1h"
            return httpx.Response(201, json={"token": "jwt_abc", "expires_at": "2026-01-02T00:00:00Z"})

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = DashboardResource(transport)
        resource.create_token(expires_in="1h")

    def test_me(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json={"scope": "admin", "expires_at": "2026-01-02T00:00:00Z"})

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = DashboardResource(transport)
        info = resource.me()
        assert isinstance(info, DashboardTokenInfo)
        assert info.scope == "admin"
```

Create `clients/python/tests/test_stats.py`:

```python
"""Tests for stats resource."""

import httpx

from carbonfiles._transport import SyncTransport
from carbonfiles.models import Stats
from carbonfiles.resources.stats import StatsResource

SAMPLE_STATS = {
    "total_buckets": 10,
    "total_files": 50,
    "total_size": 1048576,
    "total_keys": 3,
    "total_downloads": 200,
    "storage_by_owner": [],
}


class TestStatsResource:
    def test_get(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.raw_path == b"/api/stats"
            return httpx.Response(200, json=SAMPLE_STATS)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = StatsResource(transport)
        stats = resource.get()
        assert isinstance(stats, Stats)
        assert stats.total_buckets == 10
```

Create `clients/python/tests/test_short_urls.py`:

```python
"""Tests for short URL resources."""

import httpx

from carbonfiles._transport import SyncTransport
from carbonfiles.resources.short_urls import ShortUrlResource, ShortUrlsResource


class TestShortUrlsResource:
    def test_getitem(self):
        transport = SyncTransport("https://example.com", api_key="k",
                                   http_transport=lambda r: httpx.Response(200))
        resource = ShortUrlsResource(transport)
        short = resource["abc123"]
        assert isinstance(short, ShortUrlResource)


class TestShortUrlResource:
    def test_delete(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.method == "DELETE"
            assert b"/api/short/abc123" in request.url.raw_path
            return httpx.Response(204)

        transport = SyncTransport("https://example.com", api_key="k", http_transport=handler)
        resource = ShortUrlResource(transport, "abc123")
        resource.delete()
```

**Step 2: Run tests to verify they fail**

```bash
cd clients/python && python -m pytest tests/test_keys.py tests/test_tokens.py tests/test_stats.py tests/test_short_urls.py -v
```

**Step 3: Implement all admin resources**

Create `clients/python/src/carbonfiles/resources/keys.py`:

```python
"""API key resources."""

from __future__ import annotations

from typing import TYPE_CHECKING, Generator
from urllib.parse import quote

from carbonfiles.models import ApiKey, ApiKeyListItem, ApiKeyUsage, PaginatedResponse
from carbonfiles.resources.buckets import _pagination_params

if TYPE_CHECKING:
    from carbonfiles._transport import AsyncTransport, SyncTransport


class KeysResource:
    def __init__(self, transport: SyncTransport) -> None:
        self._transport = transport

    def create(self, name: str) -> ApiKey:
        resp = self._transport.request("POST", "/api/keys", json={"name": name})
        return ApiKey.model_validate(resp.json())

    def list(self, *, limit: int | None = None, offset: int | None = None,
             sort: str | None = None, order: str | None = None) -> PaginatedResponse[ApiKeyListItem]:
        params = _pagination_params(limit, offset, sort, order)
        resp = self._transport.request("GET", "/api/keys", params=params)
        return PaginatedResponse[ApiKeyListItem].model_validate(resp.json())

    def __getitem__(self, prefix: str) -> KeyResource:
        return KeyResource(self._transport, prefix)


class KeyResource:
    def __init__(self, transport: SyncTransport, prefix: str) -> None:
        self._transport = transport
        self._path = f"/api/keys/{quote(prefix, safe='')}"

    def revoke(self) -> None:
        self._transport.request("DELETE", self._path)

    def usage(self) -> ApiKeyUsage:
        resp = self._transport.request("GET", f"{self._path}/usage")
        return ApiKeyUsage.model_validate(resp.json())


class AsyncKeysResource:
    def __init__(self, transport: AsyncTransport) -> None:
        self._transport = transport

    async def create(self, name: str) -> ApiKey:
        resp = await self._transport.request("POST", "/api/keys", json={"name": name})
        return ApiKey.model_validate(resp.json())

    async def list(self, *, limit: int | None = None, offset: int | None = None,
                   sort: str | None = None, order: str | None = None) -> PaginatedResponse[ApiKeyListItem]:
        params = _pagination_params(limit, offset, sort, order)
        resp = await self._transport.request("GET", "/api/keys", params=params)
        return PaginatedResponse[ApiKeyListItem].model_validate(resp.json())

    def __getitem__(self, prefix: str) -> AsyncKeyResource:
        return AsyncKeyResource(self._transport, prefix)


class AsyncKeyResource:
    def __init__(self, transport: AsyncTransport, prefix: str) -> None:
        self._transport = transport
        self._path = f"/api/keys/{quote(prefix, safe='')}"

    async def revoke(self) -> None:
        await self._transport.request("DELETE", self._path)

    async def usage(self) -> ApiKeyUsage:
        resp = await self._transport.request("GET", f"{self._path}/usage")
        return ApiKeyUsage.model_validate(resp.json())
```

Create `clients/python/src/carbonfiles/resources/stats.py`:

```python
"""Stats resource."""

from __future__ import annotations

from typing import TYPE_CHECKING

from carbonfiles.models import Stats

if TYPE_CHECKING:
    from carbonfiles._transport import AsyncTransport, SyncTransport


class StatsResource:
    def __init__(self, transport: SyncTransport) -> None:
        self._transport = transport

    def get(self) -> Stats:
        resp = self._transport.request("GET", "/api/stats")
        return Stats.model_validate(resp.json())


class AsyncStatsResource:
    def __init__(self, transport: AsyncTransport) -> None:
        self._transport = transport

    async def get(self) -> Stats:
        resp = await self._transport.request("GET", "/api/stats")
        return Stats.model_validate(resp.json())
```

Create `clients/python/src/carbonfiles/resources/short_urls.py`:

```python
"""Short URL resources."""

from __future__ import annotations

from typing import TYPE_CHECKING
from urllib.parse import quote

if TYPE_CHECKING:
    from carbonfiles._transport import AsyncTransport, SyncTransport


class ShortUrlsResource:
    def __init__(self, transport: SyncTransport) -> None:
        self._transport = transport

    def __getitem__(self, code: str) -> ShortUrlResource:
        return ShortUrlResource(self._transport, code)


class ShortUrlResource:
    def __init__(self, transport: SyncTransport, code: str) -> None:
        self._transport = transport
        self._path = f"/api/short/{quote(code, safe='')}"

    def delete(self) -> None:
        self._transport.request("DELETE", self._path)


class AsyncShortUrlsResource:
    def __init__(self, transport: AsyncTransport) -> None:
        self._transport = transport

    def __getitem__(self, code: str) -> AsyncShortUrlResource:
        return AsyncShortUrlResource(self._transport, code)


class AsyncShortUrlResource:
    def __init__(self, transport: AsyncTransport, code: str) -> None:
        self._transport = transport
        self._path = f"/api/short/{quote(code, safe='')}"

    async def delete(self) -> None:
        await self._transport.request("DELETE", self._path)
```

Create `clients/python/src/carbonfiles/resources/dashboard.py`:

```python
"""Dashboard token resources."""

from __future__ import annotations

from typing import TYPE_CHECKING

from carbonfiles.models import DashboardToken, DashboardTokenInfo

if TYPE_CHECKING:
    from carbonfiles._transport import AsyncTransport, SyncTransport


class DashboardResource:
    def __init__(self, transport: SyncTransport) -> None:
        self._transport = transport

    def create_token(self, *, expires_in: str | None = None) -> DashboardToken:
        body: dict | None = None
        if expires_in is not None:
            body = {"expires_in": expires_in}
        resp = self._transport.request("POST", "/api/tokens/dashboard", json=body)
        return DashboardToken.model_validate(resp.json())

    def me(self) -> DashboardTokenInfo:
        resp = self._transport.request("GET", "/api/tokens/dashboard/me")
        return DashboardTokenInfo.model_validate(resp.json())


class AsyncDashboardResource:
    def __init__(self, transport: AsyncTransport) -> None:
        self._transport = transport

    async def create_token(self, *, expires_in: str | None = None) -> DashboardToken:
        body: dict | None = None
        if expires_in is not None:
            body = {"expires_in": expires_in}
        resp = await self._transport.request("POST", "/api/tokens/dashboard", json=body)
        return DashboardToken.model_validate(resp.json())

    async def me(self) -> DashboardTokenInfo:
        resp = await self._transport.request("GET", "/api/tokens/dashboard/me")
        return DashboardTokenInfo.model_validate(resp.json())
```

Create `clients/python/src/carbonfiles/resources/tokens.py`:

```python
"""Upload token resources."""

from __future__ import annotations

from typing import TYPE_CHECKING
from urllib.parse import quote

from carbonfiles.models import UploadToken

if TYPE_CHECKING:
    from carbonfiles._transport import AsyncTransport, SyncTransport


class UploadTokensResource:
    def __init__(self, transport: SyncTransport, bucket_id: str) -> None:
        self._transport = transport
        self._path = f"/api/buckets/{quote(bucket_id, safe='')}/tokens"

    def create(self, *, expires_in: str | None = None, max_uploads: int | None = None) -> UploadToken:
        body: dict = {}
        if expires_in is not None:
            body["expires_in"] = expires_in
        if max_uploads is not None:
            body["max_uploads"] = max_uploads
        resp = self._transport.request("POST", self._path, json=body if body else None)
        return UploadToken.model_validate(resp.json())


class AsyncUploadTokensResource:
    def __init__(self, transport: AsyncTransport, bucket_id: str) -> None:
        self._transport = transport
        self._path = f"/api/buckets/{quote(bucket_id, safe='')}/tokens"

    async def create(self, *, expires_in: str | None = None, max_uploads: int | None = None) -> UploadToken:
        body: dict = {}
        if expires_in is not None:
            body["expires_in"] = expires_in
        if max_uploads is not None:
            body["max_uploads"] = max_uploads
        resp = await self._transport.request("POST", self._path, json=body if body else None)
        return UploadToken.model_validate(resp.json())
```

**Step 4: Run all tests**

```bash
cd clients/python && python -m pytest tests/test_keys.py tests/test_tokens.py tests/test_stats.py tests/test_short_urls.py -v
```

Expected: All PASS.

**Step 5: Commit**

```bash
git add clients/python/src/carbonfiles/resources/ clients/python/tests/test_keys.py clients/python/tests/test_tokens.py clients/python/tests/test_stats.py clients/python/tests/test_short_urls.py
git commit -m "feat(python): add admin resources — keys, stats, short URLs, dashboard, upload tokens"
```

---

## Task 7: Client Classes

**Files:**
- Create: `clients/python/src/carbonfiles/client.py`
- Create: `clients/python/src/carbonfiles/async_client.py`
- Modify: `clients/python/src/carbonfiles/__init__.py`
- Create: `clients/python/tests/test_client.py`

**Step 1: Write client tests**

Create `clients/python/tests/test_client.py`:

```python
"""Tests for the top-level client classes."""

import httpx
import pytest

from carbonfiles import AsyncCarbonFiles, CarbonFiles, CarbonFilesError
from carbonfiles.models import HealthResponse
from carbonfiles.resources.buckets import AsyncBucketsResource, BucketsResource
from carbonfiles.resources.dashboard import AsyncDashboardResource, DashboardResource
from carbonfiles.resources.keys import AsyncKeysResource, KeysResource
from carbonfiles.resources.short_urls import AsyncShortUrlsResource, ShortUrlsResource
from carbonfiles.resources.stats import AsyncStatsResource, StatsResource


class TestCarbonFiles:
    def test_has_all_resources(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json={})

        client = CarbonFiles("https://example.com", "cf4_key", _http_transport=handler)
        assert isinstance(client.buckets, BucketsResource)
        assert isinstance(client.keys, KeysResource)
        assert isinstance(client.stats, StatsResource)
        assert isinstance(client.short_urls, ShortUrlsResource)
        assert isinstance(client.dashboard, DashboardResource)

    def test_health(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.raw_path == b"/healthz"
            return httpx.Response(200, json={"status": "healthy", "uptime_seconds": 100, "db": "ok"})

        client = CarbonFiles("https://example.com", _http_transport=handler)
        health = client.health()
        assert isinstance(health, HealthResponse)
        assert health.status == "healthy"

    def test_context_manager(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json={})

        with CarbonFiles("https://example.com", _http_transport=handler) as client:
            assert isinstance(client, CarbonFiles)

    def test_no_api_key(self):
        def handler(request: httpx.Request) -> httpx.Response:
            assert "authorization" not in request.headers
            return httpx.Response(200, json={"status": "healthy", "uptime_seconds": 0, "db": "ok"})

        client = CarbonFiles("https://example.com", _http_transport=handler)
        client.health()


class TestAsyncCarbonFiles:
    @pytest.mark.asyncio
    async def test_has_all_resources(self):
        async def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json={})

        client = AsyncCarbonFiles("https://example.com", "cf4_key", _http_transport=handler)
        assert isinstance(client.buckets, AsyncBucketsResource)
        assert isinstance(client.keys, AsyncKeysResource)
        assert isinstance(client.stats, AsyncStatsResource)
        assert isinstance(client.short_urls, AsyncShortUrlsResource)
        assert isinstance(client.dashboard, AsyncDashboardResource)

    @pytest.mark.asyncio
    async def test_health(self):
        async def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json={"status": "healthy", "uptime_seconds": 100, "db": "ok"})

        client = AsyncCarbonFiles("https://example.com", _http_transport=handler)
        health = await client.health()
        assert isinstance(health, HealthResponse)

    @pytest.mark.asyncio
    async def test_context_manager(self):
        async def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json={})

        async with AsyncCarbonFiles("https://example.com", _http_transport=handler) as client:
            assert isinstance(client, AsyncCarbonFiles)
```

**Step 2: Run tests to verify they fail**

```bash
cd clients/python && python -m pytest tests/test_client.py -v
```

**Step 3: Implement client classes**

Create `clients/python/src/carbonfiles/client.py`:

```python
"""Synchronous CarbonFiles client."""

from __future__ import annotations

from typing import Any

from carbonfiles._transport import SyncTransport
from carbonfiles.models import HealthResponse
from carbonfiles.resources.buckets import BucketsResource
from carbonfiles.resources.dashboard import DashboardResource
from carbonfiles.resources.keys import KeysResource
from carbonfiles.resources.short_urls import ShortUrlsResource
from carbonfiles.resources.stats import StatsResource


class CarbonFiles:
    """Synchronous client for the CarbonFiles API.

    Usage:
        cf = CarbonFiles("https://files.example.com", "cf4_your_api_key")
        bucket = cf.buckets.create("my-project")

    As a context manager:
        with CarbonFiles("https://files.example.com", "cf4_key") as cf:
            ...
    """

    def __init__(
        self,
        base_url: str,
        api_key: str | None = None,
        *,
        timeout: float = 30.0,
        _http_transport: Any = None,
    ) -> None:
        self._transport = SyncTransport(base_url, api_key, timeout, http_transport=_http_transport)
        self.buckets = BucketsResource(self._transport)
        self.keys = KeysResource(self._transport)
        self.stats = StatsResource(self._transport)
        self.short_urls = ShortUrlsResource(self._transport)
        self.dashboard = DashboardResource(self._transport)

    def health(self) -> HealthResponse:
        resp = self._transport.request("GET", "/healthz")
        return HealthResponse.model_validate(resp.json())

    def close(self) -> None:
        self._transport.close()

    def __enter__(self) -> CarbonFiles:
        return self

    def __exit__(self, *args: object) -> None:
        self.close()
```

Create `clients/python/src/carbonfiles/async_client.py`:

```python
"""Asynchronous CarbonFiles client."""

from __future__ import annotations

from typing import Any

from carbonfiles._transport import AsyncTransport
from carbonfiles.models import HealthResponse
from carbonfiles.resources.buckets import AsyncBucketsResource
from carbonfiles.resources.dashboard import AsyncDashboardResource
from carbonfiles.resources.keys import AsyncKeysResource
from carbonfiles.resources.short_urls import AsyncShortUrlsResource
from carbonfiles.resources.stats import AsyncStatsResource


class AsyncCarbonFiles:
    """Asynchronous client for the CarbonFiles API.

    Usage:
        async with AsyncCarbonFiles("https://files.example.com", "cf4_key") as cf:
            bucket = await cf.buckets.create("my-project")
    """

    def __init__(
        self,
        base_url: str,
        api_key: str | None = None,
        *,
        timeout: float = 30.0,
        _http_transport: Any = None,
    ) -> None:
        self._transport = AsyncTransport(base_url, api_key, timeout, http_transport=_http_transport)
        self.buckets = AsyncBucketsResource(self._transport)
        self.keys = AsyncKeysResource(self._transport)
        self.stats = AsyncStatsResource(self._transport)
        self.short_urls = AsyncShortUrlsResource(self._transport)
        self.dashboard = AsyncDashboardResource(self._transport)

    async def health(self) -> HealthResponse:
        resp = await self._transport.request("GET", "/healthz")
        return HealthResponse.model_validate(resp.json())

    async def aclose(self) -> None:
        await self._transport.aclose()

    async def __aenter__(self) -> AsyncCarbonFiles:
        return self

    async def __aexit__(self, *args: object) -> None:
        await self.aclose()
```

Update `clients/python/src/carbonfiles/__init__.py`:

```python
"""CarbonFiles Python SDK."""

from carbonfiles._transport import CarbonFilesError
from carbonfiles.async_client import AsyncCarbonFiles
from carbonfiles.client import CarbonFiles

__all__ = ["AsyncCarbonFiles", "CarbonFiles", "CarbonFilesError"]
```

**Step 4: Run ALL tests**

```bash
cd clients/python && python -m pytest -v
```

Expected: All PASS across all test files.

**Step 5: Commit**

```bash
git add clients/python/src/carbonfiles/ clients/python/tests/test_client.py
git commit -m "feat(python): add CarbonFiles and AsyncCarbonFiles client classes"
```

---

## Task 8: Pagination Tests

**Files:**
- Create: `clients/python/tests/test_pagination.py`

**Step 1: Write pagination tests**

Create `clients/python/tests/test_pagination.py`:

```python
"""Tests for auto-pagination (list_all) across resources."""

import httpx
import pytest

from carbonfiles import CarbonFiles


SAMPLE_BUCKET = {
    "id": "abc", "name": "test", "owner": "o",
    "created_at": "2026-01-01T00:00:00Z", "file_count": 0, "total_size": 0,
}

SAMPLE_FILE = {
    "path": "f.txt", "name": "f.txt", "size": 0, "mime_type": "text/plain",
    "created_at": "2026-01-01T00:00:00Z", "updated_at": "2026-01-01T00:00:00Z",
}


class TestBucketListAll:
    def test_single_page(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json={
                "items": [SAMPLE_BUCKET], "total": 1, "limit": 50, "offset": 0
            })

        cf = CarbonFiles("https://example.com", "k", _http_transport=handler)
        pages = list(cf.buckets.list_all())
        assert len(pages) == 1
        assert len(pages[0].items) == 1

    def test_multiple_pages(self):
        call_count = 0

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal call_count
            call_count += 1
            offset = int(request.url.params.get("offset", "0"))
            if offset == 0:
                return httpx.Response(200, json={
                    "items": [SAMPLE_BUCKET, SAMPLE_BUCKET], "total": 5, "limit": 2, "offset": 0
                })
            elif offset == 2:
                return httpx.Response(200, json={
                    "items": [SAMPLE_BUCKET, SAMPLE_BUCKET], "total": 5, "limit": 2, "offset": 2
                })
            else:
                return httpx.Response(200, json={
                    "items": [SAMPLE_BUCKET], "total": 5, "limit": 2, "offset": 4
                })

        cf = CarbonFiles("https://example.com", "k", _http_transport=handler)
        pages = list(cf.buckets.list_all(limit=2))
        assert len(pages) == 3
        assert call_count == 3

    def test_empty_result(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json={"items": [], "total": 0, "limit": 50, "offset": 0})

        cf = CarbonFiles("https://example.com", "k", _http_transport=handler)
        pages = list(cf.buckets.list_all())
        assert len(pages) == 1
        assert len(pages[0].items) == 0


class TestFileListAll:
    def test_file_pagination(self):
        call_count = 0

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal call_count
            call_count += 1
            offset = int(request.url.params.get("offset", "0"))
            if offset == 0:
                return httpx.Response(200, json={
                    "items": [SAMPLE_FILE], "total": 2, "limit": 1, "offset": 0
                })
            else:
                return httpx.Response(200, json={
                    "items": [SAMPLE_FILE], "total": 2, "limit": 1, "offset": 1
                })

        cf = CarbonFiles("https://example.com", "k", _http_transport=handler)
        pages = list(cf.buckets["b1"].files.list_all(limit=1))
        assert len(pages) == 2
```

**Step 2: Run tests**

```bash
cd clients/python && python -m pytest tests/test_pagination.py -v
```

Expected: All PASS.

**Step 3: Commit**

```bash
git add clients/python/tests/test_pagination.py
git commit -m "test(python): add pagination tests for list_all()"
```

---

## Task 9: Update CI Workflow

**Files:**
- Modify: `.github/workflows/publish-clients.yml` (lines 122-164)

**Step 1: Update the Python publish job**

The CI workflow needs to be updated since we no longer generate the client. Replace the `publish-python` job to:
1. Remove the openapi-python-client generation step
2. Build directly from `clients/python/`
3. Update the packages-dir path

Replace lines 122-164 with:

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
          sed -i "s/^version = .*/version = \"${{ needs.export-spec.outputs.version }}\"/" pyproject.toml
          pip install build && python -m build

      - name: Publish
        uses: pypa/gh-action-pypi-publish@release/v1
        with:
          packages-dir: clients/python/dist/
```

**Step 2: Commit**

```bash
git add .github/workflows/publish-clients.yml
git commit -m "ci: update Python publishing workflow for handcrafted SDK"
```

---

## Task 10: README

**Files:**
- Modify: `clients/python/README.md`

**Step 1: Write the README**

Match the structure and quality of the C# README (`clients/csharp/README.md`). Cover:

1. One-line description
2. Installation (`pip install carbonfiles`)
3. Quick start (create bucket example)
4. Fluent API overview
5. Uploads section (from path, bytes, file object, with progress, with token)
6. Pagination section
7. File operations section
8. Authentication section (table of 4 token types)
9. Error handling section
10. Async usage section
11. API reference tables (matching C# README format)
12. Links

Use Python code examples throughout. Keep it concise — reference the C# README for tone and structure.

**Step 2: Commit**

```bash
git add clients/python/README.md
git commit -m "docs(python): add comprehensive README for handcrafted SDK"
```

---

## Task 11: Final Verification

**Step 1: Run full test suite**

```bash
cd clients/python && python -m pytest -v --tb=short
```

Expected: All tests PASS.

**Step 2: Run linter**

```bash
cd clients/python && ruff check src/ tests/ && ruff format --check src/ tests/
```

Expected: No issues.

**Step 3: Verify package builds**

```bash
cd clients/python && pip install build && python -m build
```

Expected: Builds wheel and sdist successfully.

**Step 4: Verify imports work**

```bash
cd clients/python && python -c "from carbonfiles import CarbonFiles, AsyncCarbonFiles, CarbonFilesError; print('OK')"
```

Expected: Prints "OK".

**Step 5: Count tests**

```bash
cd clients/python && python -m pytest --collect-only -q | tail -1
```

Expected: 80+ tests collected.

**Step 6: Final commit (if any fixes needed)**

```bash
git add -A clients/python/
git commit -m "fix(python): address issues found during final verification"
```

---

## Summary

| Task | Description | Est. Tests |
|------|-------------|-----------|
| 0 | Project scaffold | 0 |
| 1 | Transport layer | ~15 |
| 2 | Pydantic models | ~20 |
| 3 | Types module | 0 |
| 4 | Bucket resources | ~13 |
| 5 | File resources + uploads | ~20 |
| 6 | Admin resources | ~12 |
| 7 | Client classes | ~8 |
| 8 | Pagination tests | ~5 |
| 9 | CI workflow update | 0 |
| 10 | README | 0 |
| 11 | Final verification | 0 |
| **Total** | | **~93** |
