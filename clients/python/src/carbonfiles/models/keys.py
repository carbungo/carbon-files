from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel

from carbonfiles.models.buckets import Bucket


class ApiKeyResponse(BaseModel):
    key: str
    prefix: str
    name: str
    created_at: datetime


class ApiKeyListItem(BaseModel):
    prefix: str
    name: str
    created_at: datetime
    last_used_at: datetime | None = None
    bucket_count: int
    file_count: int
    total_size: int


class ApiKeyUsageResponse(ApiKeyListItem):
    total_downloads: int
    buckets: list[Bucket]
