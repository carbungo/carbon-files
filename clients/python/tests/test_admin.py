from __future__ import annotations

import json

import httpx

from carbonfiles.models.keys import ApiKeyListItem, ApiKeyResponse, ApiKeyUsageResponse
from carbonfiles.models.stats import StatsResponse
from carbonfiles.models.tokens import (
    DashboardTokenInfo,
    DashboardTokenResponse,
    UploadTokenResponse,
)
from carbonfiles.resources.dashboard import DashboardResource
from carbonfiles.resources.keys import KeyResource, KeysResource
from carbonfiles.resources.short_urls import ShortUrlsResource
from carbonfiles.resources.stats import StatsResource
from carbonfiles.resources.tokens import UploadTokensResource
from carbonfiles.transport import SyncTransport

KEY_JSON = {
    "key": "cf4_abc123secret",
    "prefix": "cf4_abc",
    "name": "test-key",
    "created_at": "2026-01-01T00:00:00Z",
}

KEY_LIST_ITEM_JSON = {
    "prefix": "cf4_abc",
    "name": "test-key",
    "created_at": "2026-01-01T00:00:00Z",
    "last_used_at": None,
    "bucket_count": 2,
    "file_count": 5,
    "total_size": 1024,
}

KEY_USAGE_JSON = {
    **KEY_LIST_ITEM_JSON,
    "total_downloads": 10,
    "buckets": [],
}

STATS_JSON = {
    "total_buckets": 5,
    "total_files": 100,
    "total_size": 50000,
    "total_keys": 3,
    "total_downloads": 200,
    "storage_by_owner": [
        {"owner": "cf4_abc", "bucket_count": 2, "file_count": 50, "total_size": 25000},
    ],
}

DASHBOARD_TOKEN_JSON = {
    "token": "eyJ...",
    "expires_at": "2026-01-02T00:00:00Z",
}

DASHBOARD_INFO_JSON = {
    "scope": "admin",
    "expires_at": "2026-01-02T00:00:00Z",
}

UPLOAD_TOKEN_JSON = {
    "token": "cfu_abc123",
    "bucket_id": "b1",
    "expires_at": "2026-01-02T00:00:00Z",
    "max_uploads": None,
    "uploads_used": 0,
}


def make_transport(handler, api_key="test-key"):
    mock = httpx.MockTransport(handler)
    client = httpx.Client(transport=mock, base_url="https://example.com")
    return SyncTransport("https://example.com", api_key, http_client=client)


# ---------------------------------------------------------------------------
# KeysResource tests
# ---------------------------------------------------------------------------


class TestKeysResource:
    def test_create_sends_post(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(201, json=KEY_JSON)

        transport = make_transport(handler)
        resource = KeysResource(transport)
        result = resource.create("test-key")

        assert len(requests_log) == 1
        assert requests_log[0].method == "POST"
        assert requests_log[0].url.path == "/api/keys"
        body = json.loads(requests_log[0].content)
        assert body == {"name": "test-key"}

        assert isinstance(result, ApiKeyResponse)
        assert result.prefix == "cf4_abc"
        assert result.name == "test-key"

    def test_list_with_pagination(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(
                200,
                json={
                    "items": [KEY_LIST_ITEM_JSON],
                    "total": 1,
                    "limit": 10,
                    "offset": 0,
                },
            )

        transport = make_transport(handler)
        resource = KeysResource(transport)
        result = resource.list(limit=10)

        req = requests_log[0]
        assert req.method == "GET"
        assert req.url.path == "/api/keys"
        params = dict(req.url.params)
        assert params["limit"] == "10"

        assert result.total == 1
        assert len(result.items) == 1


# ---------------------------------------------------------------------------
# KeyResource tests
# ---------------------------------------------------------------------------


class TestKeyResource:
    def test_delete_sends_delete(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(204)

        transport = make_transport(handler)
        resource = KeyResource(transport, "cf4_abc")
        resource.delete()

        assert requests_log[0].method == "DELETE"
        assert requests_log[0].url.path == "/api/keys/cf4_abc"

    def test_usage_returns_response(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=KEY_USAGE_JSON)

        transport = make_transport(handler)
        resource = KeyResource(transport, "cf4_abc")
        result = resource.usage()

        assert requests_log[0].method == "GET"
        assert requests_log[0].url.path == "/api/keys/cf4_abc/usage"
        assert isinstance(result, ApiKeyUsageResponse)
        assert result.total_downloads == 10


# ---------------------------------------------------------------------------
# StatsResource tests
# ---------------------------------------------------------------------------


class TestStatsResource:
    def test_get_returns_stats(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=STATS_JSON)

        transport = make_transport(handler)
        resource = StatsResource(transport)
        result = resource.get()

        assert requests_log[0].method == "GET"
        assert requests_log[0].url.path == "/api/stats"
        assert isinstance(result, StatsResponse)
        assert result.total_buckets == 5
        assert result.total_files == 100
        assert len(result.storage_by_owner) == 1


# ---------------------------------------------------------------------------
# ShortUrlResource tests
# ---------------------------------------------------------------------------


class TestShortUrlResource:
    def test_delete_sends_delete(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(204)

        transport = make_transport(handler)
        resource = ShortUrlsResource(transport)["abc123"]
        resource.delete()

        assert requests_log[0].method == "DELETE"
        assert requests_log[0].url.path == "/api/short/abc123"


# ---------------------------------------------------------------------------
# DashboardResource tests
# ---------------------------------------------------------------------------


class TestDashboardResource:
    def test_create_token(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=DASHBOARD_TOKEN_JSON)

        transport = make_transport(handler)
        resource = DashboardResource(transport)
        result = resource.create_token()

        assert requests_log[0].method == "POST"
        assert requests_log[0].url.path == "/api/tokens/dashboard"
        body = json.loads(requests_log[0].content)
        assert body == {}
        assert isinstance(result, DashboardTokenResponse)
        assert result.token == "eyJ..."

    def test_create_token_with_expiry(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=DASHBOARD_TOKEN_JSON)

        transport = make_transport(handler)
        resource = DashboardResource(transport)
        resource.create_token(expires="24h")

        body = json.loads(requests_log[0].content)
        assert body == {"expires_in": "24h"}

    def test_current_user(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(200, json=DASHBOARD_INFO_JSON)

        transport = make_transport(handler)
        resource = DashboardResource(transport)
        result = resource.current_user()

        assert requests_log[0].method == "GET"
        assert requests_log[0].url.path == "/api/tokens/dashboard/me"
        assert isinstance(result, DashboardTokenInfo)
        assert result.scope == "admin"


# ---------------------------------------------------------------------------
# UploadTokensResource tests
# ---------------------------------------------------------------------------


class TestUploadTokensResource:
    def test_create_sends_post(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(201, json=UPLOAD_TOKEN_JSON)

        transport = make_transport(handler)
        resource = UploadTokensResource(transport, "b1")
        result = resource.create()

        assert requests_log[0].method == "POST"
        assert requests_log[0].url.path == "/api/buckets/b1/tokens"
        body = json.loads(requests_log[0].content)
        assert body == {}
        assert isinstance(result, UploadTokenResponse)
        assert result.token == "cfu_abc123"

    def test_create_with_options(self):
        requests_log: list[httpx.Request] = []

        def handler(request: httpx.Request) -> httpx.Response:
            requests_log.append(request)
            return httpx.Response(201, json=UPLOAD_TOKEN_JSON)

        transport = make_transport(handler)
        resource = UploadTokensResource(transport, "b1")
        resource.create(expires="1h", max_uploads=10)

        body = json.loads(requests_log[0].content)
        assert body == {"expires_in": "1h", "max_uploads": 10}
