from __future__ import annotations

import urllib.parse
from collections.abc import AsyncGenerator, Generator

from carbonfiles.models.buckets import Bucket, BucketDetail
from carbonfiles.models.common import PaginatedResponse
from carbonfiles.transport import AsyncTransport, SyncTransport


class BucketsResource:
    """Collection operations on buckets (list, create)."""

    def __init__(self, transport: SyncTransport):
        self._transport = transport

    def __getitem__(self, bucket_id: str) -> BucketResource:
        return BucketResource(self._transport, bucket_id)

    def create(
        self,
        name: str,
        *,
        description: str | None = None,
        expires: str | None = None,
    ) -> Bucket:
        body: dict = {"name": name}
        if description is not None:
            body["description"] = description
        if expires is not None:
            body["expires_in"] = expires
        return self._transport.post("/api/buckets", body, Bucket)

    def list(
        self,
        *,
        limit: int | None = None,
        offset: int | None = None,
        sort: str | None = None,
        order: str | None = None,
        include_expired: bool | None = None,
    ) -> PaginatedResponse[Bucket]:
        query: dict[str, str | None] = {}
        if limit is not None:
            query["limit"] = str(limit)
        if offset is not None:
            query["offset"] = str(offset)
        if sort is not None:
            query["sort"] = sort
        if order is not None:
            query["order"] = order
        if include_expired is not None:
            query["include_expired"] = str(include_expired).lower()
        url = SyncTransport.build_url("/api/buckets", query or None)
        return self._transport.get(url, PaginatedResponse[Bucket])

    def list_all(self, *, limit: int = 50) -> Generator[PaginatedResponse[Bucket], None, None]:
        offset = 0
        while True:
            page = self.list(limit=limit, offset=offset)
            yield page
            if offset + limit >= page.total:
                break
            offset += limit


class BucketResource:
    """Operations on a single bucket."""

    def __init__(self, transport: SyncTransport, bucket_id: str):
        self._transport = transport
        self._bucket_id = bucket_id
        self._base = f"/api/buckets/{urllib.parse.quote(bucket_id, safe='')}"

    @property
    def files(self):  # noqa: ANN201
        from carbonfiles.resources.files import FilesResource

        return FilesResource(self._transport, self._bucket_id)

    @property
    def tokens(self):  # noqa: ANN201
        from carbonfiles.resources.tokens import UploadTokensResource

        return UploadTokensResource(self._transport, self._bucket_id)

    def get(self, *, include_files: bool = False) -> BucketDetail:
        query = {"include": "files"} if include_files else None
        url = SyncTransport.build_url(self._base, query)
        return self._transport.get(url, BucketDetail)

    def update(
        self,
        *,
        name: str | None = None,
        description: str | None = None,
        expires: str | None = None,
    ) -> Bucket:
        body: dict = {}
        if name is not None:
            body["name"] = name
        if description is not None:
            body["description"] = description
        if expires is not None:
            body["expires_in"] = expires
        return self._transport.patch(self._base, body, Bucket)

    def delete(self) -> None:
        self._transport.delete(self._base)

    def summary(self) -> str:
        return self._transport.get_string(f"{self._base}/summary")

    def download_zip(self) -> bytes:
        return self._transport.get_bytes(f"{self._base}/zip")


# ---------------------------------------------------------------------------
# Async variants
# ---------------------------------------------------------------------------


class AsyncBucketsResource:
    """Async collection operations on buckets (list, create)."""

    def __init__(self, transport: AsyncTransport):
        self._transport = transport

    def __getitem__(self, bucket_id: str) -> AsyncBucketResource:
        return AsyncBucketResource(self._transport, bucket_id)

    async def create(
        self,
        name: str,
        *,
        description: str | None = None,
        expires: str | None = None,
    ) -> Bucket:
        body: dict = {"name": name}
        if description is not None:
            body["description"] = description
        if expires is not None:
            body["expires_in"] = expires
        return await self._transport.post("/api/buckets", body, Bucket)

    async def list(
        self,
        *,
        limit: int | None = None,
        offset: int | None = None,
        sort: str | None = None,
        order: str | None = None,
        include_expired: bool | None = None,
    ) -> PaginatedResponse[Bucket]:
        query: dict[str, str | None] = {}
        if limit is not None:
            query["limit"] = str(limit)
        if offset is not None:
            query["offset"] = str(offset)
        if sort is not None:
            query["sort"] = sort
        if order is not None:
            query["order"] = order
        if include_expired is not None:
            query["include_expired"] = str(include_expired).lower()
        url = AsyncTransport.build_url("/api/buckets", query or None)
        return await self._transport.get(url, PaginatedResponse[Bucket])

    async def list_all(self, *, limit: int = 50) -> AsyncGenerator[PaginatedResponse[Bucket], None]:
        offset = 0
        while True:
            page = await self.list(limit=limit, offset=offset)
            yield page
            if offset + limit >= page.total:
                break
            offset += limit


class AsyncBucketResource:
    """Async operations on a single bucket."""

    def __init__(self, transport: AsyncTransport, bucket_id: str):
        self._transport = transport
        self._bucket_id = bucket_id
        self._base = f"/api/buckets/{urllib.parse.quote(bucket_id, safe='')}"

    @property
    def files(self):  # noqa: ANN201
        from carbonfiles.resources.files import AsyncFilesResource

        return AsyncFilesResource(self._transport, self._bucket_id)

    @property
    def tokens(self):  # noqa: ANN201
        from carbonfiles.resources.tokens import AsyncUploadTokensResource

        return AsyncUploadTokensResource(self._transport, self._bucket_id)

    async def get(self, *, include_files: bool = False) -> BucketDetail:
        query = {"include": "files"} if include_files else None
        url = AsyncTransport.build_url(self._base, query)
        return await self._transport.get(url, BucketDetail)

    async def update(
        self,
        *,
        name: str | None = None,
        description: str | None = None,
        expires: str | None = None,
    ) -> Bucket:
        body: dict = {}
        if name is not None:
            body["name"] = name
        if description is not None:
            body["description"] = description
        if expires is not None:
            body["expires_in"] = expires
        return await self._transport.patch(self._base, body, Bucket)

    async def delete(self) -> None:
        await self._transport.delete(self._base)

    async def summary(self) -> str:
        return await self._transport.get_string(f"{self._base}/summary")

    async def download_zip(self) -> bytes:
        return await self._transport.get_bytes(f"{self._base}/zip")
