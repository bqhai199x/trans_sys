"""Token conformance fixtures and preparation invariants."""
import json
from pathlib import Path
import unittest

from pydantic import ValidationError

from translation_service.domain import prepare, prompt_for
from translation_service.schemas import ModelRequest
from translation_service.tokens import TokenError, validate


class TokenTests(unittest.TestCase):
    """Preserve Filehandler token grammar and dedup boundaries."""

    def test_dedup_keeps_whitespace_at_run_boundaries(self):
        """Whitespace on distinct formatting runs prevents deduplication."""
        prefix = "long word " * 15
        texts = [f"<ox:r0>{prefix}red </ox:r0><ox:r1>car</ox:r1>", f"<ox:r0>{prefix}red</ox:r0><ox:r1> car</ox:r1>"]
        units = prepare({"texts": texts})
        self.assertEqual([0, 1], [u["representative"] for u in units])

    def test_shared_fixtures(self):
        """Python and Filehandler agree on every shared fixture."""
        fixtures = Path(__file__).resolve().parents[2] / "filehandler/tests/FileHandler.Tests/Fixtures/token-fixtures.json"
        for case in json.loads(fixtures.read_text(encoding="utf-8")):
            with self.subTest(case["name"]):
                error = None
                try:
                    validate(case["source"], case["candidate"], case["fixed_order"])
                except TokenError as exc:
                    error = str(exc)
                self.assertEqual(case["error"], error)

    def test_syntax_precedes_filtering(self):
        """Nonalphabetic content still requires valid token syntax."""
        for text in ["<ox:b00/>", "<ox:r0>123</ox:r1>", "<ox:k0/><ox:k0/>"]:
            with self.assertRaises(ValidationError):
                ModelRequest(texts=[text], target_language="vi", model="test")

    def test_prompt_uses_self_describing_tokens(self):
        """Prompt carries token boundary rules without metadata side channel."""
        request = ModelRequest(texts=["<ox:r0>Version </ox:r0><ox:k0/><ox:b0/><ox:r1>next</ox:r1>"],
                               target_language="vi", model="test")
        value = request.content()
        prompt = prompt_for(value, prepare(value), [0])
        self.assertNotIn("token_metadata", prompt)
        self.assertIn("never move any token across a bN boundary", prompt)
        with self.assertRaises(ValidationError):
            ModelRequest.model_validate({**value, "model": "test", "token_metadata": []})
