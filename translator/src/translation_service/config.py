"""Processing limits and plugins; provider keys belong to requests."""
import os
from pathlib import Path

from pydantic import Field

from .schemas import StrictModel


class Settings(StrictModel):
    """Configuration shared by API instances without translation state."""

    # Optional known model limits, keyed by provider:model.
    model_capabilities: dict[str, dict] = Field(default_factory=dict)
    # Enabled entry-point names and their plugin-owned configuration.
    plugins: dict[str, dict] = Field(default_factory=dict)
    # Maximum HTTP body size in bytes, including multipart framing.
    max_request_bytes: int = Field(default=8_000_000, gt=0)
    # Maximum number of input units.
    max_units: int = Field(default=10000, gt=0)
    # Maximum source length in Unicode code points.
    max_unit_chars: int = Field(default=200000, gt=0)
    # Maximum targets per provider turn.
    max_batch_units: int = Field(default=40, ge=1, le=40)
    # Maximum discovery or individual turn duration in seconds.
    provider_timeout_seconds: float = Field(default=600.0, gt=0)
    # Reserved reasoning capacity in tokens per turn.
    reasoning_budget_tokens: int = Field(default=4096, ge=0)

    @classmethod
    def from_env(cls) -> "Settings":
        """Return TRANSLATOR_CONFIG contents or default limits."""
        config = os.environ.get("TRANSLATOR_CONFIG")
        return cls.model_validate_json(Path(config).read_text(encoding="utf-8")) if config else cls()
