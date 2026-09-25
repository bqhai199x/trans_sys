"""Strict public contracts; credentials never enter translation content."""
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, SecretStr, field_validator

from .tokens import validate


class StrictModel(BaseModel):
    """Reject coercion and unknown fields."""

    # Validation errors must not expose request values.
    model_config = ConfigDict(strict=True, extra="forbid", hide_input_in_errors=True)


class TranslationInput(StrictModel):
    """Content shared by automatic and manual translation."""

    # Source units in document order.
    texts: list[str]
    # Requested translation language.
    target_language: str = Field(min_length=1, max_length=100)
    # Document context supplied to model.
    context: str = ""
    # Additional translation instructions.
    custom_prompt: str = ""

    @field_validator("texts")
    @classmethod
    def valid_texts(cls, texts: list[str]) -> list[str]:
        """Return texts after checking Unicode and token syntax."""
        for text in texts:
            text.encode("utf-8")
            validate(text, text)
        return texts

    @field_validator("target_language", "context", "custom_prompt")
    @classmethod
    def valid_unicode(cls, value: str) -> str:
        """Return value after checking UTF-8 compatibility."""
        value.encode("utf-8")
        return value

    def content(self) -> dict:
        """Return prompt input without provider options or credentials."""
        return {name: getattr(self, name) for name in TranslationInput.model_fields}


class ModelRequest(TranslationInput):
    """Provider options for one direct translation request."""

    # Exact model identifier returned by discovery.
    model: str = Field(min_length=1, max_length=200)
    # Session reuse policy; both modes return results directly.
    mode: Literal["batch", "sync"] = "batch"
    # Optional model-specific reasoning level.
    reasoning_effort: str | None = Field(default=None, max_length=100)


class GeminiCredentials(StrictModel):
    """Gemini key scoped to one HTTP request."""

    # Secret is excluded from serialization and representations.
    api_key: SecretStr = Field(min_length=1, exclude=True, repr=False)

    @field_validator("api_key")
    @classmethod
    def nonempty_key(cls, value: SecretStr) -> SecretStr:
        """Return unmodified key, rejecting blank or invalid Unicode."""
        key = value.get_secret_value()
        if not key.strip():
            raise ValueError("gemini_credential_missing")
        key.encode("utf-8")
        return value


class GeminiRequest(ModelRequest, GeminiCredentials):
    """Translation input with a request-owned Gemini key."""


class ValidateRequest(TranslationInput):
    """Original input and pasted model output."""

    # JSON response, optionally wrapped in one Markdown code fence.
    raw_output: str


class PromptResponse(StrictModel):
    """Prompt ready to paste into ChatGPT."""

    # Complete prompt for all representative targets.
    prompt: str


class FallbackResult(StrictModel):
    """Source unit retained after translation fallback."""

    # Zero-based index in original texts array.
    source_index: int
    # Original source unit.
    input: str
    # Final output retained at this source position.
    output: str
    # Stable reason for retaining source.
    diagnostics: str


class TranslationResult(StrictModel):
    """Validated texts in original document order."""

    # Partial means at least one unit retained source due to an error.
    status: Literal["success", "partial"]
    # Export-ready units including preserved source.
    texts: list[str]
    # Only fallback units, in original source order.
    fallbacks: list[FallbackResult]


class ModelInfo(StrictModel):
    """Discovered model and known execution limits."""

    # Native provider model identifier.
    id: str = Field(min_length=1, max_length=200)
    # Supported reasoning levels.
    efforts: list[str] = Field(default_factory=list)
    # Whether provider can continue a logical session.
    session: bool = False
    # Context capacity in tokens; None means unknown.
    context_tokens: int | None = Field(default=None, gt=0)
    # Output capacity in tokens; None means unknown.
    output_tokens: int | None = Field(default=None, gt=0)


class ModelCatalog(StrictModel):
    """Models available to current request."""

    # Provider-filtered model list.
    models: list[ModelInfo]


class ErrorResponse(StrictModel):
    """Sanitized API diagnostic without request content."""

    # Stable error code.
    detail: str


# Exact response shape shared by prompt generation and provider calls.
MODEL_SCHEMA = {"type": "object", "properties": {"texts": {"type": "array", "items": {"type": "string"}}},
                "required": ["texts"], "additionalProperties": False}
