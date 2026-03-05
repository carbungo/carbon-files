from __future__ import annotations

from carbonfiles.models.stats import HealthResponse
from carbonfiles.resources.buckets import BucketsResource
from carbonfiles.resources.dashboard import DashboardResource
from carbonfiles.resources.keys import KeysResource
from carbonfiles.resources.short_urls import ShortUrlsResource
from carbonfiles.resources.stats import StatsResource
from carbonfiles.transport import SyncTransport


class CarbonFiles:
    """Synchronous CarbonFiles API client."""

    def __init__(
        self,
        base_url: str,
        api_key: str | None = None,
        *,
        http_client=None,  # noqa: ANN001
    ):
        self._transport = SyncTransport(base_url, api_key, http_client=http_client)

    @property
    def buckets(self) -> BucketsResource:
        return BucketsResource(self._transport)

    @property
    def keys(self) -> KeysResource:
        return KeysResource(self._transport)

    @property
    def stats(self) -> StatsResource:
        return StatsResource(self._transport)

    @property
    def short_urls(self) -> ShortUrlsResource:
        return ShortUrlsResource(self._transport)

    @property
    def dashboard(self) -> DashboardResource:
        return DashboardResource(self._transport)

    def health(self) -> HealthResponse:
        return self._transport.get("/healthz", HealthResponse)

    def close(self) -> None:
        self._transport.close()

    def __enter__(self) -> CarbonFiles:
        return self

    def __exit__(self, *args) -> None:  # noqa: ANN002
        self.close()
