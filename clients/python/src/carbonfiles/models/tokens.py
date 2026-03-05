from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel


class UploadTokenResponse(BaseModel):
    token: str
    bucket_id: str
    expires_at: datetime
    max_uploads: int | None = None
    uploads_used: int


class DashboardTokenResponse(BaseModel):
    token: str
    expires_at: datetime


class DashboardTokenInfo(BaseModel):
    scope: str
    expires_at: datetime
