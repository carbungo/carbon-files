from __future__ import annotations

import urllib.parse
from io import BytesIO
from pathlib import Path
from typing import BinaryIO, Callable

from carbonfiles.models.common import PaginatedResponse
from carbonfiles.models.files import (
    BucketFile,
    DirectoryListingResponse,
    FileTreeResponse,
    UploadResponse,
    VerifyResponse,
)
from carbonfiles.transport import AsyncTransport, SyncTransport


class FilesResource:
    """Collection operations on files within a bucket."""

    def __init__(self, transport: SyncTransport, bucket_id: str):
        self._transport = transport
        self._bucket_id = bucket_id
        self._base = f"/api/buckets/{urllib.parse.quote(bucket_id, safe='')}"

    def __getitem__(self, path: str) -> FileResource:
        return FileResource(self._transport, self._bucket_id, path)

    def list(
        self,
        *,
        limit: int | None = None,
        offset: int | None = None,
        sort: str | None = None,
        order: str | None = None,
    ) -> PaginatedResponse[BucketFile]:
        query: dict[str, str | None] = {}
        if limit is not None:
            query["limit"] = str(limit)
        if offset is not None:
            query["offset"] = str(offset)
        if sort is not None:
            query["sort"] = sort
        if order is not None:
            query["order"] = order
        url = SyncTransport.build_url(f"{self._base}/files", query or None)
        return self._transport.get(url, PaginatedResponse[BucketFile])

    def list_tree(
        self,
        *,
        delimiter: str = "/",
        prefix: str | None = None,
        limit: int | None = None,
        cursor: str | None = None,
    ) -> FileTreeResponse:
        query: dict[str, str | None] = {"delimiter": delimiter}
        if prefix is not None:
            query["prefix"] = prefix
        if limit is not None:
            query["limit"] = str(limit)
        if cursor is not None:
            query["cursor"] = cursor
        url = SyncTransport.build_url(f"{self._base}/files", query)
        return self._transport.get(url, FileTreeResponse)

    def list_directory(
        self,
        path: str | None = None,
        *,
        limit: int | None = None,
        offset: int | None = None,
        sort: str | None = None,
        order: str | None = None,
    ) -> DirectoryListingResponse:
        query: dict[str, str | None] = {}
        if path is not None:
            query["path"] = path
        if limit is not None:
            query["limit"] = str(limit)
        if offset is not None:
            query["offset"] = str(offset)
        if sort is not None:
            query["sort"] = sort
        if order is not None:
            query["order"] = order
        url = SyncTransport.build_url(f"{self._base}/ls", query or None)
        return self._transport.get(url, DirectoryListingResponse)

    def upload(
        self,
        source: str | Path | bytes | BinaryIO,
        *,
        filename: str | None = None,
        progress: Callable[[int, int | None, float | None], None] | None = None,
        upload_token: str | None = None,
    ) -> UploadResponse:
        """Upload a file.

        *source* can be:
        - ``str`` or ``Path``: file path on disk (filename auto-derived if not provided)
        - ``bytes``: raw content (filename required)
        - ``BinaryIO``: file-like object (filename required)
        """
        if isinstance(source, (str, Path)):
            path = Path(source)
            if filename is None:
                filename = path.name
            with open(path, "rb") as f:
                return self._do_upload(f, filename, progress, upload_token)
        elif isinstance(source, bytes):
            if filename is None:
                raise ValueError("filename is required when uploading bytes")
            return self._do_upload(BytesIO(source), filename, progress, upload_token)
        else:
            # BinaryIO
            if filename is None:
                raise ValueError("filename is required when uploading a file object")
            return self._do_upload(source, filename, progress, upload_token)

    def _do_upload(
        self,
        content: BinaryIO,
        filename: str,
        progress: Callable[[int, int | None, float | None], None] | None,
        upload_token: str | None,
    ) -> UploadResponse:
        query: dict[str, str | None] = {"filename": filename}
        if upload_token is not None:
            query["token"] = upload_token
        url = SyncTransport.build_url(f"{self._base}/upload/stream", query)
        return self._transport.put_stream(url, content, UploadResponse, progress=progress)


class FileResource:
    """Operations on a single file within a bucket."""

    def __init__(self, transport: SyncTransport, bucket_id: str, path: str):
        self._transport = transport
        self._bucket_id = bucket_id
        self._path = path
        self._base = (
            f"/api/buckets/{urllib.parse.quote(bucket_id, safe='')}"
            f"/files/{urllib.parse.quote(path, safe='')}"
        )

    def metadata(self) -> BucketFile:
        return self._transport.get(self._base, BucketFile)

    def download(self) -> bytes:
        return self._transport.get_bytes(f"{self._base}/content")

    def download_to(self, dest: str | Path) -> None:
        data = self.download()
        Path(dest).write_bytes(data)

    def delete(self) -> None:
        self._transport.delete(self._base)

    def verify(self) -> VerifyResponse:
        return self._transport.get(f"{self._base}/verify", VerifyResponse)

    def append(self, data: bytes | BinaryIO) -> BucketFile:
        """Append data to file using ``X-Append: true`` header."""
        content = data if isinstance(data, bytes) else data.read()
        response = self._transport.send_raw(
            "PATCH",
            f"{self._base}/content",
            content=content,
            headers={"content-type": "application/octet-stream", "x-append": "true"},
        )
        self._transport._handle_error(response)
        return BucketFile.model_validate(response.json())

    def patch(self, data: bytes | BinaryIO, *, range_start: int, range_end: int, total_size: int) -> BucketFile:
        """Write to a byte range using ``Content-Range`` header."""
        content = data if isinstance(data, bytes) else data.read()
        response = self._transport.send_raw(
            "PATCH",
            f"{self._base}/content",
            content=content,
            headers={
                "content-type": "application/octet-stream",
                "content-range": f"bytes {range_start}-{range_end}/{total_size}",
            },
        )
        self._transport._handle_error(response)
        return BucketFile.model_validate(response.json())


# ---------------------------------------------------------------------------
# Async variants
# ---------------------------------------------------------------------------


class AsyncFilesResource:
    """Async collection operations on files within a bucket."""

    def __init__(self, transport: AsyncTransport, bucket_id: str):
        self._transport = transport
        self._bucket_id = bucket_id
        self._base = f"/api/buckets/{urllib.parse.quote(bucket_id, safe='')}"

    def __getitem__(self, path: str) -> AsyncFileResource:
        return AsyncFileResource(self._transport, self._bucket_id, path)

    async def list(
        self,
        *,
        limit: int | None = None,
        offset: int | None = None,
        sort: str | None = None,
        order: str | None = None,
    ) -> PaginatedResponse[BucketFile]:
        query: dict[str, str | None] = {}
        if limit is not None:
            query["limit"] = str(limit)
        if offset is not None:
            query["offset"] = str(offset)
        if sort is not None:
            query["sort"] = sort
        if order is not None:
            query["order"] = order
        url = AsyncTransport.build_url(f"{self._base}/files", query or None)
        return await self._transport.get(url, PaginatedResponse[BucketFile])

    async def list_tree(
        self,
        *,
        delimiter: str = "/",
        prefix: str | None = None,
        limit: int | None = None,
        cursor: str | None = None,
    ) -> FileTreeResponse:
        query: dict[str, str | None] = {"delimiter": delimiter}
        if prefix is not None:
            query["prefix"] = prefix
        if limit is not None:
            query["limit"] = str(limit)
        if cursor is not None:
            query["cursor"] = cursor
        url = AsyncTransport.build_url(f"{self._base}/files", query)
        return await self._transport.get(url, FileTreeResponse)

    async def list_directory(
        self,
        path: str | None = None,
        *,
        limit: int | None = None,
        offset: int | None = None,
        sort: str | None = None,
        order: str | None = None,
    ) -> DirectoryListingResponse:
        query: dict[str, str | None] = {}
        if path is not None:
            query["path"] = path
        if limit is not None:
            query["limit"] = str(limit)
        if offset is not None:
            query["offset"] = str(offset)
        if sort is not None:
            query["sort"] = sort
        if order is not None:
            query["order"] = order
        url = AsyncTransport.build_url(f"{self._base}/ls", query or None)
        return await self._transport.get(url, DirectoryListingResponse)

    async def upload(
        self,
        source: str | Path | bytes | BinaryIO,
        *,
        filename: str | None = None,
        progress: Callable[[int, int | None, float | None], None] | None = None,
        upload_token: str | None = None,
    ) -> UploadResponse:
        """Upload a file.

        *source* can be:
        - ``str`` or ``Path``: file path on disk (filename auto-derived if not provided)
        - ``bytes``: raw content (filename required)
        - ``BinaryIO``: file-like object (filename required)
        """
        if isinstance(source, (str, Path)):
            p = Path(source)
            if filename is None:
                filename = p.name
            with open(p, "rb") as f:
                return await self._do_upload(f, filename, progress, upload_token)
        elif isinstance(source, bytes):
            if filename is None:
                raise ValueError("filename is required when uploading bytes")
            return await self._do_upload(BytesIO(source), filename, progress, upload_token)
        else:
            if filename is None:
                raise ValueError("filename is required when uploading a file object")
            return await self._do_upload(source, filename, progress, upload_token)

    async def _do_upload(
        self,
        content: BinaryIO,
        filename: str,
        progress: Callable[[int, int | None, float | None], None] | None,
        upload_token: str | None,
    ) -> UploadResponse:
        query: dict[str, str | None] = {"filename": filename}
        if upload_token is not None:
            query["token"] = upload_token
        url = AsyncTransport.build_url(f"{self._base}/upload/stream", query)
        return await self._transport.put_stream(url, content, UploadResponse, progress=progress)


class AsyncFileResource:
    """Async operations on a single file within a bucket."""

    def __init__(self, transport: AsyncTransport, bucket_id: str, path: str):
        self._transport = transport
        self._bucket_id = bucket_id
        self._path = path
        self._base = (
            f"/api/buckets/{urllib.parse.quote(bucket_id, safe='')}"
            f"/files/{urllib.parse.quote(path, safe='')}"
        )

    async def metadata(self) -> BucketFile:
        return await self._transport.get(self._base, BucketFile)

    async def download(self) -> bytes:
        return await self._transport.get_bytes(f"{self._base}/content")

    async def download_to(self, dest: str | Path) -> None:
        data = await self.download()
        Path(dest).write_bytes(data)

    async def delete(self) -> None:
        await self._transport.delete(self._base)

    async def verify(self) -> VerifyResponse:
        return await self._transport.get(f"{self._base}/verify", VerifyResponse)

    async def append(self, data: bytes | BinaryIO) -> BucketFile:
        """Append data to file using ``X-Append: true`` header."""
        content = data if isinstance(data, bytes) else data.read()
        response = await self._transport.send_raw(
            "PATCH",
            f"{self._base}/content",
            content=content,
            headers={"content-type": "application/octet-stream", "x-append": "true"},
        )
        self._transport._handle_error(response)
        return BucketFile.model_validate(response.json())

    async def patch(self, data: bytes | BinaryIO, *, range_start: int, range_end: int, total_size: int) -> BucketFile:
        """Write to a byte range using ``Content-Range`` header."""
        content = data if isinstance(data, bytes) else data.read()
        response = await self._transport.send_raw(
            "PATCH",
            f"{self._base}/content",
            content=content,
            headers={
                "content-type": "application/octet-stream",
                "content-range": f"bytes {range_start}-{range_end}/{total_size}",
            },
        )
        self._transport._handle_error(response)
        return BucketFile.model_validate(response.json())
