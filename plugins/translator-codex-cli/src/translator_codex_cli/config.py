"""Plugin-owned gateway configuration."""
from pydantic import Field
from translation_service.schemas import StrictModel


class GatewaySettings(StrictModel):
    """Gateway registration and transport limits."""

    # Separate SQLite database; legacy translator databases are not accepted.
    database: str = "translator-codex-cli.sqlite"
    # Public HTTPS origin embedded into enrollment downloads.
    public_url: str = "https://localhost"
    # Optional installer ZIP built from gateway source.
    gateway_bundle: str | None = None
    # Maximum duration of one transport delivery in seconds.
    turn_timeout_seconds: float = Field(default=600.0, gt=0)
    # Lifetime of request-owner lease in seconds.
    request_lease_seconds: float = Field(default=60.0, gt=0)
    # Heartbeat age after which gateway is offline, in seconds.
    offline_seconds: float = Field(default=45.0, gt=0)
    # Maximum catalog age in seconds.
    model_cache_seconds: float = Field(default=600.0, gt=0)
    # Accepted/discarded delivery receipt retention in seconds.
    receipt_retention_seconds: float = Field(default=86400.0, gt=0)
