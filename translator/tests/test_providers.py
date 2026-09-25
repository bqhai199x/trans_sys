"""Pinned Gemini SDK transport, capability filtering, and credential isolation."""
import asyncio
import json
import unittest
import warnings

from fastapi.testclient import TestClient
from google import genai
import httpx
from pydantic import SecretStr

from translation_service.api import create_app
from translation_service.config import Settings
from translation_service.providers import GeminiAdapter, ProviderContext, ProviderFailure, Turn
from translation_service.schemas import MODEL_SCHEMA


class GeminiTests(unittest.TestCase):
    """Verify actual SDK request serialization with a fake HTTP transport."""

    def adapter(self, handler):
        """Return adapter whose SDK calls use handler instead of network."""
        def factory(key):
            """Return isolated SDK client authenticated with key."""
            return genai.Client(vertexai=False, api_key=key, http_options={
                "async_client_args": {"transport": httpx.MockTransport(handler)}, "retry_options": {"attempts": 1}})
        return GeminiAdapter(factory)

    def test_sdk_schema_session_and_request_key(self):
        """Interactions carries schema, session, and current request key."""
        calls = []

        def handle(request):
            """Record SDK request and return current Interactions response."""
            calls.append((request.headers["x-goog-api-key"], json.loads(request.content)))
            return httpx.Response(200, json={"id": "cursor-2", "status": "completed", "steps": [
                {"type": "model_output", "content": [{"type": "text", "text": '{"texts":["translated"]}'}]}],
                "usage": {"total_tokens": 50}})

        adapter = self.adapter(handle)
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            result = asyncio.run(adapter.execute(ProviderContext(credential=SecretStr("key-A")),
                                                  Turn("translate", "gemini-2.5-flash", "sync", "cursor-1")))
            asyncio.run(adapter.execute(ProviderContext(credential=SecretStr("key-B")),
                                        Turn("translate", "gemini-2.5-flash", "batch")))
        self.assertEqual("cursor-2", result.session)
        self.assertEqual('{"texts":["translated"]}', result.raw)
        self.assertEqual(["key-A", "key-B"], [key for key, _ in calls])
        self.assertEqual({"type": "text", "mime_type": "application/json", "schema": MODEL_SCHEMA}, calls[0][1]["response_format"])
        self.assertTrue(calls[0][1]["store"])
        self.assertEqual("cursor-1", calls[0][1]["previous_interaction_id"])
        self.assertFalse(calls[1][1]["store"])
        self.assertNotIn("previous_interaction_id", calls[1][1])

    def test_sdk_never_retries_interactions(self):
        """HTTP 429 is returned after exactly one SDK call."""
        calls = []

        def handle(request):
            """Return rate limiting with a caller backoff hint."""
            calls.append(request)
            return httpx.Response(429, headers={"Retry-After": "7"},
                                  json={"error": {"message": "private provider detail", "code": 429, "status": "RESOURCE_EXHAUSTED"}})

        with warnings.catch_warnings(), self.assertRaises(ProviderFailure) as error:
            warnings.simplefilter("ignore", UserWarning)
            asyncio.run(self.adapter(handle).execute(ProviderContext(credential=SecretStr("key")),
                                                      Turn("translate", "gemini-2.5-flash", "batch")))
        self.assertEqual(1, len(calls))
        self.assertEqual(429, error.exception.status_code)
        self.assertEqual(7, error.exception.retry_after)
        self.assertNotIn("private", str(error.exception))

    def test_discovery_is_paginated_filtered_and_not_cached_between_keys(self):
        """Only confirmed text/JSON models survive every discovery page."""
        calls = []

        def handle(request):
            """Return two model pages with misleading generation actions."""
            calls.append(request.headers["x-goog-api-key"])
            if request.url.params.get("pageToken"):
                models = [{"name": "models/gemini-3.8-flash", "supportedGenerationMethods": ["generateContent"]}]
                return httpx.Response(200, json={"models": models})
            models = [{"name": "models/"+name, "supportedGenerationMethods": ["generateContent"]}
                      for name in ["gemini-2.5-flash", "gemini-2.5-flash-image", "gemini-3.1-flash-tts-preview",
                                   "gemini-3.8-live", "gemini-embedding-001", "unknown-text-model"]]
            models.extend([
                {"name": "models/gemini-2.5-pro"},
                {"name": "models/gemini-2.5-flash-lite", "supportedGenerationMethods": ["generateImages"]},
            ])
            return httpx.Response(200, json={"models": models, "nextPageToken": "second"})

        adapter = self.adapter(handle)
        for key in ("one", "two"):
            models = asyncio.run(adapter.discover(ProviderContext(credential=SecretStr(key))))
            self.assertEqual(["gemini-2.5-flash", "gemini-3.8-flash"], [m.id for m in models])
        self.assertEqual(["one", "one", "two", "two"], calls)

    def test_discovery_does_not_retry_or_fallback_to_environment(self):
        """Discovery failure makes one call and missing request key is rejected."""
        calls = []

        def handle(request):
            """Return unavailable discovery endpoint."""
            calls.append(request)
            return httpx.Response(503, json={"error": {"message": "unavailable", "code": 503}})

        with self.assertRaises(ProviderFailure):
            asyncio.run(self.adapter(handle).discover(ProviderContext(credential=SecretStr("key"))))
        self.assertEqual(1, len(calls))
        with self.assertRaises(ProviderFailure):
            asyncio.run(self.adapter(handle).discover(ProviderContext()))
        self.assertEqual(1, len(calls))

    def test_translation_cannot_bypass_discovery_filter(self):
        """Excluded model is rejected before Interactions execution."""
        calls = []

        def handle(request):
            """Only model discovery may be invoked."""
            calls.append(request)
            self.assertTrue(request.url.path.endswith("/models"))
            return httpx.Response(200, json={"models": [{"name": "models/gemini-2.5-flash-image",
                                                         "supportedGenerationMethods": ["generateContent"]}]})

        with TestClient(create_app(Settings(), self.adapter(handle))) as client:
            response = client.post("/api/gemini/translations", json={
                "texts": ["hello"], "target_language": "vi", "model": "gemini-2.5-flash-image", "api_key": "key"})
        self.assertEqual(422, response.status_code)
        self.assertEqual("model_or_effort_unsupported", response.json()["detail"])
        self.assertEqual(1, len(calls))
