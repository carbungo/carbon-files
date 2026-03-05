from __future__ import annotations

from carbonfiles.models.stats import HealthResponse
from carbonfiles.resources.buckets import AsyncBucketsResource
from carbonfiles.resources.dashboard import AsyncDashboardResource
from carbonfiles.resources.keys import AsyncKeysResource
from carbonfiles.resources.short_urls import AsyncShortUrlsResource
from carbonfiles.resources.stats import AsyncStatsResource
from carbonfiles.transport import AsyncTransport


class AsyncCarbonFiles:
    """Asynchronous CarbonFiles API client."""

    def __init__(
        self,
        base_url: str,
        api_key: str | None = None,
        *,
        http_client=None,  # noqa: ANN001
    ):
        self._transport = AsyncTransport(base_url, api_key, http_client=http_client)

    @property
    def buckets(self) -> AsyncBucketsResource:
        return AsyncBucketsResource(self._transport)

    @property
    def keys(self) -> AsyncKeysResource:
        return AsyncKeysResource(self._transport)

    @property
    def stats(self) -> AsyncStatsResource:
        return AsyncStatsResource(self._transport)

    @property
    def short_urls(self) -> AsyncShortUrlsResource:
        return AsyncShortUrlsResource(self._transport)

    @property
    def dashboard(self) -> AsyncDashboardResource:
        return AsyncDashboardResource(self._transport)

    async def health(self) -> HealthResponse:
        return await self._transport.get("/healthz", HealthResponse)

    async def close(self) -> None:
        await self._transport.close()

    async def __aenter__(self) -> AsyncCarbonFiles:
        return self

    async def __aexit__(self, *args) -> None:  # noqa: ANN002
        await self.close()
