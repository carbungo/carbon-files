from __future__ import annotations

from io import BytesIO

import httpx
import pytest

from carbonfiles.models.files import UploadResponse
from carbonfiles.resources.files import FilesResource
from carbonfiles.transport import SyncTransport

UPLOAD_RESPONSE_JSON = {
    "uploaded": [
        {
            "path": "test.txt",
            "name": "test.txt",
            "size": 100,
            "mime_type": "text/plain",
            "created_at": "2026-01-01T00:00:00Z",
            "updated_at": "2026-01-01T00:00:00Z",
        }
    ]
}

UPLOAD_RESPONSE_DEDUP_JSON = {
    "uploaded": [
        {
            "path": "test.txt",
            "name": "test.txt",
            "size": 100,
            "mime_type": "text/plain",
            "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            "deduplicated": True,
            "created_at": "2026-01-01T00:00:00Z",
            "updated_at": "2026-01-01T00:00:00Z",
        }
    ]
}


def make_transport(handler, api_key="test-key"):
    mock = httpx.MockTransport(handler)
    client = httpx.Client(transport=mock, base_url="https://example.com")
    return SyncTransport("https://example.com", api_key, http_client=client)


class TestUpload:
    def test_upload_bytes_sends_put(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=UPLOAD_RESPONSE_JSON)

        transport = make_transport(handler)
        resource = FilesResource(transport, "b1")
        result = resource.upload(b"content", filename="test.txt")

        req = requests_log[0]
        assert req.method == "PUT"
        assert req.url.path == "/api/buckets/b1/upload/stream"
        params = dict(req.url.params)
        assert params["filename"] == "test.txt"
        assert isinstance(result, UploadResponse)
        assert len(result.uploaded) == 1

    def test_upload_file_path_derives_filename(self, tmp_path):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=UPLOAD_RESPONSE_JSON)

        transport = make_transport(handler)
        resource = FilesResource(transport, "b1")

        test_file = tmp_path / "file.txt"
        test_file.write_text("hello")
        result = resource.upload(test_file)

        req = requests_log[0]
        assert req.method == "PUT"
        params = dict(req.url.params)
        assert params["filename"] == "file.txt"
        assert isinstance(result, UploadResponse)

    def test_upload_file_path_overrides_filename(self, tmp_path):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=UPLOAD_RESPONSE_JSON)

        transport = make_transport(handler)
        resource = FilesResource(transport, "b1")

        test_file = tmp_path / "file.txt"
        test_file.write_text("hello")
        resource.upload(test_file, filename="custom.txt")

        params = dict(requests_log[0].url.params)
        assert params["filename"] == "custom.txt"

    def test_upload_binary_io(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=UPLOAD_RESPONSE_JSON)

        transport = make_transport(handler)
        resource = FilesResource(transport, "b1")
        result = resource.upload(BytesIO(b"data"), filename="test.bin")

        req = requests_log[0]
        assert req.method == "PUT"
        params = dict(req.url.params)
        assert params["filename"] == "test.bin"
        assert isinstance(result, UploadResponse)

    def test_upload_with_progress_reports(self, tmp_path):
        progress_calls: list[tuple] = []

        def handler(request: httpx.Request) -> httpx.Response:
            # Read the streaming content
            _ = request.content
            return httpx.Response(200, json=UPLOAD_RESPONSE_JSON)

        transport = make_transport(handler)
        resource = FilesResource(transport, "b1")

        test_file = tmp_path / "data.bin"
        test_file.write_bytes(b"x" * 1000)

        def on_progress(bytes_sent: int, total: int | None, pct: float | None):
            progress_calls.append((bytes_sent, total, pct))

        resource.upload(test_file, progress=on_progress)

        assert len(progress_calls) >= 1
        last = progress_calls[-1]
        assert last[0] == 1000  # bytes_sent
        assert last[1] == 1000  # total
        assert last[2] == pytest.approx(100.0)  # percentage

    def test_upload_with_upload_token_in_query(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=UPLOAD_RESPONSE_JSON)

        transport = make_transport(handler)
        resource = FilesResource(transport, "b1")
        resource.upload(b"content", filename="test.txt", upload_token="cfu_abc")

        params = dict(requests_log[0].url.params)
        assert params["token"] == "cfu_abc"

    def test_upload_dedup_fields_deserialized(self):
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json=UPLOAD_RESPONSE_DEDUP_JSON)

        transport = make_transport(handler)
        resource = FilesResource(transport, "b1")
        result = resource.upload(b"content", filename="test.txt")

        uploaded = result.uploaded[0]
        assert uploaded.sha256 == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        assert uploaded.deduplicated is True

    def test_upload_bytes_requires_filename(self):
        transport = make_transport(lambda _: httpx.Response(200))
        resource = FilesResource(transport, "b1")

        with pytest.raises(ValueError, match="filename is required"):
            resource.upload(b"data")

    def test_upload_binary_io_requires_filename(self):
        transport = make_transport(lambda _: httpx.Response(200))
        resource = FilesResource(transport, "b1")

        with pytest.raises(ValueError, match="filename is required"):
            resource.upload(BytesIO(b"data"))

    def test_upload_string_path(self, tmp_path):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=UPLOAD_RESPONSE_JSON)

        transport = make_transport(handler)
        resource = FilesResource(transport, "b1")

        test_file = tmp_path / "test.txt"
        test_file.write_text("hello")
        resource.upload(str(test_file))

        params = dict(requests_log[0].url.params)
        assert params["filename"] == "test.txt"
