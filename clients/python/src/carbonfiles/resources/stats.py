from __future__ import annotations

from carbonfiles.models.stats import StatsResponse
from carbonfiles.transport import AsyncTransport, SyncTransport


class StatsResource:
    """Server statistics (admin only)."""

    def __init__(self, transport: SyncTransport):
        self._transport = transport

    def get(self) -> StatsResponse:
        return self._transport.get("/api/stats", StatsResponse)


# ---------------------------------------------------------------------------
# Async variant
# ---------------------------------------------------------------------------


class AsyncStatsResource:
    """Async server statistics (admin only)."""

    def __init__(self, transport: AsyncTransport):
        self._transport = transport

    async def get(self) -> StatsResponse:
        return await self._transport.get("/api/stats", StatsResponse)
