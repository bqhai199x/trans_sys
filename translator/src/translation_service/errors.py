"""Safe HTTP-facing failures without request or provider payloads."""


class ServiceError(Exception):
    """Stable diagnostic code and HTTP status."""

    def __init__(self, code: str, status_code: int = 502, retry_after: float = 0):
        """Initialize code, HTTP status_code, and optional retry_after seconds."""
        super().__init__(code)
        # Public diagnostic code; never raw exception text.
        self.code = code
        # HTTP response status.
        self.status_code = status_code
        # Provider backoff hint for caller; service never retries.
        self.retry_after = retry_after
