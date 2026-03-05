from __future__ import annotations

import httpx
import pytest

from carbonfiles.exceptions import CarbonFilesError
from carbonfiles.models.common import PaginatedResponse
from carbonfiles.models.files import (
    BucketFile,
    DirectoryListingResponse,
    FileTreeResponse,
    VerifyResponse,
)
from carbonfiles.resources.files import FileResource, FilesResource
from carbonfiles.transport import SyncTransport

BUCKET_FILE_JSON = {
    "path": "test.txt",
    "name": "test.txt",
    "size": 100,
    "mime_type": "text/plain",
    "created_at": "2026-01-01T00:00:00Z",
    "updated_at": "2026-01-01T00:00:00Z",
}

BUCKET_FILE_WITH_SHA_JSON = {
    **BUCKET_FILE_JSON,
    "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
}

VERIFY_JSON = {
    "path": "file.txt",
    "stored_hash": "abc123",
    "computed_hash": "abc123",
    "valid": True,
}


def make_transport(handler, api_key="test-key"):
    mock = httpx.MockTransport(handler)
    client = httpx.Client(transport=mock, base_url="https://example.com")
    return SyncTransport("https://example.com", api_key, http_client=client)


# ---------------------------------------------------------------------------
# FilesResource tests
# ---------------------------------------------------------------------------


class TestFilesResource:
    def test_list_with_pagination(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(
                200,
                json={
                    "items": [BUCKET_FILE_JSON],
                    "total": 1,
                    "limit": 10,
                    "offset": 0,
                },
            )

        transport = make_transport(handler)
        resource = FilesResource(transport, "b1")
        result = resource.list(limit=10, sort="name")

        req = requests_log[0]
        assert req.method == "GET"
        assert req.url.path == "/api/buckets/b1/files"
        params = dict(req.url.params)
        assert params["limit"] == "10"
        assert params["sort"] == "name"

        assert isinstance(result, PaginatedResponse)
        assert result.total == 1
        assert len(result.items) == 1

    def test_list_tree_with_delimiter(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(
                200,
                json={
                    "prefix": "docs/",
                    "delimiter": "/",
                    "directories": [
                        {"path": "docs/sub/", "file_count": 3, "total_size": 1024}
                    ],
                    "files": [BUCKET_FILE_JSON],
                    "total_files": 1,
                    "total_directories": 1,
                },
            )

        transport = make_transport(handler)
        resource = FilesResource(transport, "b1")
        result = resource.list_tree(prefix="docs/", limit=50)

        req = requests_log[0]
        assert req.method == "GET"
        assert req.url.path == "/api/buckets/b1/files"
        params = dict(req.url.params)
        assert params["delimiter"] == "/"
        assert params["prefix"] == "docs/"
        assert params["limit"] == "50"

        assert isinstance(result, FileTreeResponse)
        assert result.delimiter == "/"
        assert len(result.directories) == 1
        assert len(result.files) == 1

    def test_list_tree_with_cursor(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(
                200,
                json={
                    "delimiter": "/",
                    "directories": [],
                    "files": [],
                    "total_files": 0,
                    "total_directories": 0,
                    "cursor": "next-page",
                },
            )

        transport = make_transport(handler)
        resource = FilesResource(transport, "b1")
        result = resource.list_tree(cursor="abc123")

        params = dict(requests_log[0].url.params)
        assert params["cursor"] == "abc123"
        assert result.cursor == "next-page"

    def test_list_directory_with_path(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(
                200,
                json={
                    "files": [BUCKET_FILE_JSON],
                    "folders": ["sub/"],
                    "total_files": 1,
                    "total_folders": 1,
                    "limit": 50,
                    "offset": 0,
                },
            )

        transport = make_transport(handler)
        resource = FilesResource(transport, "b1")
        result = resource.list_directory("docs")

        req = requests_log[0]
        assert req.method == "GET"
        assert req.url.path == "/api/buckets/b1/ls"
        params = dict(req.url.params)
        assert params["path"] == "docs"

        assert isinstance(result, DirectoryListingResponse)
        assert len(result.files) == 1
        assert result.folders == ["sub/"]

    def test_getitem_returns_file_resource(self):
        transport = make_transport(lambda _: httpx.Response(200))
        resource = FilesResource(transport, "b1")
        file_resource = resource["readme.txt"]
        assert isinstance(file_resource, FileResource)


# ---------------------------------------------------------------------------
# FileResource tests
# ---------------------------------------------------------------------------


class TestFileResource:
    def test_metadata_returns_bucket_file(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=BUCKET_FILE_JSON)

        transport = make_transport(handler)
        resource = FileResource(transport, "b1", "readme.txt")
        result = resource.metadata()

        assert requests_log[0].method == "GET"
        assert requests_log[0].url.path == "/api/buckets/b1/files/readme.txt"
        assert isinstance(result, BucketFile)
        assert result.name == "test.txt"

    def test_download_returns_bytes(self):
        requests_log: list[httpx.Request] = []
        file_content = b"hello world"

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, content=file_content)

        transport = make_transport(handler)
        resource = FileResource(transport, "b1", "test.txt")
        result = resource.download()

        assert requests_log[0].method == "GET"
        assert requests_log[0].url.path == "/api/buckets/b1/files/test.txt/content"
        assert result == file_content

    def test_download_to_writes_file(self, tmp_path):
        file_content = b"hello world"

        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, content=file_content)

        transport = make_transport(handler)
        resource = FileResource(transport, "b1", "test.txt")
        dest = tmp_path / "downloaded.txt"
        resource.download_to(dest)

        assert dest.read_bytes() == file_content

    def test_delete_sends_delete(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(204)

        transport = make_transport(handler)
        resource = FileResource(transport, "b1", "test.txt")
        resource.delete()

        assert requests_log[0].method == "DELETE"
        assert requests_log[0].url.path == "/api/buckets/b1/files/test.txt"

    def test_verify_returns_verify_response(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=VERIFY_JSON)

        transport = make_transport(handler)
        resource = FileResource(transport, "b1", "file.txt")
        result = resource.verify()

        assert requests_log[0].method == "GET"
        assert requests_log[0].url.path == "/api/buckets/b1/files/file.txt/verify"
        assert isinstance(result, VerifyResponse)
        assert result.valid is True
        assert result.stored_hash == "abc123"

    def test_verify_throws_on_not_found(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(404, json={"error": "File not found"})

        transport = make_transport(handler)
        resource = FileResource(transport, "b1", "missing.txt")

        with pytest.raises(CarbonFilesError) as exc_info:
            resource.verify()

        assert exc_info.value.status_code == 404

    def test_append_sends_patch_with_x_append(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=BUCKET_FILE_JSON)

        transport = make_transport(handler)
        resource = FileResource(transport, "b1", "log.txt")
        result = resource.append(b"new data")

        req = requests_log[0]
        assert req.method == "PATCH"
        assert req.url.path == "/api/buckets/b1/files/log.txt/content"
        assert req.headers["x-append"] == "true"
        assert req.headers["content-type"] == "application/octet-stream"
        assert req.content == b"new data"
        assert isinstance(result, BucketFile)

    def test_patch_sends_content_range_header(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=BUCKET_FILE_JSON)

        transport = make_transport(handler)
        resource = FileResource(transport, "b1", "test.bin")
        result = resource.patch(b"\x00" * 512, range_start=0, range_end=511, total_size=1024)

        req = requests_log[0]
        assert req.method == "PATCH"
        assert req.url.path == "/api/buckets/b1/files/test.bin/content"
        assert req.headers["content-range"] == "bytes 0-511/1024"
        assert req.headers["content-type"] == "application/octet-stream"
        assert isinstance(result, BucketFile)

    def test_patch_throws_on_not_found(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(404, json={"error": "File not found"})

        transport = make_transport(handler)
        resource = FileResource(transport, "b1", "missing.bin")

        with pytest.raises(CarbonFilesError) as exc_info:
            resource.patch(b"\x00", range_start=0, range_end=0, total_size=1)

        assert exc_info.value.status_code == 404

    def test_special_chars_in_path_escaped(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=BUCKET_FILE_JSON)

        transport = make_transport(handler)
        resource = FileResource(transport, "b1", "docs/readme.txt")
        resource.metadata()

        # httpx decodes percent-encoding in .path, so check raw_path
        assert requests_log[0].url.raw_path == b"/api/buckets/b1/files/docs%2Freadme.txt"

    def test_metadata_deserializes_sha256(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json=BUCKET_FILE_WITH_SHA_JSON)

        transport = make_transport(handler)
        resource = FileResource(transport, "b1", "test.txt")
        result = resource.metadata()

        assert result.sha256 == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
