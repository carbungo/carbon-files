from __future__ import annotations

import urllib.parse

from carbonfiles.models.tokens import UploadTokenResponse
from carbonfiles.transport import AsyncTransport, SyncTransport


class UploadTokensResource:
    """Upload token operations scoped to a bucket."""

    def __init__(self, transport: SyncTransport, bucket_id: str):
        self._transport = transport
        self._bucket_id = bucket_id
        self._base = f"/api/buckets/{urllib.parse.quote(bucket_id, safe='')}/tokens"

    def create(
        self,
        *,
        expires: str | None = None,
        max_uploads: int | None = None,
    ) -> UploadTokenResponse:
        body: dict = {}
        if expires is not None:
            body["expires_in"] = expires
        if max_uploads is not None:
            body["max_uploads"] = max_uploads
        return self._transport.post(self._base, body, UploadTokenResponse)


# ---------------------------------------------------------------------------
# Async variant
# ---------------------------------------------------------------------------


class AsyncUploadTokensResource:
    """Async upload token operations scoped to a bucket."""

    def __init__(self, transport: AsyncTransport, bucket_id: str):
        self._transport = transport
        self._bucket_id = bucket_id
        self._base = f"/api/buckets/{urllib.parse.quote(bucket_id, safe='')}/tokens"

    async def create(
        self,
        *,
        expires: str | None = None,
        max_uploads: int | None = None,
    ) -> UploadTokenResponse:
        body: dict = {}
        if expires is not None:
            body["expires_in"] = expires
        if max_uploads is not None:
            body["max_uploads"] = max_uploads
        return await self._transport.post(self._base, body, UploadTokenResponse)
