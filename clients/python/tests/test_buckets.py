from __future__ import annotations

import json

import httpx

from carbonfiles.models.buckets import Bucket, BucketDetail
from carbonfiles.models.common import PaginatedResponse
from carbonfiles.resources.buckets import BucketResource, BucketsResource
from carbonfiles.transport import SyncTransport

BUCKET_JSON = {
    "id": "b1",
    "name": "test",
    "owner": "cf4_key",
    "created_at": "2026-01-01T00:00:00Z",
    "file_count": 0,
    "total_size": 0,
}

BUCKET_DETAIL_JSON = {
    **BUCKET_JSON,
    "unique_content_count": 0,
    "unique_content_size": 0,
    "has_more_files": False,
}


def make_transport(handler, api_key="test-key"):
    mock = httpx.MockTransport(handler)
    client = httpx.Client(transport=mock, base_url="https://example.com")
    return SyncTransport("https://example.com", api_key, http_client=client)


# ---------------------------------------------------------------------------
# BucketsResource tests
# ---------------------------------------------------------------------------


class TestBucketsResource:
    def test_create_sends_post(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(201, json=BUCKET_JSON)

        transport = make_transport(handler)
        resource = BucketsResource(transport)
        result = resource.create("test")

        assert len(requests_log) == 1
        assert requests_log[0].method == "POST"
        assert requests_log[0].url.path == "/api/buckets"
        body = json.loads(requests_log[0].content)
        assert body == {"name": "test"}

        assert isinstance(result, Bucket)
        assert result.id == "b1"
        assert result.name == "test"

    def test_create_with_all_options(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(201, json=BUCKET_JSON)

        transport = make_transport(handler)
        resource = BucketsResource(transport)
        resource.create("test", description="A bucket", expires="7d")

        body = json.loads(requests_log[0].content)
        assert body == {"name": "test", "description": "A bucket", "expires_in": "7d"}

    def test_list_with_pagination(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(
                200,
                json={"items": [BUCKET_JSON], "total": 1, "limit": 10, "offset": 0},
            )

        transport = make_transport(handler)
        resource = BucketsResource(transport)
        result = resource.list(limit=10, offset=0, sort="name", order="asc")

        req = requests_log[0]
        assert req.method == "GET"
        assert req.url.path == "/api/buckets"
        params = dict(req.url.params)
        assert params["limit"] == "10"
        assert params["offset"] == "0"
        assert params["sort"] == "name"
        assert params["order"] == "asc"

        assert result.total == 1
        assert len(result.items) == 1

    def test_list_default_params(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(
                200,
                json={"items": [], "total": 0, "limit": 50, "offset": 0},
            )

        transport = make_transport(handler)
        resource = BucketsResource(transport)
        resource.list()

        req = requests_log[0]
        assert req.url.path == "/api/buckets"
        # No query params when none provided
        assert str(req.url.params) == ""

    def test_list_all_auto_paginates(self):
        call_count = 0

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal call_count
            call_count += 1
            if call_count == 1:
                return httpx.Response(
                    200,
                    json={
                        "items": [BUCKET_JSON, BUCKET_JSON],
                        "total": 3,
                        "limit": 2,
                        "offset": 0,
                    },
                )
            else:
                return httpx.Response(
                    200,
                    json={
                        "items": [BUCKET_JSON],
                        "total": 3,
                        "limit": 2,
                        "offset": 2,
                    },
                )

        transport = make_transport(handler)
        resource = BucketsResource(transport)
        pages = list(resource.list_all(limit=2))

        assert len(pages) == 2
        assert len(pages[0].items) == 2
        assert len(pages[1].items) == 1
        assert call_count == 2

    def test_list_include_expired(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(
                200,
                json={"items": [], "total": 0, "limit": 50, "offset": 0},
            )

        transport = make_transport(handler)
        resource = BucketsResource(transport)
        resource.list(include_expired=True)

        params = dict(requests_log[0].url.params)
        assert params["include_expired"] == "true"


# ---------------------------------------------------------------------------
# BucketResource tests
# ---------------------------------------------------------------------------


class TestBucketResource:
    def test_get_returns_detail(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=BUCKET_DETAIL_JSON)

        transport = make_transport(handler)
        resource = BucketResource(transport, "b1")
        result = resource.get()

        assert requests_log[0].method == "GET"
        assert requests_log[0].url.path == "/api/buckets/b1"
        assert isinstance(result, BucketDetail)
        assert result.id == "b1"
        assert result.unique_content_count == 0

    def test_get_with_include_files(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=BUCKET_DETAIL_JSON)

        transport = make_transport(handler)
        resource = BucketResource(transport, "b1")
        resource.get(include_files=True)

        params = dict(requests_log[0].url.params)
        assert params["include"] == "files"

    def test_update_sends_patch(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=BUCKET_JSON)

        transport = make_transport(handler)
        resource = BucketResource(transport, "b1")
        result = resource.update(name="renamed", description="new desc")

        assert requests_log[0].method == "PATCH"
        assert requests_log[0].url.path == "/api/buckets/b1"
        body = json.loads(requests_log[0].content)
        assert body == {"name": "renamed", "description": "new desc"}
        assert isinstance(result, Bucket)

    def test_delete_sends_delete(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(204)

        transport = make_transport(handler)
        resource = BucketResource(transport, "b1")
        resource.delete()

        assert requests_log[0].method == "DELETE"
        assert requests_log[0].url.path == "/api/buckets/b1"

    def test_summary_returns_string(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, text="Bucket summary text")

        transport = make_transport(handler)
        resource = BucketResource(transport, "b1")
        result = resource.summary()

        assert requests_log[0].method == "GET"
        assert requests_log[0].url.path == "/api/buckets/b1/summary"
        assert result == "Bucket summary text"

    def test_download_zip_returns_bytes(self):
        requests_log: list[httpx.Request] = []
        zip_content = b"\x50\x4b\x03\x04fake-zip-data"

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, content=zip_content)

        transport = make_transport(handler)
        resource = BucketResource(transport, "b1")
        result = resource.download_zip()

        assert requests_log[0].method == "GET"
        assert requests_log[0].url.path == "/api/buckets/b1/zip"
        assert result == zip_content

    def test_special_chars_in_bucket_id_escaped(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=BUCKET_DETAIL_JSON)

        transport = make_transport(handler)
        resource = BucketResource(transport, "my bucket/id")
        resource.get()

        # The bucket ID should be URL-encoded in the path
        # httpx's .path decodes percent-encoding, so check raw_path instead
        assert requests_log[0].url.raw_path == b"/api/buckets/my%20bucket%2Fid"
