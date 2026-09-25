"""HTTP integration for optional gateway transport and direct Codex translation."""
import asyncio
from contextlib import closing
from io import BytesIO
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch
import uuid
import zipfile

import httpx

from translation_service.api import create_app
from translation_service.config import Settings


class PluginTests(unittest.IsolatedAsyncioTestCase):
    """Exercise installed entry point with gateway and caller HTTP clients."""

    async def asyncSetUp(self):
        """Create isolated plugin database, installer fixture, and API."""
        # Test-owned temporary storage and packaged installer fixture.
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        bundle = self.root / "gateway.zip"
        with zipfile.ZipFile(bundle, "w") as archive:
            archive.writestr("install.ps1", "# installer fixture")
        # Explicit plugin configuration; no translation database exists.
        self.options = {"database": str(self.root / "gateway.sqlite"), "gateway_bundle": str(bundle),
                        "public_url": "https://gateway.test"}
        self.settings = Settings(max_batch_units=1, reasoning_budget_tokens=0, plugins={"codex": self.options})
        self.app = create_app(self.settings)
        # Async HTTP client and request tasks owned by this test.
        self.api = httpx.AsyncClient(transport=httpx.ASGITransport(app=self.app), base_url="https://gateway.test")
        self.pending = []

    async def asyncTearDown(self):
        """Cancel unfinished requests and close test-owned resources."""
        for task in self.pending:
            if not task.done():
                task.cancel()
        await asyncio.gather(*self.pending, return_exceptions=True)
        await self.api.aclose()
        self.directory.cleanup()

    def track(self, operation):
        """Start operation and return a task managed by test cleanup."""
        task = asyncio.create_task(operation)
        self.pending.append(task)
        return task

    async def device(self, name="machine", ready=True):
        """Enroll name through actual installer download and return device context."""
        response = await self.api.post("/api/gateways/enrollments", json={"client_instance_id": name})
        self.assertEqual(202, response.status_code, response.text)
        downloaded = await self.api.get(response.json()["download_url"])
        self.assertEqual("no-store", downloaded.headers["cache-control"])
        with zipfile.ZipFile(BytesIO(downloaded.content)) as archive:
            registration = json.loads(archive.read("registration.json"))
        enrollment = {"code": registration["code"], "installation_id": str(uuid.uuid4()), "device_token": uuid.uuid4().hex}
        enrolled = await self.api.post("/internal/gateways/enroll", json=enrollment)
        self.assertEqual(200, enrolled.status_code, enrolled.text)
        context = {"client": name, "enrollment": enrollment, "id": enrolled.json()["gateway_id"],
                   "headers": {"Authorization": "Bearer "+enrollment["device_token"]}}
        if ready:
            await self.heartbeat(context)
        return context

    async def heartbeat(self, device, active=None, readiness="ready"):
        """Return heartbeat response for device, active delivery, and readiness."""
        return await self.api.post("/internal/gateways/heartbeat", headers=device["headers"], json={
            "readiness": readiness, "active_task": active,
            "models": [{"id": "codex-test", "session": True, "context_tokens": 1000000, "output_tokens": 100000}],
        })

    def body(self, texts=None, mode="batch"):
        """Return translation request using optional texts and mode."""
        return {"texts": texts or ["hello"], "target_language": "vi", "model": "codex-test", "mode": mode}

    def translate(self, device, body=None):
        """Start direct translation on device and return pending HTTP task."""
        return self.track(self.api.post("/api/codex/translations", json=body or self.body(),
                                        headers={"X-Client-Instance-Id": device["client"]}))

    async def next(self, device):
        """Return next reserved delivery for device and authorize execution."""
        response = await asyncio.wait_for(self.api.get("/internal/gateways/tasks/next", headers=device["headers"]), 3)
        self.assertEqual(200, response.status_code, response.text)
        task = response.json()
        started = await self.api.post("/internal/gateways/tasks/"+task["id"]+"/started", headers=device["headers"],
                                      json={"lease_token": task["lease_token"]})
        self.assertEqual(200, started.status_code, started.text)
        return task

    async def deliver(self, device, task, raw='{"texts":["done"]}', session="thread-one", completion="completed"):
        """Return server acknowledgement for task output from device."""
        return await self.api.post("/internal/gateways/tasks/"+task["id"]+"/result", headers=device["headers"],
                                   json={"lease_token": task["lease_token"], "result": {
                                       "raw": raw, "completion": completion, "session": session,
                                       "request_id": None, "usage": {}}})

    async def test_entry_point_and_direct_result(self):
        """Installed plugin completes a request without creating job tables."""
        device = await self.device()
        pending = self.translate(device)
        task = await self.next(device)
        self.assertEqual({"texts": {"type": "array", "items": {"type": "string"}}}, task["schema"]["properties"])
        self.assertNotIn("api_key", task["prompt"])
        acknowledgement = await self.deliver(device, task)
        self.assertEqual("accepted", acknowledgement.json()["status"])
        response = await asyncio.wait_for(pending, 3)
        self.assertEqual(200, response.status_code)
        self.assertEqual(["done"], response.json()["texts"])
        with closing(sqlite3.connect(self.options["database"])) as db:
            tables = {row[0] for row in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
            self.assertEqual({"gateways", "enrollments", "deliveries"}, tables)
            self.assertEqual(("{}", None), db.execute("SELECT payload,response FROM deliveries").fetchone())

    async def test_sync_and_batch_session_policies(self):
        """Sequential turns reuse only sync cursor without introducing jobs."""
        device = await self.device()
        for mode in ("sync", "batch"):
            request = self.translate(device, self.body(["first", "second", "third"], mode))
            for index in range(3):
                task = await self.next(device)
                self.assertEqual("thread-one" if mode == "sync" and index else None, task["session"])
                await self.deliver(device, task)
            response = await asyncio.wait_for(request, 3)
            self.assertEqual(200, response.status_code, response.text)
            self.assertEqual(["done"]*3, response.json()["texts"])

    async def test_public_routes_need_no_caller_authorization(self):
        """Device routing and status ignore missing or arbitrary caller headers."""
        device = await self.device()
        for authorization in (None, "", "null", "Bearer unused"):
            headers = {} if authorization is None else {"Authorization": authorization}
            with self.subTest(authorization=authorization):
                response = await self.api.get("/api/codex/models", headers={
                    **headers, "X-Client-Instance-Id": device["client"]})
                self.assertEqual(200, response.status_code, response.text)
                self.assertEqual("codex-test", response.json()["models"][0]["id"])
                response = await self.api.get("/api/gateways/"+device["client"], headers=headers)
                self.assertEqual(200, response.status_code, response.text)
                self.assertTrue(response.json()["online"])
        response = await self.api.get("/api/codex/models")
        self.assertEqual(422, response.status_code)
        for path, operations in self.app.openapi()["paths"].items():
            if path.startswith("/api/"):
                for operation in operations.values():
                    self.assertNotIn("401", operation["responses"])
                    self.assertFalse(operation.get("security"))
                    self.assertFalse(any(parameter["name"].lower() == "authorization"
                                         for parameter in operation.get("parameters", [])))

    async def test_legacy_form_fields_enroll_and_translate(self):
        """Legacy form fields submit enrollment and translation through core validation."""
        created = await self.api.post("/api/gateways/enrollments", data={"client_instance_id": "form-device"})
        self.assertEqual(202, created.status_code, created.text)
        downloaded = await self.api.get(created.json()["download_url"])
        with zipfile.ZipFile(BytesIO(downloaded.content)) as archive:
            registration = json.loads(archive.read("registration.json"))
        enrollment = {"code": registration["code"], "installation_id": str(uuid.uuid4()),
                      "device_token": uuid.uuid4().hex}
        enrolled = await self.api.post("/internal/gateways/enroll", json=enrollment)
        self.assertEqual(200, enrolled.status_code, enrolled.text)
        device = {"client": "form-device", "headers": {"Authorization": "Bearer "+enrollment["device_token"]}}
        await self.heartbeat(device)
        pending = self.track(self.api.post("/api/codex/translations", data={
            "texts": '["hello"]', "target_language": "vi", "model": "codex-test", "mode": "batch",
            "reasoning_effort": "", "context": "", "custom_prompt": ""},
            headers={"X-Client-Instance-Id": device["client"]}))
        task = await self.next(device)
        await self.deliver(device, task)
        response = await pending
        self.assertEqual(200, response.status_code, response.text)
        self.assertEqual(["done"], response.json()["texts"])
        self.assertEqual([], response.json()["fallbacks"])
        self.assertNotIn("units", response.json())
        for path in ("/api/codex/translations", "/api/gateways/enrollments"):
            content = self.app.openapi()["paths"][path]["post"]["requestBody"]["content"]
            self.assertEqual({"application/json"}, set(content))
            self.assertTrue(all(field.get("example") is not None for field in
                                content["application/json"]["schema"]["properties"].values()))

    async def test_gateway_transport_still_requires_device_token(self):
        """Internal transport rejects missing or invalid device credentials."""
        await self.device()
        requests = [
            ("POST", "/internal/gateways/heartbeat", {"readiness": "ready"}),
            ("GET", "/internal/gateways/tasks/next", None),
            ("POST", "/internal/gateways/tasks/unknown/started", {"lease_token": "test"}),
            ("POST", "/internal/gateways/tasks/unknown/result", {
                "lease_token": "test", "result": {"raw": "", "completion": "completed"}}),
        ]
        for headers in ({}, {"Authorization": "null"}, {"Authorization": "Bearer invalid"}):
            for method, path, body in requests:
                with self.subTest(headers=headers, path=path):
                    response = await self.api.request(method, path, headers=headers, json=body)
                    self.assertEqual(401, response.status_code, response.text)
                    self.assertEqual({"detail": "gateway_credential"}, response.json())

    async def test_existing_gateway_ignores_legacy_caller(self):
        """Legacy device remains usable and can be re-enrolled without caller mapping."""
        device = await self.device()
        with closing(sqlite3.connect(self.options["database"])) as db:
            db.execute("UPDATE gateways SET caller='legacy-owner'")
            db.execute("UPDATE enrollments SET caller='legacy-owner'")
            db.commit()
        await self.api.aclose()
        self.app = create_app(self.settings)
        self.api = httpx.AsyncClient(transport=httpx.ASGITransport(app=self.app), base_url="https://gateway.test")
        response = await self.api.get("/api/gateways/"+device["client"])
        self.assertEqual(200, response.status_code, response.text)
        self.assertTrue(response.json()["online"])
        pending = self.translate(device)
        task = await self.next(device)
        await self.deliver(device, task)
        self.assertEqual(["done"], (await pending).json()["texts"])
        created = await self.api.post("/api/gateways/enrollments", json={"client_instance_id": device["client"]})
        self.assertEqual(202, created.status_code, created.text)
        response = await self.api.delete("/api/gateways/"+device["client"])
        self.assertEqual(202, response.status_code, response.text)
        self.assertEqual(404, (await self.api.get(created.json()["download_url"])).status_code)
        replacement = await self.device(device["client"])
        self.assertEqual(device["id"], replacement["id"])
        self.assertEqual(401, (await self.heartbeat(device)).status_code)
        self.assertEqual(200, (await self.heartbeat(replacement)).status_code)
        with closing(sqlite3.connect(self.options["database"])) as db:
            self.assertEqual(1, db.execute("SELECT COUNT(*) FROM gateways").fetchone()[0])

    async def test_gateway_busy_fails_without_enqueuing(self):
        """Second request receives 409 while first delivery owns gateway."""
        device = await self.device()
        first = self.translate(device)
        task = await self.next(device)
        response = await asyncio.wait_for(self.translate(device), 1)
        self.assertEqual(409, response.status_code)
        self.assertEqual("gateway_busy", response.json()["detail"])
        with closing(sqlite3.connect(self.options["database"])) as db:
            self.assertEqual(1, db.execute("SELECT COUNT(*) FROM deliveries").fetchone()[0])
        await self.deliver(device, task)
        self.assertEqual(200, (await first).status_code)

    async def test_offline_readiness_and_unknown_device_fail_immediately(self):
        """Offline, login-required, and unknown devices are distinct failures."""
        device = await self.device(ready=False)
        response = await asyncio.wait_for(self.translate(device), 1)
        self.assertEqual("gateway_offline", response.json()["detail"])
        await self.heartbeat(device, readiness="login_required")
        response = await self.translate(device)
        self.assertEqual("gateway_not_ready", response.json()["detail"])
        response = await self.api.get("/api/codex/models", headers={
            "X-Client-Instance-Id": "unknown-device"})
        self.assertEqual(404, response.status_code)
        self.assertEqual("client_instance", response.json()["detail"])
        with closing(sqlite3.connect(self.options["database"])) as db:
            self.assertEqual(0, db.execute("SELECT COUNT(*) FROM deliveries").fetchone()[0])

    async def test_lost_ack_and_restart_keep_receipt_without_reexecution(self):
        """Duplicate result remains acknowledged after app recreation."""
        device = await self.device()
        pending = self.translate(device)
        task = await self.next(device)
        self.assertEqual("accepted", (await self.deliver(device, task)).json()["status"])
        self.assertEqual(200, (await pending).status_code)
        await self.api.aclose()
        self.app = create_app(self.settings)
        self.api = httpx.AsyncClient(transport=httpx.ASGITransport(app=self.app), base_url="https://gateway.test")
        self.assertEqual("duplicate", (await self.deliver(device, task)).json()["status"])
        self.assertEqual(409, (await self.deliver(device, task, raw='{"texts":["different"]}')).status_code)
        with closing(sqlite3.connect(self.options["database"])) as db:
            self.assertEqual(1, db.execute("SELECT COUNT(*) FROM deliveries").fetchone()[0])

    async def test_timeout_discards_late_result_and_signals_cancel(self):
        """Timed-out request cancels active CLI and never accepts its late result."""
        await self.api.aclose()
        self.settings.provider_timeout_seconds = 0.2
        self.app = create_app(self.settings)
        self.api = httpx.AsyncClient(transport=httpx.ASGITransport(app=self.app), base_url="https://gateway.test")
        device = await self.device()
        pending = self.translate(device)
        task = await self.next(device)
        response = await pending
        self.assertEqual(504, response.status_code)
        heartbeat = await self.heartbeat(device, task["id"])
        self.assertEqual([task["id"]], heartbeat.json()["cancel"])
        self.assertEqual("discarded", (await self.deliver(device, task)).json()["status"])
        await self.heartbeat(device)
        response = await self.api.get("/api/codex/models", headers={"X-Client-Instance-Id": device["client"]})
        self.assertEqual(200, response.status_code)

    async def test_actual_http_disconnect_discards_delivery(self):
        """ASGI disconnect cancels provider operation and invalidates its receipt."""
        device = await self.device()
        incoming = asyncio.Queue()
        outgoing = []
        await incoming.put({"type": "http.request", "body": json.dumps(self.body()).encode(), "more_body": False})

        async def receive():
            """Return next simulated HTTP peer event."""
            return await incoming.get()

        async def send(message):
            """Capture ASGI response message; no return value."""
            outgoing.append(message)

        scope = {"type": "http", "asgi": {"version": "3.0"}, "http_version": "1.1", "method": "POST",
                 "scheme": "https", "path": "/api/codex/translations", "raw_path": b"/api/codex/translations",
                 "query_string": b"", "root_path": "", "server": ("gateway.test", 443), "client": ("test", 1),
                 "headers": [(b"content-type", b"application/json"),
                             (b"x-client-instance-id", device["client"].encode())]}
        pending = self.track(self.app(scope, receive, send))
        task = await self.next(device)
        await incoming.put({"type": "http.disconnect"})
        await asyncio.wait_for(pending, 3)
        self.assertEqual(499, next(message["status"] for message in outgoing if message["type"] == "http.response.start"))
        self.assertEqual("discarded", (await self.deliver(device, task)).json()["status"])

    async def test_enrollment_retry_and_revoke(self):
        """Enrollment is idempotent for one installation and revocation denies access."""
        device = await self.device()
        response = await self.api.post("/internal/gateways/enroll", json=device["enrollment"])
        self.assertEqual(device["id"], response.json()["gateway_id"])
        response = await self.api.post("/internal/gateways/enroll", json={
            **device["enrollment"], "installation_id": str(uuid.uuid4())})
        self.assertEqual(409, response.status_code)
        response = await self.api.delete("/api/gateways/"+device["client"])
        self.assertEqual(202, response.status_code)
        self.assertEqual(401, (await self.heartbeat(device)).status_code)
        self.assertEqual(409, (await self.api.post("/internal/gateways/enroll", json=device["enrollment"])).status_code)

    async def test_expired_enrollment_is_rejected(self):
        """Expired one-use code cannot register a new device."""
        created = await self.api.post("/api/gateways/enrollments", json={})
        downloaded = await self.api.get(created.json()["download_url"])
        with zipfile.ZipFile(BytesIO(downloaded.content)) as archive:
            registration = json.loads(archive.read("registration.json"))
        with patch("translator_codex_cli.store.time.time", return_value=created.json()["expires_at"]+1):
            response = await self.api.post("/internal/gateways/enroll", json={
                "code": registration["code"], "installation_id": str(uuid.uuid4()), "device_token": uuid.uuid4().hex})
        self.assertEqual(401, response.status_code)
        self.assertEqual("enrollment_expired", response.json()["detail"])

    async def test_other_api_instance_can_receive_inflight_result(self):
        """In-flight delivery remains associated with request across API instances."""
        device = await self.device()
        pending = self.translate(device)
        task = await self.next(device)
        other = create_app(self.settings)
        async with httpx.AsyncClient(transport=httpx.ASGITransport(app=other), base_url="https://gateway.test") as api:
            response = await api.post("/internal/gateways/tasks/"+task["id"]+"/result", headers=device["headers"], json={
                "lease_token": task["lease_token"], "result": {"raw": '{"texts":["other instance"]}', "completion": "completed"}})
        self.assertEqual("accepted", response.json()["status"])
        self.assertEqual(["other instance"], (await pending).json()["texts"])

    async def test_two_gateways_and_foreign_delivery_are_isolated(self):
        """Different devices progress independently and cannot submit each other's result."""
        first, second = await self.device("first-device"), await self.device("second-device")
        a, b = self.translate(first), self.translate(second)
        task_a, task_b = await self.next(first), await self.next(second)
        self.assertNotEqual(task_a["id"], task_b["id"])
        foreign = await self.deliver(second, task_a)
        self.assertEqual("discarded", foreign.json()["status"])
        self.assertFalse(a.done())
        await self.deliver(first, task_a, raw='{"texts":["first result"]}')
        await self.deliver(second, task_b, raw='{"texts":["second result"]}')
        self.assertEqual(["first result"], (await a).json()["texts"])
        self.assertEqual(["second result"], (await b).json()["texts"])

    async def test_late_unknown_journal_is_discarded(self):
        """Old authenticated journals can clear receipts after database replacement."""
        device = await self.device()
        old = {"id": str(uuid.uuid4()), "lease_token": "old-token"}
        response = await self.deliver(device, old)
        self.assertEqual("discarded", response.json()["status"])
        response = await self.api.post("/internal/gateways/tasks/"+old["id"]+"/started", headers=device["headers"],
                                       json={"lease_token": old["lease_token"]})
        self.assertEqual(409, response.status_code)

    async def test_invalid_provider_output_is_fatal_without_second_delivery(self):
        """Malformed Codex output fails one request and never retries."""
        device = await self.device()
        pending = self.translate(device)
        task = await self.next(device)
        await self.deliver(device, task, raw="not JSON")
        response = await pending
        self.assertEqual(502, response.status_code)
        with closing(sqlite3.connect(self.options["database"])) as db:
            self.assertEqual(1, db.execute("SELECT COUNT(*) FROM deliveries").fetchone()[0])
