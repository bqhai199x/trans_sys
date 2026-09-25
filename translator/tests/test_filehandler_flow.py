"""Real Filehandler HTTP import/export around direct and manual translation."""
from email import policy
from email.parser import BytesParser
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time
import unittest

from fastapi.testclient import TestClient
import httpx

from translation_service.api import create_app
from translation_service.config import Settings
from translation_service.providers import TurnResult
from translation_service.schemas import ModelInfo


# Repository root containing untouched Filehandler implementation.
ROOT = Path(__file__).resolve().parents[2]
# Optional prebuilt Filehandler binary for cross-service acceptance.
DLL = Path(os.environ.get("FILEHANDLER_DLL", str(ROOT / "filehandler/src/FileHandler.Api/bin/Debug/net10.0/FileHandler.Api.dll")))


class FakeGemini:
    """Provider producing token-preserving formatting reordering."""

    async def discover(self, context):
        """Return test model available to context."""
        return [ModelInfo(id="test", context_tokens=100000, output_tokens=10000)]

    async def execute(self, context, turn):
        """Return reordered translated runs for turn without real provider I/O."""
        return TurnResult(json.dumps({"texts": ["<ox:r1>xe </ox:r1><ox:r0>??</ox:r0>"]}))


@unittest.skipUnless(DLL.exists(), "Build Filehandler before cross-service acceptance")
class FilehandlerFlowTests(unittest.TestCase):
    """Verify exported bytes retain formatting under both Translator flows."""

    @classmethod
    def setUpClass(cls):
        """Start Filehandler on a temporary loopback port."""
        # Test-owned logs, process, and HTTP client.
        cls.directory = tempfile.TemporaryDirectory()
        cls.log = (Path(cls.directory.name) / "filehandler.log").open("w+b")
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            port = listener.getsockname()[1]
        cls.process = subprocess.Popen(
            ["dotnet", str(DLL), "--urls", f"http://127.0.0.1:{port}"],
            cwd=ROOT / "filehandler/src/FileHandler.Api", stdout=cls.log, stderr=subprocess.STDOUT,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        cls.files = httpx.Client(base_url=f"http://127.0.0.1:{port}", timeout=5, trust_env=False)
        cls.addClassCleanup(cls.stop)
        deadline = time.monotonic()+20
        while time.monotonic() < deadline:
            try:
                if cls.files.get("/swagger/v1/swagger.json").is_success:
                    return
            except httpx.HTTPError:
                pass
            if cls.process.poll() is not None:
                break
            time.sleep(0.1)
        cls.log.seek(0)
        raise AssertionError("Filehandler startup failed: "+cls.log.read().decode("utf-8", errors="replace")[-4000:])

    @classmethod
    def stop(cls):
        """Close test-owned client/process and temporary logs."""
        cls.files.close()
        if cls.process.poll() is None:
            cls.process.terminate()
            try:
                cls.process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                cls.process.kill()
                cls.process.wait(timeout=5)
        cls.log.close()
        cls.directory.cleanup()

    def translate_and_export(self, manual: bool, upload: bool = False):
        """Assert output formatting through automatic or manual/upload flow."""
        source = b"**red** car"
        imported = self.files.post("/api/markdown/import", files={"File": ("source.md", source, "text/markdown")})
        self.assertEqual(200, imported.status_code, imported.text)
        document = imported.json()
        original = {"texts": document["texts"], "target_language": "vi"}
        with TestClient(create_app(Settings(), FakeGemini())) as api:
            if manual:
                prompt = api.post("/api/genprompt", json=original)
                self.assertEqual(200, prompt.status_code)
                self.assertIn(document["texts"][0], json.loads(prompt.json()["prompt"].splitlines()[-1])["targets"][0]["text"])
                raw = json.dumps({"texts": ["<ox:r1>xe </ox:r1><ox:r0>??</ox:r0>"]}, ensure_ascii=False)
                result = (api.post("/api/validate", data={"input": json.dumps(original)},
                                   files={"output_file": ("answer.json", b"\xef\xbb\xbf"+raw.encode(), "application/json")})
                          if upload else api.post("/api/validate", json={**original, "raw_output": raw}))
            else:
                result = api.post("/api/gemini/translations", json={**original, "api_key": "test-key", "model": "test"})
        self.assertEqual(200, result.status_code, result.text)
        self.assertEqual("success", result.json()["status"])
        exported = self.files.post("/api/markdown/export", files={"File": ("source.md", source, "text/markdown")},
                                   data={"Texts": json.dumps(result.json()["texts"])})
        self.assertEqual(200, exported.status_code, exported.text)
        message = BytesParser(policy=policy.default).parsebytes(
            ("Content-Type: "+exported.headers["content-type"]+"\r\nMIME-Version: 1.0\r\n\r\n").encode()+exported.content)
        attachments = [part for part in message.iter_parts() if part.get_filename() == "source.md"]
        self.assertEqual(1, len(attachments))
        self.assertEqual("xe **??**", attachments[0].get_payload(decode=True).decode("utf-8"))

    def test_import_translate_export_preserves_bold(self):
        """Direct provider result exports reordered bold runs correctly."""
        self.translate_and_export(False)

    def test_import_prompt_validate_export_preserves_bold(self):
        """Pasted ChatGPT result exports reordered bold runs correctly."""
        self.translate_and_export(True)

    def test_import_prompt_upload_export_preserves_bold(self):
        """Uploaded JSON/BOM result exports reordered bold runs correctly."""
        self.translate_and_export(True, True)

    def test_imported_text_array_works_in_legacy_genprompt_form(self):
        """Legacy form accepts JSON array of tokenized units from Filehandler."""
        source = b"**red** car"
        imported = self.files.post("/api/markdown/import", files={"File": ("source.md", source, "text/markdown")})
        self.assertEqual(200, imported.status_code, imported.text)
        texts = imported.json()["texts"]
        with TestClient(create_app(Settings(), FakeGemini())) as api:
            response = api.post("/api/genprompt", data={"texts": json.dumps(texts), "target_language": "vi"})
        self.assertEqual(200, response.status_code, response.text)
        self.assertEqual(texts[0], json.loads(response.json()["prompt"].splitlines()[-1])["targets"][0]["text"])
