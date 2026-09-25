"""Canonical wire grammar shared with Filehandler; no response repair."""
from collections import Counter
from dataclasses import dataclass
import re


class TokenError(ValueError):
    """Stable token syntax or preservation error."""


@dataclass(frozen=True)
class Part:
    """Decoded run, protected anchor, or region boundary."""

    # Native token identifier; empty for plain text.
    id: str
    # Decoded run contents; None for anchors and boundaries.
    text: str | None


# Canonical token syntax shared with Filehandler.
OPEN = re.compile(r"<ox:(r(?:0|[1-9][0-9]*))>|<ox:([kb](?:0|[1-9][0-9]*))/>")


def structured(text: str) -> bool:
    """Return whether text contains reserved token markers."""
    return "<ox:" in text or "</ox:" in text


def parse(text: str) -> list[Part]:
    """Return decoded parts from text, raising TokenError on invalid syntax."""
    parts, offset = [], 0
    while offset < len(text):
        match = OPEN.match(text, offset)
        if not match:
            raise TokenError("token_syntax")
        offset = match.end()
        if match[2]:
            parts.append(Part(match[2], None))
            continue
        identifier = match[1]
        closing = f"</ox:{identifier}>"
        value = []
        while offset < len(text):
            char = text[offset]
            offset += 1
            if char == "\\":
                if offset == len(text) or text[offset] not in "\\<":
                    raise TokenError("token_syntax")
                value.append(text[offset])
                offset += 1
            elif char == "<":
                if not text.startswith(closing, offset - 1):
                    raise TokenError("token_syntax")
                offset += len(closing) - 1
                break
            else:
                value.append(char)
        else:
            raise TokenError("token_syntax")
        parts.append(Part(identifier, "".join(value)))
    return parts


def validate(source: str, candidate: str, fixed_order: bool = False) -> list[Part]:
    """Check source token identity and regions against candidate.

    Args:
        source: Original source unit.
        candidate: Proposed translation.
        fixed_order: Whether every token must retain original position.
    Returns:
        Candidate parts, or raises TokenError with a stable diagnostic code.
    """
    if not structured(source):
        if source.strip() and not candidate.strip():
            raise TokenError("empty_translation")
        return [Part("", candidate)]
    before, after = parse(source), parse(candidate)
    ids = [p.id for p in before]
    if len(set(ids)) != len(ids) or Counter(ids) != Counter(p.id for p in after):
        raise TokenError("token_identity")
    region, regions = 0, {}
    for part in before:
        barrier = part.id.startswith("b")
        if barrier:
            region += 1
        regions[part.id] = region
        if barrier:
            region += 1
    if fixed_order and ids != [p.id for p in after]:
        raise TokenError("token_order")
    if [regions[p.id] for p in before] != [regions[p.id] for p in after]:
        raise TokenError("token_region")
    if any((p.text or "").strip() for p in before) and not any((p.text or "").strip() for p in after):
        raise TokenError("empty_translation")
    return after
