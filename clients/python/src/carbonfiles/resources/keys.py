from __future__ import annotations

import urllib.parse

from carbonfiles.models.common import PaginatedResponse
from carbonfiles.models.keys import ApiKeyListItem, ApiKeyResponse, ApiKeyUsageResponse
from carbonfiles.transport import AsyncTransport, SyncTransport


class KeysResource:
    """Collection operations on API keys."""

    def __init__(self, transport: SyncTransport):
        self._transport = transport

    def __getitem__(self, prefix: str) -> KeyResource:
        return KeyResource(self._transport, prefix)

    def create(self, name: str) -> ApiKeyResponse:
        return self._transport.post("/api/keys", {"name": name}, ApiKeyResponse)

    def list(
        self,
        *,
        limit: int | None = None,
        offset: int | None = None,
        sort: str | None = None,
        order: str | None = None,
    ) -> PaginatedResponse[ApiKeyListItem]:
        query: dict[str, str | None] = {}
        if limit is not None:
            query["limit"] = str(limit)
        if offset is not None:
            query["offset"] = str(offset)
        if sort is not None:
            query["sort"] = sort
        if order is not None:
            query["order"] = order
        url = SyncTransport.build_url("/api/keys", query or None)
        return self._transport.get(url, PaginatedResponse[ApiKeyListItem])


class KeyResource:
    """Operations on a single API key."""

    def __init__(self, transport: SyncTransport, prefix: str):
        self._transport = transport
        self._prefix = prefix
        self._base = f"/api/keys/{urllib.parse.quote(prefix, safe='')}"

    def delete(self) -> None:
        self._transport.delete(self._base)

    def usage(self) -> ApiKeyUsageResponse:
        return self._transport.get(f"{self._base}/usage", ApiKeyUsageResponse)


# ---------------------------------------------------------------------------
# Async variants
# ---------------------------------------------------------------------------


class AsyncKeysResource:
    """Async collection operations on API keys."""

    def __init__(self, transport: AsyncTransport):
        self._transport = transport

    def __getitem__(self, prefix: str) -> AsyncKeyResource:
        return AsyncKeyResource(self._transport, prefix)

    async def create(self, name: str) -> ApiKeyResponse:
        return await self._transport.post("/api/keys", {"name": name}, ApiKeyResponse)

    async def list(
        self,
        *,
        limit: int | None = None,
        offset: int | None = None,
        sort: str | None = None,
        order: str | None = None,
    ) -> PaginatedResponse[ApiKeyListItem]:
        query: dict[str, str | None] = {}
        if limit is not None:
            query["limit"] = str(limit)
        if offset is not None:
            query["offset"] = str(offset)
        if sort is not None:
            query["sort"] = sort
        if order is not None:
            query["order"] = order
        url = AsyncTransport.build_url("/api/keys", query or None)
        return await self._transport.get(url, PaginatedResponse[ApiKeyListItem])


class AsyncKeyResource:
    """Async operations on a single API key."""

    def __init__(self, transport: AsyncTransport, prefix: str):
        self._transport = transport
        self._prefix = prefix
        self._base = f"/api/keys/{urllib.parse.quote(prefix, safe='')}"

    async def delete(self) -> None:
        await self._transport.delete(self._base)

    async def usage(self) -> ApiKeyUsageResponse:
        return await self._transport.get(f"{self._base}/usage", ApiKeyUsageResponse)
