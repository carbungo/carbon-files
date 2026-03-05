"""Tests for Pydantic model serialization and deserialization."""

from __future__ import annotations

from datetime import datetime, timezone

from carbonfiles.models import (
    ApiKeyListItem,
    ApiKeyResponse,
    ApiKeyUsageResponse,
    Bucket,
    BucketDetail,
    BucketFile,
    DashboardTokenInfo,
    DashboardTokenResponse,
    DirectoryEntry,
    DirectoryListingResponse,
    ErrorResponse,
    FileTreeResponse,
    HealthResponse,
    OwnerStats,
    PaginatedResponse,
    StatsResponse,
    UploadedFile,
    UploadResponse,
    UploadTokenResponse,
    VerifyResponse,
)

NOW = datetime(2026, 3, 4, 12, 0, 0, tzinfo=timezone.utc)
NOW_STR = "2026-03-04T12:00:00Z"


# ---------- common.py ----------


class TestErrorResponse:
    def test_with_hint(self) -> None:
        data = {"error": "not found", "hint": "check the bucket id"}
        model = ErrorResponse.model_validate(data)
        assert model.error == "not found"
        assert model.hint == "check the bucket id"

    def test_without_hint(self) -> None:
        data = {"error": "unauthorized"}
        model = ErrorResponse.model_validate(data)
        assert model.error == "unauthorized"
        assert model.hint is None

    def test_roundtrip(self) -> None:
        data = {"error": "bad request", "hint": "missing name"}
        model = ErrorResponse.model_validate(data)
        dumped = model.model_dump(exclude_none=True)
        assert dumped == data


class TestPaginatedResponse:
    def test_paginated_buckets(self) -> None:
        data = {
            "items": [
                {
                    "id": "abc123",
                    "name": "test",
                    "owner": "owner1",
                    "created_at": NOW_STR,
                    "file_count": 5,
                    "total_size": 1024,
                }
            ],
            "total": 1,
            "limit": 20,
            "offset": 0,
        }
        model = PaginatedResponse[Bucket].model_validate(data)
        assert model.total == 1
        assert model.limit == 20
        assert model.offset == 0
        assert len(model.items) == 1
        assert model.items[0].id == "abc123"
        assert model.items[0].name == "test"


# ---------- buckets.py ----------


def _make_bucket_data(**overrides: object) -> dict:
    base = {
        "id": "bkt_abc123",
        "name": "my-bucket",
        "owner": "owner1",
        "description": "A test bucket",
        "created_at": NOW_STR,
        "expires_at": "2026-04-04T12:00:00Z",
        "last_used_at": NOW_STR,
        "file_count": 10,
        "total_size": 2048,
    }
    base.update(overrides)
    return base


class TestBucket:
    def test_full(self) -> None:
        model = Bucket.model_validate(_make_bucket_data())
        assert model.id == "bkt_abc123"
        assert model.name == "my-bucket"
        assert model.owner == "owner1"
        assert model.description == "A test bucket"
        assert model.file_count == 10
        assert model.total_size == 2048
        assert model.expires_at is not None
        assert model.last_used_at is not None

    def test_minimal(self) -> None:
        data = _make_bucket_data(description=None, expires_at=None, last_used_at=None)
        model = Bucket.model_validate(data)
        assert model.description is None
        assert model.expires_at is None
        assert model.last_used_at is None

    def test_roundtrip(self) -> None:
        data = _make_bucket_data()
        model = Bucket.model_validate(data)
        dumped = model.model_dump(mode="json")
        model2 = Bucket.model_validate(dumped)
        assert model2 == model


class TestBucketDetail:
    def test_with_files(self) -> None:
        data = {
            **_make_bucket_data(),
            "unique_content_count": 8,
            "unique_content_size": 1500,
            "has_more_files": True,
            "files": [
                {
                    "path": "/docs/readme.txt",
                    "name": "readme.txt",
                    "size": 256,
                    "mime_type": "text/plain",
                    "short_code": "abc123",
                    "created_at": NOW_STR,
                    "updated_at": NOW_STR,
                }
            ],
        }
        model = BucketDetail.model_validate(data)
        assert model.unique_content_count == 8
        assert model.unique_content_size == 1500
        assert model.has_more_files is True
        assert model.files is not None
        assert len(model.files) == 1
        assert model.files[0].path == "/docs/readme.txt"

    def test_without_files(self) -> None:
        data = {**_make_bucket_data()}
        model = BucketDetail.model_validate(data)
        assert model.unique_content_count == 0
        assert model.unique_content_size == 0
        assert model.files is None
        assert model.has_more_files is False


# ---------- files.py ----------


def _make_file_data(**overrides: object) -> dict:
    base = {
        "path": "/images/photo.jpg",
        "name": "photo.jpg",
        "size": 50000,
        "mime_type": "image/jpeg",
        "short_code": "xYz789",
        "short_url": "https://example.com/s/xYz789",
        "sha256": "abcdef1234567890",
        "created_at": NOW_STR,
        "updated_at": NOW_STR,
    }
    base.update(overrides)
    return base


class TestBucketFile:
    def test_full(self) -> None:
        model = BucketFile.model_validate(_make_file_data())
        assert model.path == "/images/photo.jpg"
        assert model.name == "photo.jpg"
        assert model.size == 50000
        assert model.mime_type == "image/jpeg"
        assert model.short_code == "xYz789"
        assert model.short_url == "https://example.com/s/xYz789"
        assert model.sha256 == "abcdef1234567890"

    def test_minimal(self) -> None:
        data = _make_file_data(short_code=None, short_url=None, sha256=None)
        model = BucketFile.model_validate(data)
        assert model.short_code is None
        assert model.short_url is None
        assert model.sha256 is None


class TestUploadResponse:
    def test_with_dedup(self) -> None:
        data = {
            "uploaded": [
                {
                    "path": "/docs/file.txt",
                    "name": "file.txt",
                    "size": 100,
                    "mime_type": "text/plain",
                    "short_code": "abc",
                    "sha256": "deadbeef",
                    "deduplicated": True,
                    "created_at": NOW_STR,
                    "updated_at": NOW_STR,
                },
                {
                    "path": "/docs/file2.txt",
                    "name": "file2.txt",
                    "size": 200,
                    "mime_type": "text/plain",
                    "deduplicated": False,
                    "created_at": NOW_STR,
                    "updated_at": NOW_STR,
                },
            ]
        }
        model = UploadResponse.model_validate(data)
        assert len(model.uploaded) == 2
        assert model.uploaded[0].deduplicated is True
        assert model.uploaded[1].deduplicated is False


class TestVerifyResponse:
    def test_valid(self) -> None:
        data = {
            "path": "/docs/file.txt",
            "stored_hash": "aaa",
            "computed_hash": "aaa",
            "valid": True,
        }
        model = VerifyResponse.model_validate(data)
        assert model.valid is True
        assert model.stored_hash == model.computed_hash

    def test_invalid(self) -> None:
        data = {
            "path": "/docs/file.txt",
            "stored_hash": "aaa",
            "computed_hash": "bbb",
            "valid": False,
        }
        model = VerifyResponse.model_validate(data)
        assert model.valid is False


class TestFileTreeResponse:
    def test_with_directories_and_cursor(self) -> None:
        data = {
            "prefix": "docs/",
            "delimiter": "/",
            "directories": [
                {"path": "docs/sub/", "file_count": 3, "total_size": 900},
            ],
            "files": [_make_file_data()],
            "total_files": 1,
            "total_directories": 1,
            "cursor": "next_page_token",
        }
        model = FileTreeResponse.model_validate(data)
        assert model.prefix == "docs/"
        assert model.delimiter == "/"
        assert len(model.directories) == 1
        assert model.directories[0].path == "docs/sub/"
        assert model.directories[0].file_count == 3
        assert len(model.files) == 1
        assert model.cursor == "next_page_token"

    def test_no_prefix_no_cursor(self) -> None:
        data = {
            "delimiter": "/",
            "directories": [],
            "files": [],
            "total_files": 0,
            "total_directories": 0,
        }
        model = FileTreeResponse.model_validate(data)
        assert model.prefix is None
        assert model.cursor is None


class TestDirectoryListingResponse:
    def test_basic(self) -> None:
        data = {
            "files": [_make_file_data()],
            "folders": ["images/", "docs/"],
            "total_files": 1,
            "total_folders": 2,
            "limit": 50,
            "offset": 0,
        }
        model = DirectoryListingResponse.model_validate(data)
        assert len(model.files) == 1
        assert model.folders == ["images/", "docs/"]
        assert model.total_files == 1
        assert model.total_folders == 2
        assert model.limit == 50
        assert model.offset == 0


# ---------- keys.py ----------


class TestApiKeyResponse:
    def test_basic(self) -> None:
        data = {
            "key": "cf4_secret_key_value",
            "prefix": "cf4_se",
            "name": "my-key",
            "created_at": NOW_STR,
        }
        model = ApiKeyResponse.model_validate(data)
        assert model.key == "cf4_secret_key_value"
        assert model.prefix == "cf4_se"
        assert model.name == "my-key"


class TestApiKeyListItem:
    def test_with_last_used(self) -> None:
        data = {
            "prefix": "cf4_ab",
            "name": "prod-key",
            "created_at": NOW_STR,
            "last_used_at": NOW_STR,
            "bucket_count": 3,
            "file_count": 50,
            "total_size": 1_000_000,
        }
        model = ApiKeyListItem.model_validate(data)
        assert model.prefix == "cf4_ab"
        assert model.last_used_at is not None
        assert model.bucket_count == 3

    def test_without_last_used(self) -> None:
        data = {
            "prefix": "cf4_cd",
            "name": "new-key",
            "created_at": NOW_STR,
            "bucket_count": 0,
            "file_count": 0,
            "total_size": 0,
        }
        model = ApiKeyListItem.model_validate(data)
        assert model.last_used_at is None


class TestApiKeyUsageResponse:
    def test_with_buckets(self) -> None:
        data = {
            "prefix": "cf4_ab",
            "name": "prod-key",
            "created_at": NOW_STR,
            "last_used_at": NOW_STR,
            "bucket_count": 1,
            "file_count": 10,
            "total_size": 5000,
            "total_downloads": 100,
            "buckets": [_make_bucket_data()],
        }
        model = ApiKeyUsageResponse.model_validate(data)
        assert model.total_downloads == 100
        assert len(model.buckets) == 1
        assert model.buckets[0].id == "bkt_abc123"


# ---------- tokens.py ----------


class TestUploadTokenResponse:
    def test_with_max_uploads(self) -> None:
        data = {
            "token": "cfu_token_value",
            "bucket_id": "bkt_abc123",
            "expires_at": NOW_STR,
            "max_uploads": 10,
            "uploads_used": 3,
        }
        model = UploadTokenResponse.model_validate(data)
        assert model.token == "cfu_token_value"
        assert model.bucket_id == "bkt_abc123"
        assert model.max_uploads == 10
        assert model.uploads_used == 3

    def test_without_max_uploads(self) -> None:
        data = {
            "token": "cfu_token_value",
            "bucket_id": "bkt_abc123",
            "expires_at": NOW_STR,
            "uploads_used": 0,
        }
        model = UploadTokenResponse.model_validate(data)
        assert model.max_uploads is None


class TestDashboardTokenResponse:
    def test_basic(self) -> None:
        data = {"token": "jwt_token_here", "expires_at": NOW_STR}
        model = DashboardTokenResponse.model_validate(data)
        assert model.token == "jwt_token_here"


class TestDashboardTokenInfo:
    def test_basic(self) -> None:
        data = {"scope": "admin", "expires_at": NOW_STR}
        model = DashboardTokenInfo.model_validate(data)
        assert model.scope == "admin"


# ---------- stats.py ----------


class TestStatsResponse:
    def test_with_storage_by_owner(self) -> None:
        data = {
            "total_buckets": 5,
            "total_files": 100,
            "total_size": 500_000,
            "total_keys": 3,
            "total_downloads": 1000,
            "storage_by_owner": [
                {"owner": "user1", "bucket_count": 3, "file_count": 60, "total_size": 300_000},
                {"owner": "user2", "bucket_count": 2, "file_count": 40, "total_size": 200_000},
            ],
        }
        model = StatsResponse.model_validate(data)
        assert model.total_buckets == 5
        assert model.total_downloads == 1000
        assert len(model.storage_by_owner) == 2
        assert model.storage_by_owner[0].owner == "user1"
        assert model.storage_by_owner[1].total_size == 200_000


class TestHealthResponse:
    def test_basic(self) -> None:
        data = {"status": "healthy", "uptime_seconds": 3600, "db": "ok"}
        model = HealthResponse.model_validate(data)
        assert model.status == "healthy"
        assert model.uptime_seconds == 3600
        assert model.db == "ok"
