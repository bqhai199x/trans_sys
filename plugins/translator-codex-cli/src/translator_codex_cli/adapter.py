"""Await one reserved gateway delivery within caller's HTTP request."""
import asyncio

from translation_service.providers import ProviderContext, Turn, TurnResult
from translation_service.schemas import ModelInfo

from .store import GatewayStore


class CodexAdapter:
    """Provider adapter with transport ownership and no translation scheduling."""

    def __init__(self, store: GatewayStore):
        """Initialize gateway store used for reservations and receipts."""
        # Plugin-owned transport persistence.
        self.store = store

    async def discover(self, context: ProviderContext) -> list[ModelInfo]:
        """Return available models for context device."""
        return [ModelInfo.model_validate(m) for m in self.store.models(context.client_instance)]

    async def execute(self, context: ProviderContext, turn: Turn) -> TurnResult:
        """Return one gateway result for turn; discard delivery on cancellation."""
        identifier = self.store.reserve(context.client_instance, turn.payload())
        accepted = False
        try:
            while True:
                response = self.store.poll(identifier)
                if response is not None:
                    accepted = True
                    return TurnResult(raw=response["raw"], completion=response["completion"],
                                      session=response.get("session"))
                await asyncio.sleep(0.1)
        finally:
            self.store.finish(identifier, accepted)
