from __future__ import annotations

from carbonfiles.models.tokens import DashboardTokenInfo, DashboardTokenResponse
from carbonfiles.transport import AsyncTransport, SyncTransport


class DashboardResource:
    """Dashboard token operations (admin only)."""

    def __init__(self, transport: SyncTransport):
        self._transport = transport

    def create_token(self, *, expires: str | None = None) -> DashboardTokenResponse:
        body: dict = {}
        if expires is not None:
            body["expires_in"] = expires
        return self._transport.post("/api/tokens/dashboard", body, DashboardTokenResponse)

    def current_user(self) -> DashboardTokenInfo:
        return self._transport.get("/api/tokens/dashboard/me", DashboardTokenInfo)


# ---------------------------------------------------------------------------
# Async variant
# ---------------------------------------------------------------------------


class AsyncDashboardResource:
    """Async dashboard token operations (admin only)."""

    def __init__(self, transport: AsyncTransport):
        self._transport = transport

    async def create_token(self, *, expires: str | None = None) -> DashboardTokenResponse:
        body: dict = {}
        if expires is not None:
            body["expires_in"] = expires
        return await self._transport.post("/api/tokens/dashboard", body, DashboardTokenResponse)

    async def current_user(self) -> DashboardTokenInfo:
        return await self._transport.get("/api/tokens/dashboard/me", DashboardTokenInfo)
