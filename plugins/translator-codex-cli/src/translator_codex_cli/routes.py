"""Optional Codex provider and outbound gateway HTTP routes."""
import asyncio
import io
from pathlib import Path
import time
from typing import Literal
import zipfile

from fastapi import APIRouter, Depends, Header, Path as ApiPath, Request, Response
from pydantic import Field

from translation_service.domain import canonical
from translation_service.errors import ServiceError
from translation_service.api import JSON_EXAMPLES, input_openapi, read_input
from translation_service.extensions import PluginHost, connected
from translation_service.providers import ProviderContext
from translation_service.schemas import ErrorResponse, ModelCatalog, ModelInfo, ModelRequest, StrictModel, TranslationResult

from .adapter import CodexAdapter
from .config import GatewaySettings
from .store import GatewayStore


class EnrollmentRequest(StrictModel):
    """Gateway installation request."""

    # Existing device ID or None to allocate one.
    client_instance_id: str | None = Field(default=None, min_length=1, max_length=100)


class EnrollRequest(StrictModel):
    """One-use enrollment proof and durable device identity."""

    # Enrollment code from installer registration.
    code: str = Field(min_length=32, max_length=200)
    # Installation identity persisted before enrollment.
    installation_id: str = Field(min_length=16, max_length=100)
    # Device-specific bearer secret.
    device_token: str = Field(min_length=32, max_length=200, repr=False)


class HeartbeatRequest(StrictModel):
    """Gateway runtime readiness and optional catalog refresh."""

    # CLI readiness independent of network connectivity.
    readiness: Literal["ready", "login_required", "error"]
    # Currently executing transport delivery.
    active_task: str | None = Field(default=None, max_length=100)
    # Latest discovered catalog; None retains existing catalog.
    models: list[ModelInfo] | None = Field(default=None, max_length=1000)


class StartedRequest(StrictModel):
    """Proof that a delivery belongs to authenticated gateway."""

    # Per-delivery random credential.
    lease_token: str = Field(min_length=1, max_length=200, repr=False)


class ResultPayload(StrictModel):
    """Final CLI output before core content validation."""

    # Raw final response; erased after request consumes it.
    raw: str = ""
    # CLI terminal state.
    completion: Literal["completed", "incomplete", "truncated", "failed", "cancelled", "uncertain"]
    # Native session cursor.
    session: str | None = None
    # Optional native request identifier accepted from installed gateways.
    request_id: str | None = None
    # Optional provider counters accepted from installed gateways.
    usage: dict[str, int] = Field(default_factory=dict)


class ResultRequest(StartedRequest):
    """Authenticated final delivery payload."""

    # CLI result to acknowledge exactly once.
    result: ResultPayload


def routes(host: PluginHost, settings: GatewaySettings, store: GatewayStore) -> APIRouter:
    """Return plugin routes using host services, settings, and gateway store."""
    router = APIRouter()
    adapter = CodexAdapter(store)

    def gateway_auth(authorization: str | None = Header(default=None)) -> dict:
        """Return registered device from Bearer authorization."""
        token = authorization[7:] if authorization and authorization.startswith("Bearer ") else ""
        return store.authenticate(token)

    @router.get("/api/codex/models", response_model=ModelCatalog)
    async def models(request: Request,
                     client: str = Header(alias="X-Client-Instance-Id", min_length=1, max_length=100,
                                          openapi_examples={"device": {"value": JSON_EXAMPLES["client_instance_id"]}})):
        """Return available models for client device."""
        context = ProviderContext(client_instance=client)
        return ModelCatalog(models=await connected(request, host.service.models(adapter, context, "codex")))

    @router.post("/api/codex/translations", response_model=TranslationResult,
                 openapi_extra=input_openapi(ModelRequest, {**JSON_EXAMPLES, "model": "MODEL_ID_FROM_CODEX_MODELS"}))
    async def translate(request: Request,
                        client: str = Header(alias="X-Client-Instance-Id", min_length=1, max_length=100,
                                             openapi_examples={"device": {"value": JSON_EXAMPLES["client_instance_id"]}})):
        """Return direct translation of body on client device."""
        body = await read_input(request, ModelRequest)
        context = ProviderContext(client_instance=client)
        return await connected(request, host.service.translate(body, adapter, context, "codex"))

    @router.post("/api/gateways/enrollments", status_code=202, openapi_extra=input_openapi(EnrollmentRequest))
    async def enrollment(request: Request):
        """Return installer enrollment for requested device."""
        body = await read_input(request, EnrollmentRequest)
        if not settings.gateway_bundle or not Path(settings.gateway_bundle).is_file():
            raise ServiceError("gateway_bundle_unavailable", 503)
        if not settings.public_url.startswith("https://"):
            raise ServiceError("https_public_url_required", 503)
        client, code, expires = store.enrollment(body.client_instance_id)
        return {"client_instance_id": client, "expires_at": expires,
                "download_url": settings.public_url.rstrip("/") + "/downloads/gateway/" + code}

    @router.get("/downloads/gateway/{code}")
    def download(code: str):
        """Return installer ZIP containing registration for unused code."""
        registration = store.registration(code)
        if not settings.gateway_bundle or not Path(settings.gateway_bundle).is_file():
            raise ServiceError("gateway_bundle_unavailable", 503)
        buffer = io.BytesIO()
        with zipfile.ZipFile(settings.gateway_bundle) as source, zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as target:
            for info in source.infolist():
                if info.filename != "registration.json":
                    target.writestr(info, source.read(info))
            target.writestr("registration.json", canonical({
                "server_url": settings.public_url, "code": code, "client_instance_id": registration["client_instance"]}))
        return Response(buffer.getvalue(), media_type="application/zip",
                        headers={"Content-Disposition": 'attachment; filename="translator-codex-cli-gateway.zip"',
                                 "Cache-Control": "no-store"})

    @router.post("/internal/gateways/enroll", responses={401: {"model": ErrorResponse}})
    def enroll(body: EnrollRequest):
        """Return device registration from one-use enrollment body."""
        return store.enroll(body.code, body.installation_id, body.device_token)

    @router.get("/api/gateways/{client_instance_id}")
    def status(client_instance_id: str = ApiPath(openapi_examples={
        "device": {"value": JSON_EXAMPLES["client_instance_id"]}})):
        """Return gateway status for client instance."""
        return store.status(client_instance_id)

    @router.delete("/api/gateways/{client_instance_id}", status_code=202)
    def revoke(client_instance_id: str = ApiPath(openapi_examples={
        "device": {"value": JSON_EXAMPLES["client_instance_id"]}})):
        """Revoke client gateway and return acknowledgement."""
        store.revoke(client_instance_id)
        return {"status": "revoked"}

    @router.post("/internal/gateways/heartbeat", responses={401: {"model": ErrorResponse}})
    def heartbeat(body: HeartbeatRequest, gateway=Depends(gateway_auth)):
        """Return cancellations after recording authenticated heartbeat body."""
        models = [m.model_dump() for m in body.models] if body.models is not None else None
        return {"cancel": store.heartbeat(gateway["id"], body.readiness, body.active_task, models)}

    @router.get("/internal/gateways/tasks/next", responses={401: {"model": ErrorResponse}})
    async def next_task(gateway=Depends(gateway_auth)):
        """Long-poll authenticated gateway's reserved delivery for 25 seconds."""
        deadline = time.monotonic()+25
        while True:
            delivery = store.next(gateway["id"])
            if delivery is not None:
                return delivery
            if time.monotonic() >= deadline:
                return Response(status_code=204)
            await asyncio.sleep(0.1)

    @router.post("/internal/gateways/tasks/{identifier}/started", responses={401: {"model": ErrorResponse}})
    def started(identifier: str, body: StartedRequest, gateway=Depends(gateway_auth)):
        """Authorize identifier using gateway's delivery credential."""
        store.started(gateway["id"], identifier, body.lease_token)
        return {"authorized": True}

    @router.post("/internal/gateways/tasks/{identifier}/result", responses={401: {"model": ErrorResponse}})
    def result(identifier: str, body: ResultRequest, gateway=Depends(gateway_auth)):
        """Return idempotent acknowledgement for gateway's result body."""
        return {"status": store.deliver(gateway["id"], identifier, body.lease_token, body.result.model_dump())}

    return router
