"""Provider interface and request-scoped Gemini I/O without retries."""
import asyncio
from dataclasses import dataclass, field
from email.utils import parsedate_to_datetime
import time
from typing import Protocol

from pydantic import SecretStr

from .errors import ServiceError
from .schemas import MODEL_SCHEMA, ModelInfo


# Text/JSON Interactions models verified on 2026-09-24:
# https://ai.google.dev/gemini-api/docs/interactions#supported-models
GEMINI_TEXT_MODELS = frozenset({
    "gemini-3.8-flash", "gemini-3.7-flash", "gemini-3.6-flash", "gemini-3.5-flash",
    "gemini-3.5-flash-lite", "gemini-3.1-pro-preview", "gemini-3.1-flash-lite",
    "gemini-3-flash-preview", "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.5-flash-lite",
})


@dataclass(frozen=True)
class ProviderContext:
    """Provider credential and device routing confined to one request."""

    # Optional credential; never serialized into prompts.
    credential: SecretStr | None = field(default=None, repr=False)
    # Optional device routing identifier interpreted by plugin.
    client_instance: str | None = None


@dataclass(frozen=True)
class Turn:
    """One provider call with no scheduling or retry state."""

    # Complete credential-free prompt.
    prompt: str
    # Discovered model identifier.
    model: str
    # Session reuse policy.
    mode: str
    # Cursor from previous turn; None starts a fresh session.
    session: str | None = None
    # Optional model-specific reasoning level.
    reasoning_effort: str | None = None

    def payload(self) -> dict:
        """Return transport envelope compatible with gateway protocol."""
        return {"prompt": self.prompt, "schema": MODEL_SCHEMA, "model": self.model,
                "session": self.session, "effort": self.reasoning_effort}


@dataclass
class TurnResult:
    """Raw provider result before shared content validation."""

    # Final model response.
    raw: str = ""
    # Provider terminal state.
    completion: str = "completed"
    # Cursor for continuing a logical session.
    session: str | None = None


class Provider(Protocol):
    """Contract implemented by built-in adapters and optional plugins."""

    async def discover(self, context: ProviderContext) -> list[ModelInfo]:
        """Return models available to context credential and routing."""
        ...

    async def execute(self, context: ProviderContext, turn: Turn) -> TurnResult:
        """Return one turn result for context; propagate cancellation."""
        ...


class ProviderFailure(ServiceError):
    """Sanitized provider transport, authentication, or completion failure."""


def retry_after(value: str | None) -> float:
    """Return bounded delay in seconds from numeric or HTTP-date value."""
    if not value:
        return 0
    try:
        return min(3600, max(0, float(value)))
    except ValueError:
        try:
            return min(3600, max(0, parsedate_to_datetime(value).timestamp() - time.time()))
        except (ValueError, TypeError, OverflowError):
            return 0


def provider_error(exc: Exception) -> ProviderFailure:
    """Return sanitized Gemini failure from SDK exception exc."""
    code = getattr(exc, "status_code", getattr(exc, "code", None))
    response = getattr(exc, "response", None)
    delay = retry_after(response.headers.get("Retry-After")) if response is not None else 0
    if code in (400, 401, 403, 404):
        return ProviderFailure("gemini_auth_or_config", 502)
    return ProviderFailure("gemini_unavailable", 429 if code == 429 else 503, delay)


class GeminiAdapter:
    """Stateless adapter; each call owns and closes its SDK client."""

    def __init__(self, factory=None):
        """Initialize optional SDK factory for transport testing."""
        # Client factory; no provider credentials are retained here.
        self.factory = factory

    def client(self, context: ProviderContext):
        """Return SDK client using only context credential."""
        if context.credential is None:
            raise ProviderFailure("gemini_credential_missing", 422)
        key = context.credential.get_secret_value()
        if self.factory:
            return self.factory(key)
        from google import genai
        return genai.Client(vertexai=False, api_key=key,
                            http_options={"timeout": 600000, "retry_options": {"attempts": 1}})

    async def discover(self, context: ProviderContext) -> list[ModelInfo]:
        """Return available text/JSON models for context, without caching."""
        client = self.client(context)
        try:
            models = []
            pages = await client.aio.models.list()
            async for model in pages:
                identifier = (model.name or "").removeprefix("models/")
                if identifier not in GEMINI_TEXT_MODELS or "generateContent" not in (model.supported_actions or []):
                    continue
                models.append(ModelInfo(id=identifier, context_tokens=model.input_token_limit,
                                        output_tokens=model.output_token_limit, session=True))
            return models
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            raise provider_error(exc) from None
        finally:
            await client.aio.aclose()
            client.close()

    async def execute(self, context: ProviderContext, turn: Turn) -> TurnResult:
        """Return one Interactions response for turn without SDK retries."""
        client = self.client(context)
        try:
            args = {"model": turn.model, "input": turn.prompt, "store": turn.mode == "sync",
                    "response_format": {"type": "text", "mime_type": "application/json", "schema": MODEL_SCHEMA}}
            if turn.session:
                args["previous_interaction_id"] = turn.session
            if turn.reasoning_effort:
                args["generation_config"] = {"thinking_level": turn.reasoning_effort}
            interactions = client.aio.interactions
            # Pinned SDK interprets 1 as an additional Interactions retry.
            interactions._client.max_retries = 0
            response = await interactions.create(**args)
            output = getattr(response, "output_text", None)
            if output is None:
                # Pinned SDK exposes newer model_output steps as extra fields.
                output = "".join(content["text"] for step in (getattr(response, "steps", []) or [])
                                 if isinstance(step, dict) and step.get("type") == "model_output"
                                 for content in step.get("content", [])
                                 if isinstance(content, dict) and content.get("type") == "text"
                                 and isinstance(content.get("text"), str))
            return TurnResult(raw=output, completion=getattr(response, "status", "completed"),
                              session=response.id)
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            raise provider_error(exc) from None
        finally:
            await client.aio.aclose()
            client.close()
