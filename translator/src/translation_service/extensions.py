"""Small host interface for explicitly enabled Python plugins."""
import asyncio
from dataclasses import dataclass
from importlib.metadata import entry_points

from fastapi import FastAPI, Request

from .config import Settings
from .errors import ServiceError
from .service import TranslationService


async def connected(request: Request, operation):
    """Await operation while cancelling it on request disconnect.

    Args:
        request: HTTP request whose body has already been consumed.
        operation: Awaitable owning provider I/O.
    Returns:
        Operation result; cancellation stops pending provider work.
    """
    async def disconnected():
        """Wait until HTTP peer disconnects; no return value."""
        while True:
            if (await request.receive())["type"] == "http.disconnect":
                return

    work = asyncio.create_task(operation)
    peer = asyncio.create_task(disconnected())
    try:
        done, _ = await asyncio.wait({work, peer}, return_when=asyncio.FIRST_COMPLETED)
        if work in done:
            return await work
        raise ServiceError("client_disconnected", 499)
    finally:
        for task in (work, peer):
            if not task.done():
                task.cancel()
        await asyncio.gather(work, peer, return_exceptions=True)


@dataclass(frozen=True)
class PluginHost:
    """Core services available to an optional provider plugin."""

    # Application receiving plugin routes.
    app: FastAPI
    # Shared configuration without provider credentials.
    settings: Settings
    # Stateless input/output pipeline.
    service: TranslationService


def load_plugins(host: PluginHost) -> None:
    """Register configured entry points with host; no return value."""
    installed = entry_points(group="translation_service.plugins")
    for name, options in host.settings.plugins.items():
        matches = [entry for entry in installed if entry.name == name]
        if len(matches) != 1:
            raise RuntimeError("Configured translator plugin is missing or ambiguous: " + name)
        matches[0].load()(host, options)
