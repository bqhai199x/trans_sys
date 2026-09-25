"""Direct translation API with shared manual prompt and validation endpoints."""
import json
from importlib.resources import files
from typing import TypeVar

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.openapi.docs import get_swagger_ui_html
from fastapi.responses import HTMLResponse, JSONResponse, Response
from pydantic import ValidationError
from starlette.datastructures import UploadFile
from starlette.exceptions import HTTPException

from .config import Settings
from .errors import ServiceError
from .extensions import PluginHost, connected, load_plugins
from .providers import GeminiAdapter, ProviderContext
from .schemas import (
    ErrorResponse, GeminiCredentials, GeminiRequest, ModelCatalog, PromptResponse,
    StrictModel, TranslationInput, TranslationResult, ValidateRequest,
)
from .service import TranslationService


TInput = TypeVar("TInput", bound=StrictModel)

# JSON examples keep source unit boundaries visible in Swagger.
JSON_EXAMPLES = {
    "api_key": "YOUR_GEMINI_API_KEY",
    "texts": ["Hello world", "Goodbye"],
    "target_language": "vi",
    "context": "Product interface",
    "custom_prompt": "Use a friendly tone.",
    "model": "gemini-2.5-flash",
    "mode": "batch",
    "reasoning_effort": "",
    "raw_output": '{"texts":["Xin chào thế giới"]}',
    "client_instance_id": "my-windows-device",
}


class BodyLimit:
    """Enforce one HTTP body limit for JSON and form requests."""

    def __init__(self, app, maximum: int):
        """Initialize downstream app and maximum body bytes."""
        # Downstream ASGI application.
        self.app = app
        # Maximum request bytes including multipart framing.
        self.maximum = maximum

    async def __call__(self, scope, receive, send):
        """Forward bounded HTTP scope through receive/send; no return value."""
        if scope["type"] != "http":
            return await self.app(scope, receive, send)
        chunks, size = [], 0
        while True:
            message = await receive()
            if message["type"] == "http.disconnect":
                return
            size += len(message.get("body", b""))
            if size > self.maximum:
                return await JSONResponse({"detail": "request_too_large"}, status_code=413)(scope, receive, send)
            chunks.append(message.get("body", b""))
            if not message.get("more_body"):
                break
        sent = False

        async def buffered():
            """Return buffered body once, then delegate disconnect events."""
            nonlocal sent
            if not sent:
                sent = True
                return {"type": "http.request", "body": b"".join(chunks), "more_body": False}
            return await receive()

        await self.app(scope, buffered, send)


def input_openapi(model: type[StrictModel], examples: dict | None = None) -> dict:
    """Return JSON request contract with field examples for model."""
    schema = model.model_json_schema()
    for name, field in schema["properties"].items():
        field["example"] = (examples or JSON_EXAMPLES)[name]
    return {"requestBody": {"required": True, "content": {"application/json": {"schema": schema}}}}


async def read_input(request: Request, model: type[TInput], error_code: str = "request_schema") -> TInput:
    """Build validated model from JSON or Swagger form fields.

    Args:
        request: HTTP request containing JSON or URL-encoded form fields.
        model: Strict request schema used by existing JSON clients.
        error_code: Sanitized code returned for malformed input.
    Returns:
        Validated request model with single or array form text restored to a string array.
    """
    media_type = request.headers.get("content-type", "").split(";", 1)[0].strip().lower()
    try:
        if not media_type or media_type == "application/json":
            return model.model_validate(await request.json())
        if media_type == "application/x-www-form-urlencoded":
            async with request.form(max_files=0, max_fields=len(model.model_fields)) as form:
                if len(form.multi_items()) != len(form) or any(not isinstance(value, str) for value in form.values()):
                    raise ValueError("invalid_form_fields")
                data = dict(form)
                if "texts" in data:
                    try:
                        parsed = json.loads(data["texts"])
                    except json.JSONDecodeError:
                        parsed = None
                    data["texts"] = parsed if isinstance(parsed, list) else [data["texts"]]
                if data.get("reasoning_effort") == "":
                    data.pop("reasoning_effort")
                if data.get("mode") == "":
                    data.pop("mode")
                return model.model_validate(data)
        raise ValueError("unsupported_media_type")
    except (ValidationError, ValueError, UnicodeError, RecursionError, HTTPException):
        raise ServiceError(error_code, 422) from None


def validation_openapi() -> dict:
    """Return JSON and file-upload contracts for validation."""
    content = input_openapi(ValidateRequest)["requestBody"]["content"]
    content["multipart/form-data"] = {
        "schema": {"type": "object", "required": ["input", "output_file"], "additionalProperties": False,
                   "properties": {
                       "input": {"type": "string", "description": "JSON-encoded TranslationInput.",
                                 "example": '{"texts":["hello"],"target_language":"vi"}'},
                       "output_file": {"type": "string", "format": "binary",
                                       "description": "UTF-8 JSON object with texts array."}}},
        "encoding": {"output_file": {"contentType": "application/json"}},
    }
    return {"requestBody": {"required": True, "content": content}}


async def read_validation(request: Request, maximum: int) -> tuple[TranslationInput, str, bool]:
    """Return original input, model output, and fence policy from request.

    Args:
        request: JSON, URL-encoded form, or multipart HTTP request.
        maximum: Maximum multipart field or file bytes.
    Returns:
        Typed input, decoded output, and whether a code fence is permitted.
    """
    media_type = request.headers.get("content-type", "").split(";", 1)[0].strip().lower()
    if media_type in ("application/json", "application/x-www-form-urlencoded"):
        body = await read_input(request, ValidateRequest, "validation_input_invalid")
        return body, body.raw_output, True
    try:
        if media_type != "multipart/form-data":
            raise ServiceError("unsupported_media_type", 415)
        async with request.form(max_files=1, max_fields=1, max_part_size=maximum) as form:
            if len(form.multi_items()) != 2 or set(form) != {"input", "output_file"}:
                raise ServiceError("validation_upload_schema", 422)
            original, upload = form["input"], form["output_file"]
            if not isinstance(original, str) or not isinstance(upload, UploadFile):
                raise ServiceError("validation_upload_schema", 422)
            body = TranslationInput.model_validate_json(original)
            content = await upload.read(maximum + 1)
            if len(content) > maximum:
                raise ServiceError("request_too_large", 413)
            return body, content.decode("utf-8-sig"), False
    except (ValidationError, ValueError, UnicodeError, RecursionError, HTTPException):
        raise ServiceError("validation_input_invalid", 422) from None


def create_app(settings: Settings | None = None, gemini=None) -> FastAPI:
    """Return API using settings and optional Gemini test adapter."""
    settings = settings or Settings.from_env()
    gemini = gemini or GeminiAdapter()
    service = TranslationService(settings)
    app = FastAPI(title="Translator", version="0.2.0", docs_url=None, responses={
        status: {"model": ErrorResponse} for status in (409, 413, 415, 422, 429, 499, 500, 502, 503, 504)
    })
    app.add_middleware(BodyLimit, maximum=settings.max_request_bytes)

    @app.get("/docs/swagger-fields.js", include_in_schema=False)
    def swagger_fields() -> Response:
        """Return Swagger field editor script for JSON request bodies."""
        script = files("translation_service").joinpath("swagger_fields.js").read_bytes()
        return Response(script, media_type="application/javascript")

    @app.get("/docs", include_in_schema=False)
    def swagger_docs(request: Request) -> HTMLResponse:
        """Return Swagger UI with separate field inputs for JSON requests."""
        root_path = request.scope.get("root_path", "")
        html = get_swagger_ui_html(openapi_url=root_path + app.openapi_url,
                                   title=f"{app.title} - Swagger UI").body.decode("utf-8")
        marker = "<!-- `SwaggerUIBundle` is now available on the page -->"
        html = html.replace(marker, f'<script src="{root_path}/docs/swagger-fields.js"></script>\n    {marker}', 1)
        html = html.replace("const ui = SwaggerUIBundle({",
                            "const ui = SwaggerUIBundle({\n        requestInterceptor: translatorFieldsToJson,", 1)
        return HTMLResponse(html)

    @app.exception_handler(ServiceError)
    async def service_error(request: Request, exc: ServiceError):
        """Return sanitized HTTP response for service exc."""
        headers = {"Retry-After": str(int(exc.retry_after))} if exc.retry_after else None
        return JSONResponse({"detail": exc.code}, status_code=exc.status_code, headers=headers)

    @app.exception_handler(RequestValidationError)
    async def schema_error(request: Request, exc: RequestValidationError):
        """Return schema failure without echoing request values."""
        return JSONResponse({"detail": "request_schema"}, status_code=422)

    @app.exception_handler(Exception)
    async def unexpected_error(request: Request, exc: Exception):
        """Return unexpected failure without logging request or exception content."""
        return JSONResponse({"detail": "internal_error"}, status_code=500)

    @app.get("/health")
    def health() -> dict[str, str]:
        """Return API liveness without requiring a database or provider."""
        return {"status": "ok"}

    @app.post("/api/gemini/models", response_model=ModelCatalog, openapi_extra=input_openapi(GeminiCredentials))
    async def models(request: Request):
        """Return text model catalog using body credential."""
        body = await read_input(request, GeminiCredentials)
        context = ProviderContext(credential=body.api_key)
        catalog = await connected(request, service.models(gemini, context, "gemini"))
        return ModelCatalog(models=catalog)

    @app.post("/api/gemini/translations", response_model=TranslationResult, openapi_extra=input_openapi(GeminiRequest))
    async def translate(request: Request):
        """Return direct translation of body using request credential."""
        body = await read_input(request, GeminiRequest)
        return await connected(request, service.translate(body, gemini, ProviderContext(credential=body.api_key), "gemini"))

    @app.post("/api/genprompt", response_model=PromptResponse, openapi_extra=input_openapi(TranslationInput))
    async def genprompt(request: Request):
        """Return one paste-ready prompt for body."""
        body = await read_input(request, TranslationInput)
        return PromptResponse(prompt=service.genprompt(body))

    @app.post("/api/validate", response_model=TranslationResult, openapi_extra=validation_openapi())
    async def validate_output(request: Request):
        """Return validated pasted or uploaded output from request."""
        body, raw, allow_fence = await read_validation(request, settings.max_request_bytes)
        return service.validate(body, raw, allow_fence)

    load_plugins(PluginHost(app, settings, service))
    return app
