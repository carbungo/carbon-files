from __future__ import annotations

import urllib.parse

from carbonfiles.transport import AsyncTransport, SyncTransport


class ShortUrlsResource:
    """Collection operations on short URLs."""

    def __init__(self, transport: SyncTransport):
        self._transport = transport

    def __getitem__(self, code: str) -> ShortUrlResource:
        return ShortUrlResource(self._transport, code)


class ShortUrlResource:
    """Operations on a single short URL."""

    def __init__(self, transport: SyncTransport, code: str):
        self._transport = transport
        self._code = code

    def delete(self) -> None:
        self._transport.delete(f"/api/short/{urllib.parse.quote(self._code, safe='')}")


# ---------------------------------------------------------------------------
# Async variants
# ---------------------------------------------------------------------------


class AsyncShortUrlsResource:
    """Async collection operations on short URLs."""

    def __init__(self, transport: AsyncTransport):
        self._transport = transport

    def __getitem__(self, code: str) -> AsyncShortUrlResource:
        return AsyncShortUrlResource(self._transport, code)


class AsyncShortUrlResource:
    """Async operations on a single short URL."""

    def __init__(self, transport: AsyncTransport, code: str):
        self._transport = transport
        self._code = code

    async def delete(self) -> None:
        await self._transport.delete(f"/api/short/{urllib.parse.quote(self._code, safe='')}")
