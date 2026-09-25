"""Shared input, prompt, validation, and direct translation pipeline."""
import asyncio

from .config import Settings
from .domain import Capability, parse_response, plan, prepare, prompt_for
from .errors import ServiceError
from .providers import Provider, ProviderContext, Turn
from .schemas import FallbackResult, ModelInfo, ModelRequest, TranslationInput, TranslationResult
from .tokens import TokenError, validate


def representatives(units: list[dict]) -> list[int]:
    """Return non-skipped representative indices from prepared units."""
    return [u["index"] for u in units if u["status"] != "skipped" and u["index"] == u["representative"]]


def apply_response(units: list[dict], indices: list[int], texts: list[str]) -> None:
    """Validate texts against indices and fan out by representative; no return value."""
    groups = {}
    for unit in units:
        if unit["status"] != "skipped":
            groups.setdefault(unit["representative"], []).append(unit)
    for index, text in zip(indices, texts):
        for unit in groups[index]:
            try:
                validate(unit["source"], text)
                unit.update(status="translated", result=text, diagnostics=None)
            except TokenError as exc:
                unit.update(status="fallback", diagnostics=str(exc))


def result_for(units: list[dict]) -> TranslationResult:
    """Return export-ready result with source preserved at skipped or failed units."""
    return TranslationResult(
        status="partial" if any(u["status"] == "fallback" for u in units) else "success",
        texts=[u["result"] if u["status"] == "translated" else u["source"] for u in units],
        fallbacks=[FallbackResult(source_index=u["index"], input=u["source"], output=u["source"],
                                  diagnostics=u["diagnostics"]) for u in units if u["status"] == "fallback"],
    )


class TranslationService:
    """Request-local execution using shared, stateless domain functions."""

    def __init__(self, settings: Settings):
        """Initialize deployment limits from settings."""
        # Immutable-by-convention deployment settings.
        self.settings = settings

    def prepare(self, body: TranslationInput) -> list[dict]:
        """Return prepared body units after enforcing deployment limits."""
        if len(body.texts) > self.settings.max_units or any(len(t) > self.settings.max_unit_chars for t in body.texts):
            raise ServiceError("request_limits", 413)
        return prepare(body.content())

    def genprompt(self, body: TranslationInput) -> str:
        """Return one prompt for all representative targets in body."""
        units = self.prepare(body)
        return prompt_for(body.content(), units, representatives(units))

    def validate(self, body: TranslationInput, raw: str, allow_fence: bool = False) -> TranslationResult:
        """Return validated raw output mapped to body source positions.

        Args:
            body: Original content used to generate prompt.
            raw: Uploaded or pasted model response.
            allow_fence: Whether pasted output may use one code fence.
        Returns:
            Export-ready result, or raises ServiceError for invalid schema.
        """
        units = self.prepare(body)
        indices = representatives(units)
        try:
            texts = parse_response(raw, len(indices), allow_fence)
        except ValueError as exc:
            raise ServiceError(str(exc), 422) from None
        apply_response(units, indices, texts)
        return result_for(units)

    async def models(self, provider: Provider, context: ProviderContext, name: str) -> list[ModelInfo]:
        """Return provider catalog for context with configured capability overrides."""
        try:
            async with asyncio.timeout(self.settings.provider_timeout_seconds):
                discovered = await provider.discover(context)
        except TimeoutError:
            raise ServiceError("provider_timeout", 504) from None
        result = []
        for item in discovered:
            model = item if isinstance(item, ModelInfo) else ModelInfo.model_validate(item)
            overrides = self.settings.model_capabilities.get(name + ":" + model.id, {})
            if set(overrides) - {"context_tokens", "output_tokens", "session", "efforts"}:
                raise ServiceError("model_capability_config", 500)
            result.append(ModelInfo.model_validate({**model.model_dump(), **overrides}))
        return result

    async def translate(self, body: ModelRequest, provider: Provider, context: ProviderContext, name: str) -> TranslationResult:
        """Return direct translation while keeping all turns inside this request.

        Args:
            body: Translation input and provider options without persisted state.
            provider: Adapter responsible for one call per turn.
            context: Request-owned credential and routing.
            name: Registered provider name for capability overrides.
        Returns:
            Ordered result, or raises ServiceError on fatal provider/output failure.
        """
        units = self.prepare(body)
        indices = representatives(units)
        if not indices:
            return result_for(units)
        models = await self.models(provider, context, name)
        model = next((m for m in models if m.id == body.model), None)
        if model is None or body.reasoning_effort is not None and body.reasoning_effort not in model.efforts:
            raise ServiceError("model_or_effort_unsupported", 422)
        if body.mode == "sync" and not (model.session and model.context_tokens and model.output_tokens):
            raise ServiceError("sync_budget_unavailable", 422)
        capability = Capability(model.context_tokens, model.output_tokens, self.settings.reasoning_budget_tokens)
        content = body.content()
        session, history = None, 0
        while indices:
            await asyncio.sleep(0)
            selected, oversized, prompt = plan(content, units, indices, capability, history, self.settings.max_batch_units)
            if oversized and body.mode == "sync" and history:
                raise ServiceError("session_budget_exhausted", 502)
            for unit in units:
                if unit["representative"] in oversized:
                    unit.update(status="fallback", diagnostics="unit_too_large")
            consumed = set(selected + oversized)
            indices = [i for i in indices if i not in consumed]
            if not selected:
                continue
            turn = Turn(prompt, body.model, body.mode, session, body.reasoning_effort)
            try:
                async with asyncio.timeout(self.settings.provider_timeout_seconds):
                    response = await provider.execute(context, turn)
            except TimeoutError:
                raise ServiceError("provider_timeout", 504) from None
            if response.completion != "completed":
                raise ServiceError("provider_completion", 502)
            if body.mode == "sync":
                if not response.session:
                    raise ServiceError("provider_session_missing", 502)
                session = response.session
            try:
                texts = parse_response(response.raw, len(selected))
            except ValueError as exc:
                raise ServiceError(str(exc), 502) from None
            apply_response(units, selected, texts)
            if body.mode == "sync":
                history += len(prompt.encode("utf-8")) + len(response.raw.encode("utf-8"))
        return result_for(units)
