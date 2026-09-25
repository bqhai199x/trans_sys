"""Optional translator-codex-cli entry point."""
from translation_service.extensions import PluginHost

from .config import GatewaySettings
from .routes import routes
from .store import GatewayStore


def register(host: PluginHost, options: dict) -> None:
    """Register gateway/provider routes with host using options; no return value."""
    settings = GatewaySettings.model_validate(options)
    host.app.include_router(routes(host, settings, GatewayStore(settings)))
