from __future__ import annotations

from datetime import datetime
from typing import TYPE_CHECKING

from pydantic import BaseModel

if TYPE_CHECKING:
    from carbonfiles.models.files import BucketFile


class Bucket(BaseModel):
    id: str
    name: str
    owner: str
    description: str | None = None
    created_at: datetime
    expires_at: datetime | None = None
    last_used_at: datetime | None = None
    file_count: int
    total_size: int


class BucketDetail(Bucket):
    unique_content_count: int = 0
    unique_content_size: int = 0
    files: list[BucketFile] | None = None
    has_more_files: bool = False


class SyncDeployResponse(BaseModel):
    kept: int
    deleted: int


def _rebuild_models() -> None:
    from carbonfiles.models.files import BucketFile  # noqa: F401

    BucketDetail.model_rebuild()


_rebuild_models()
