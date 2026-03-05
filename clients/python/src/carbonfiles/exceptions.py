from __future__ import annotations


class CarbonFilesError(Exception):
    """Raised when the CarbonFiles API returns a non-2xx response."""

    def __init__(self, status_code: int, error: str, hint: str | None = None):
        self.status_code = status_code
        self.error = error
        self.hint = hint
        super().__init__(f"{status_code}: {error}" + (f" ({hint})" if hint else ""))
