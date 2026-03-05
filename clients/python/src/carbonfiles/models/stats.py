from __future__ import annotations

from pydantic import BaseModel


class OwnerStats(BaseModel):
    owner: str
    bucket_count: int
    file_count: int
    total_size: int


class StatsResponse(BaseModel):
    total_buckets: int
    total_files: int
    total_size: int
    total_keys: int
    total_downloads: int
    storage_by_owner: list[OwnerStats]


class HealthResponse(BaseModel):
    status: str
    uptime_seconds: int
    db: str
