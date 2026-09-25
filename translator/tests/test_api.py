"""HTTP contracts, manual upload, sessions, and request isolation."""
import asyncio
import json
import unittest
from unittest.mock import patch
from urllib.parse import urlencode

from fastapi.testclient import TestClient
import httpx

from translation_service.api import create_app
from translation_service.config import Settings
from translation_service.errors import ServiceError
from translation_service.providers import TurnResult
from translation_service.schemas import ModelInfo


class FakeProvider:
    """Deterministic provider recording request credentials and turns."""

    def __init__(self, outputs=None):
        """Initialize optional sequence of outputs or exceptions."""
        # Captured contexts and turns for boundary assertions.
        self.calls = []
        # Keys used for discovery.
        self.keys = []
        # Explicit response sequence; None generates valid translations.
        self.outputs = None if outputs is None else list(outputs)

    async def discover(self, context):
        """Return test model while recording context credential."""
        self.keys.append(context.credential.get_secret_value() if context.credential else None)
        return [ModelInfo(id="text-test", session=True, context_tokens=1000000, output_tokens=100000)]

    async def execute(self, context, turn):
        """Return next output for turn or a generated valid response."""
        self.calls.append((context, turn))
        if self.outputs is not None:
            result = self.outputs.pop(0)
            if isinstance(result, Exception):
                raise result
            return result
        await asyncio.sleep(0)
        targets = json.loads(turn.prompt.splitlines()[-1])["targets"]
        return TurnResult(json.dumps({"texts": ["translated " + target["text"] for target in targets]}),
                          session="cursor-" + str(len(self.calls)))


class ApiTests(unittest.TestCase):
    """Exercise actual request parsing and shared pipeline behavior."""

    def setUp(self):
        """Create API client and recording provider."""
        # Test-owned provider and app configuration.
        self.provider = FakeProvider()
        self.settings = Settings(max_batch_units=2, reasoning_budget_tokens=0)
        # Client does not call real provider services.
        self.client = TestClient(create_app(self.settings, self.provider))

    def tearDown(self):
        """Close test HTTP client."""
        self.client.close()

    def content(self, texts=None):
        """Return shared input with optional texts override."""
        return {"texts": ["hello"] if texts is None else texts, "target_language": "vi"}

    def request(self, **extra):
        """Return Gemini request with options overridden by extra."""
        return {**self.content(), "api_key": "key-A", "model": "text-test", **extra}

    def test_direct_translation_and_removed_contracts(self):
        """Direct result replaces every job and custom route."""
        response = self.client.post("/api/gemini/translations", json=self.request())
        self.assertEqual(200, response.status_code, response.text)
        self.assertEqual({"status", "texts", "fallbacks"}, set(response.json()))
        self.assertEqual(["translated hello"], response.json()["texts"])
        self.assertEqual([], response.json()["fallbacks"])
        for path in ["/api/custom/translations", "/api/custom/mapping/validate",
                     "/api/gemini/translations/old", "/api/gemini/translations/old/result",
                     "/api/gemini/translations/old/cancel", "/api/gemini/translations/old/resume",
                     "/api/codex/translations"]:
            self.assertEqual(404, self.client.post(path, json={}).status_code, path)
        self.assertEqual(405, self.client.get("/api/gemini/models").status_code)
        self.assertEqual(422, self.client.post("/api/gemini/translations", json=self.request(mode="auto")).status_code)

    def test_default_configuration_needs_no_authorization(self):
        """Core routes accept requests without caller configuration or credentials."""
        with patch.dict("os.environ", {}, clear=True):
            app = create_app(gemini=self.provider)
        with TestClient(app) as client:
            for headers in ({}, {"Authorization": ""}, {"Authorization": "null"},
                            {"Authorization": "Bearer unused"}):
                with self.subTest(headers=headers):
                    response = client.post("/api/genprompt", json=self.content(), headers=headers)
                    self.assertEqual(200, response.status_code, response.text)
                    self.assertIn("hello", response.json()["prompt"])
                    response = client.post("/api/validate", headers=headers, json={
                        **self.content(), "raw_output": '{"texts":["done"]}'})
                    self.assertEqual(200, response.status_code, response.text)
                    self.assertEqual(["done"], response.json()["texts"])
                    response = client.post("/api/gemini/models", json={"api_key": "key-A"}, headers=headers)
                    self.assertEqual(200, response.status_code, response.text)
                    self.assertEqual("text-test", response.json()["models"][0]["id"])
                    response = client.post("/api/gemini/translations", json=self.request(), headers=headers)
                    self.assertEqual(200, response.status_code, response.text)
                    self.assertEqual(["translated hello"], response.json()["texts"])

    def test_legacy_form_fields_match_json_requests(self):
        """Legacy form clients build same validated requests as JSON bodies."""
        form = {"texts": '["hello"]', "target_language": "vi", "context": "Product interface",
                "custom_prompt": "Use a friendly tone."}
        body = {"texts": ["hello"], "target_language": "vi", "context": "Product interface",
                "custom_prompt": "Use a friendly tone."}
        cases = [
            ("/api/gemini/models", {"api_key": "key-A"}, {"api_key": "key-A"}),
            ("/api/genprompt", form, body),
            ("/api/validate", {**form, "raw_output": '{"texts":["Xin chào"]}'},
             {**body, "raw_output": '{"texts":["Xin chào"]}'}),
            ("/api/gemini/translations", {**form, "api_key": "key-A", "model": "text-test", "mode": "batch",
                                          "reasoning_effort": ""},
             {**body, "api_key": "key-A", "model": "text-test", "mode": "batch"}),
        ]
        for path, fields, original in cases:
            with self.subTest(path=path):
                from_form = self.client.post(path, data=fields)
                from_json = self.client.post(path, json=original)
                self.assertEqual(200, from_form.status_code, from_form.text)
                self.assertEqual(from_json.json(), from_form.json())
                content = self.client.app.openapi()["paths"][path]["post"]["requestBody"]["content"]
                expected_media = ({"application/json", "multipart/form-data"} if path == "/api/validate"
                                  else {"application/json"})
                self.assertEqual(expected_media, set(content))
                properties = content["application/json"]["schema"]["properties"]
                self.assertEqual(set(fields), set(properties))
                self.assertTrue(all(property_schema.get("example") is not None
                                    for property_schema in properties.values()))
        validate_content = self.client.app.openapi()["paths"]["/api/validate"]["post"]["requestBody"]["content"]
        self.assertIn("multipart/form-data", validate_content)

    def test_genprompt_legacy_form_accepts_plain_or_array_texts(self):
        """Legacy form accepts one plain unit and preserves JSON array unit order."""
        for value, texts in (("hello", ["hello"]), ("[hello]", ["[hello]"]),
                             ('["hello", "world"]', ["hello", "world"])):
            with self.subTest(value=value):
                response = self.client.post("/api/genprompt", data={"texts": value, "target_language": "vi"})
                expected = self.client.post("/api/genprompt", json={"texts": texts, "target_language": "vi"})
                self.assertEqual(200, response.status_code, response.text)
                self.assertEqual(expected.json(), response.json())
        properties = self.client.app.openapi()["paths"]["/api/genprompt"]["post"]["requestBody"]["content"][
            "application/json"]["schema"]["properties"]
        self.assertEqual("array", properties["texts"]["type"])
        self.assertEqual(["Hello world", "Goodbye"], properties["texts"]["example"])
        media = self.client.app.openapi()["paths"]["/api/genprompt"]["post"]["requestBody"]["content"]
        self.assertEqual({"application/json"}, set(media))

    def test_genprompt_form_preserves_pasted_array_boundaries(self):
        """A pasted JSON array keeps comma-containing tokenized source units distinct."""
        texts = ["<ox:r0>first, second</ox:r0>", "<ox:r0>third</ox:r0>"]
        response = self.client.post("/api/genprompt", data={
            "texts": json.dumps(texts, ensure_ascii=False), "target_language": "vi"})
        expected = self.client.post("/api/genprompt", json={"texts": texts, "target_language": "vi"})
        self.assertEqual(200, response.status_code, response.text)
        self.assertEqual(expected.json(), response.json())

        flattened = self.client.post("/api/genprompt", data={
            "texts": ",".join(texts), "target_language": "vi"})
        self.assertEqual(422, flattened.status_code)
        self.assertEqual({"detail": "request_schema"}, flattened.json())

    def test_swagger_separate_fields_use_json_request(self):
        """Swagger serves separate JSON field editor while API accepts tokenized arrays."""
        docs = self.client.get("/docs")
        self.assertEqual(200, docs.status_code, docs.text)
        self.assertIn("/docs/swagger-fields.js", docs.text)
        self.assertIn("requestInterceptor: translatorFieldsToJson", docs.text)
        script = self.client.get("/docs/swagger-fields.js")
        self.assertEqual(200, script.status_code, script.text)
        self.assertIn("application/javascript", script.headers["content-type"])
        self.assertIn("JSON.stringify(buildBody(readFields(panel)))", script.text)

        texts = ["<ox:r0>first, second</ox:r0>", "<ox:r0>third</ox:r0>"]
        response = self.client.post("/api/genprompt", json={"texts": texts, "target_language": "vi"})
        self.assertEqual(200, response.status_code, response.text)
        self.assertIn("first, second", response.json()["prompt"])

    def test_legacy_form_rejects_malformed_or_duplicate_fields(self):
        """Invalid legacy form data returns sanitized schema errors without provider work."""
        cases = [
            ("/api/genprompt", {"texts": "[1]", "target_language": "vi"}),
            ("/api/genprompt", {"texts": '["hello", 7]', "target_language": "vi"}),
            ("/api/genprompt", {"texts": '["hello"]', "target_language": "vi", "unexpected": "secret"}),
            ("/api/genprompt", [("texts", '["hello"]'), ("texts", '["other"]'),
                                ("target_language", "vi")]),
            ("/api/gemini/translations", {"texts": '["hello"]', "target_language": "vi",
                                          "model": "text-test"}),
        ]
        for path, fields in cases:
            with self.subTest(path=path, fields=fields):
                response = (self.client.post(path, data=fields) if isinstance(fields, dict)
                            else self.client.post(path, content=urlencode(fields),
                                                  headers={"Content-Type": "application/x-www-form-urlencoded"}))
                self.assertEqual(422, response.status_code, response.text)
                self.assertEqual({"detail": "request_schema"}, response.json())
        response = self.client.post("/api/validate", data={"texts": "[1]", "target_language": "vi",
                                                           "raw_output": '{"texts":["done"]}'})
        self.assertEqual(422, response.status_code)
        self.assertEqual({"detail": "validation_input_invalid"}, response.json())
        self.assertEqual([], self.provider.calls)

    def test_sensitive_validation_errors(self):
        """Schema failures never echo key or document."""
        secret = "private-document-and-key"
        for body in [self.request(api_key=""), self.request(api_key=None),
                     self.request(api_key=secret, model=123), self.request(texts=[secret], unknown=secret),
                     self.request(texts=["\\ud800"])]:
            if body.get("texts") == ["\\ud800"]:
                body["texts"] = [chr(0xD800)]
            response = self.client.post("/api/gemini/translations", content=json.dumps(body))
            self.assertEqual(422, response.status_code)
            self.assertNotIn(secret, response.text)
            self.assertNotIn("input", response.json())
        self.assertEqual(422, self.client.post("/api/genprompt", json={**self.content(), "api_key": secret}).status_code)

    def test_keys_are_request_scoped_and_excluded_from_prompt(self):
        """Each discovery/translation uses its own key without caching."""
        for key in ["key-A", "key-B"]:
            response = self.client.post("/api/gemini/models", json={"api_key": key})
            self.assertEqual(200, response.status_code)
            response = self.client.post("/api/gemini/translations", json=self.request(api_key=key))
            self.assertEqual(200, response.status_code)
            self.assertNotIn(key, response.text)
        self.assertEqual(["key-A", "key-A", "key-B", "key-B"], self.provider.keys)
        self.assertEqual(["key-A", "key-B"], [context.credential.get_secret_value() for context, _ in self.provider.calls])
        for _, turn in self.provider.calls:
            self.assertNotIn("api_key", turn.prompt)
            self.assertNotIn("key-A", turn.prompt)
            self.assertNotIn("key-B", turn.prompt)

    def test_sessions_are_reused_only_within_sync_request(self):
        """Sync chains cursors while batch and new requests start fresh."""
        source = ["first", "second", "third", "fourth", "fifth"]
        for mode in ["sync", "batch", "sync"]:
            start = len(self.provider.calls)
            response = self.client.post("/api/gemini/translations", json=self.request(texts=source, mode=mode))
            self.assertEqual(200, response.status_code, response.text)
            calls = [turn for _, turn in self.provider.calls[start:]]
            self.assertEqual(3, len(calls))
            self.assertIsNone(calls[0].session)
            self.assertEqual([None, None, None] if mode == "batch" else [None, f"cursor-{start+1}", f"cursor-{start+2}"],
                             [turn.session for turn in calls])

    def test_invalid_provider_output_and_transport_are_not_retried(self):
        """Fatal output or transport failure produces exactly one execution."""
        for output, status in [(TurnResult("bad"), 502), (TurnResult('{"texts":[]}'), 502),
                               (TurnResult(completion="uncertain"), 502), (ServiceError("rate_limit", 429), 429)]:
            provider = FakeProvider([output])
            with TestClient(create_app(self.settings, provider), headers=self.client.headers) as client:
                response = client.post("/api/gemini/translations", json=self.request(texts=["first", "second", "third"]))
            self.assertEqual(status, response.status_code)
            self.assertEqual(1, len(provider.calls))

    def test_sync_never_replaces_missing_session(self):
        """Missing cursor stops sync before another model call."""
        provider = FakeProvider([TurnResult('{"texts":["mot","hai"]}')])
        with TestClient(create_app(self.settings, provider), headers=self.client.headers) as client:
            response = client.post("/api/gemini/translations",
                                   json=self.request(texts=["first", "second", "third"], mode="sync"))
        self.assertEqual(502, response.status_code)
        self.assertEqual("provider_session_missing", response.json()["detail"])
        self.assertEqual(1, len(provider.calls))

    def test_oversized_unit_does_not_prevent_independent_translation(self):
        """Planner retains oversized source and still translates fitting targets."""
        self.settings.model_capabilities = {"gemini:text-test": {"output_tokens": 100}}
        source = "long unit content " * 20
        response = self.client.post("/api/gemini/translations", json=self.request(texts=[source, "hello"]))
        self.assertEqual(200, response.status_code, response.text)
        self.assertEqual("partial", response.json()["status"])
        self.assertEqual([source, "translated hello"], response.json()["texts"])
        self.assertEqual([{"source_index": 0, "input": source, "output": source,
                           "diagnostics": "unit_too_large"}], response.json()["fallbacks"])
        self.assertEqual(1, len(self.provider.calls))

    def test_sync_budget_exhaustion_never_starts_fresh_session(self):
        """Growing history fails explicitly instead of silently switching mode."""
        self.settings.model_capabilities = {"gemini:text-test": {"context_tokens": 2000}}
        response = self.client.post("/api/gemini/translations", json=self.request(
            texts=["first", "second", "third", "fourth", "fifth", "sixth"], mode="sync"))
        self.assertEqual(502, response.status_code, response.text)
        self.assertEqual("session_budget_exhausted", response.json()["detail"])
        self.assertGreater(len(self.provider.calls), 0)
        self.assertTrue(all(turn.session is not None for _, turn in self.provider.calls[1:]))

    def test_genprompt_filter_dedup_and_own_source_fallback(self):
        """Manual mapping preserves every original source after dedup."""
        source = "long translated content " * 8
        body = self.content([source, source.upper(), "123", ""])
        prompt = self.client.post("/api/genprompt", json=body)
        self.assertEqual(200, prompt.status_code)
        payload = json.loads(prompt.json()["prompt"].splitlines()[-1])
        self.assertEqual([0], [target["source_index"] for target in payload["targets"]])
        result = self.client.post("/api/validate", json={**body, "raw_output": '{"texts":[""]}'}).json()
        self.assertEqual(body["texts"], result["texts"])
        self.assertEqual("partial", result["status"])
        self.assertEqual([0, 1], [item["source_index"] for item in result["fallbacks"]])
        self.assertEqual([source, source.upper()], [item["input"] for item in result["fallbacks"]])
        self.assertEqual([source, source.upper()], [item["output"] for item in result["fallbacks"]])
        self.assertEqual([], self.provider.calls)
        self.assertEqual([], self.provider.keys)

    def test_valid_units_survive_invalid_token_neighbor(self):
        """Only invalid unit retains its own source."""
        body = self.content(["<ox:r0>red</ox:r0>", "<ox:r0>green</ox:r0>"])
        raw = json.dumps({"texts": ["<ox:r0>??</ox:r0>", "<ox:r1>xanh</ox:r1>"]})
        response = self.client.post("/api/validate", json={**body, "raw_output": raw})
        self.assertEqual(200, response.status_code)
        self.assertEqual(["<ox:r0>??</ox:r0>", body["texts"][1]], response.json()["texts"])
        self.assertEqual([{"source_index": 1, "input": body["texts"][1], "output": body["texts"][1],
                           "diagnostics": "token_identity"}], response.json()["fallbacks"])

    def test_text_and_json_upload_are_equivalent_with_bom(self):
        """UTF-8/BOM file and pasted JSON preserve identical Unicode output."""
        body = self.content(["hello", "second"])
        raw = json.dumps({"texts": ["  xin ch?o ??\n", "hai"]}, ensure_ascii=False)
        expected = self.client.post("/api/validate", json={**body, "raw_output": raw})
        for prefix in [b"", b"\xef\xbb\xbf"]:
            response = self.client.post("/api/validate", data={"input": json.dumps(body)},
                                        files={"output_file": ("answer.json", prefix+raw.encode(), "application/json")})
            self.assertEqual(200, response.status_code, response.text)
            self.assertEqual(expected.json(), response.json())

    def test_code_fence_is_only_accepted_for_pasted_output(self):
        """Whole fences are accepted for text, never repaired inside files."""
        raw = '```json\n{"texts":["xin ch?o"]}\n```'
        self.assertEqual(200, self.client.post("/api/validate", json={**self.content(), "raw_output": raw}).status_code)
        for invalid in ["explanation\n"+raw, raw+"\nexplanation", raw+"\n"+raw]:
            self.assertEqual(422, self.client.post("/api/validate",
                                                  json={**self.content(), "raw_output": invalid}).status_code)
        response = self.client.post("/api/validate", data={"input": json.dumps(self.content())},
                                    files={"output_file": ("answer.json", raw.encode(), "application/json")})
        self.assertEqual(422, response.status_code)

    def test_invalid_schema_count_unicode_and_duplicate_properties(self):
        """Malformed responses fail without accepting any translations."""
        for raw in ['[]', '{}', '{"texts":[]}', '{"texts":[1]}', '{"texts":["one","two"]}',
                    '{"texts":["one"],"extra":1}', '{"texts":["one"],"texts":["two"]}',
                    '{"texts":["\\ud800"]}', 'not JSON']:
            with self.subTest(raw=raw):
                response = self.client.post("/api/validate", json={**self.content(), "raw_output": raw})
                self.assertEqual(422, response.status_code)
                self.assertNotIn("texts", response.json())

    def test_upload_rejects_missing_multiple_or_conflicting_parts(self):
        """Multipart accepts exactly one input field and one output file."""
        valid = ("answer.json", b'{"texts":["ok"]}', "application/json")
        for fields, files in [
            ({}, {"output_file": valid}),
            ({"input": "{}"}, {"output_file": valid}),
            ({"input": json.dumps(self.content()), "raw_output": "{}"}, {"output_file": valid}),
            ({"input": json.dumps(self.content())}, [("output_file", valid), ("output_file", valid)]),
            ({"input": json.dumps(self.content())}, {"output_file": ("bad.json", b"\xff", "application/json")}),
        ]:
            response = self.client.post("/api/validate", data=fields, files=files)
            self.assertEqual(422, response.status_code, response.text)
        self.assertEqual(415, self.client.post("/api/validate", content="{}", headers={"Content-Type": "text/plain"}).status_code)

    def test_quotas_apply_to_json_multipart_and_units(self):
        """Shared body and unit limits apply before provider execution."""
        settings = Settings(max_request_bytes=500, max_units=1, max_unit_chars=10)
        with TestClient(create_app(settings, self.provider)) as client:
            self.assertEqual(413, client.post("/api/genprompt", json=self.content(["a", "b"])).status_code)
            self.assertEqual(413, client.post("/api/genprompt", json=self.content(["a"*11])).status_code)
            self.assertEqual(413, client.post("/api/validate", content=b"x"*501).status_code)
            response = client.post("/api/validate", data={"input": json.dumps(self.content())},
                                   files={"output_file": ("large.json", b"x"*501, "application/json")})
            self.assertEqual(413, response.status_code)
        self.assertEqual([], self.provider.calls)

    def test_empty_and_nontext_do_not_call_provider(self):
        """Filtered requests return source without discovery or execution."""
        for texts in [[], ["123", "", "??"]]:
            response = self.client.post("/api/gemini/translations", json=self.request(texts=texts))
            self.assertEqual(200, response.status_code)
            self.assertEqual(texts, response.json()["texts"])
            self.assertEqual([], response.json()["fallbacks"])
        self.assertEqual([], self.provider.calls)
        self.assertEqual([], self.provider.keys)

    def test_core_has_no_storage_or_gateway_routes(self):
        """Default app has no database ownership or installed-plugin side effects."""
        app = create_app(self.settings, self.provider)
        self.assertFalse(hasattr(app.state, "repository"))
        self.assertNotIn("/api/codex/models", app.openapi()["paths"])
        self.assertNotIn("/api/gateways/enrollments", app.openapi()["paths"])
        media = app.openapi()["paths"]["/api/validate"]["post"]["requestBody"]["content"]
        self.assertEqual({"application/json", "multipart/form-data"}, set(media))
        for operations in app.openapi()["paths"].values():
            for operation in operations.values():
                self.assertNotIn("401", operation["responses"])
                self.assertFalse(operation.get("security"))
                self.assertFalse(any(parameter["name"].lower() == "authorization"
                                     for parameter in operation.get("parameters", [])))
        with self.assertRaises(ValueError):
            Settings(gemini_keys={"alice": "forbidden"})


class AsyncRequestTests(unittest.IsolatedAsyncioTestCase):
    """Async request cancellation and credential isolation."""

    async def test_concurrent_keys_never_share_session(self):
        """Overlapping sync requests each begin with an empty cursor."""
        provider = FakeProvider()
        app = create_app(Settings(max_batch_units=1), provider)
        async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
            async def send(key):
                """Return sync response using supplied key."""
                return await client.post("/api/gemini/translations", json={
                    "texts": ["first", "second"], "target_language": "vi", "model": "text-test",
                    "mode": "sync", "api_key": key})
            responses = await asyncio.gather(send("one"), send("two"))
        self.assertEqual([200, 200], [r.status_code for r in responses])
        for key in ("one", "two"):
            calls = [turn for context, turn in provider.calls if context.credential.get_secret_value() == key]
            self.assertEqual(2, len(calls))
            self.assertIsNone(calls[0].session)
            self.assertIsNotNone(calls[1].session)

    async def test_timeout_cancels_provider_once(self):
        """Timeout cancels pending I/O and never starts another call."""
        started, stopped = asyncio.Event(), asyncio.Event()

        class HangingProvider(FakeProvider):
            """Provider blocked until cancellation."""

            async def execute(self, context, turn):
                """Record turn and stop only when HTTP timeout cancels it."""
                self.calls.append((context, turn))
                started.set()
                try:
                    await asyncio.Event().wait()
                finally:
                    stopped.set()

        provider = HangingProvider()
        app = create_app(Settings(provider_timeout_seconds=0.05), provider)
        async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
            response = await client.post("/api/gemini/translations", json={
                "texts": ["hello"], "target_language": "vi", "model": "text-test", "api_key": "key"})
        self.assertEqual(504, response.status_code)
        self.assertTrue(started.is_set() and stopped.is_set())
        self.assertEqual(1, len(provider.calls))
