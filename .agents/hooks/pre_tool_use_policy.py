#!/usr/bin/env python3
"""Pre-tool policy check for the Antigravity harness.

The hook is intentionally small and conservative. It blocks obvious destructive
or production-impacting shell commands unless the user/session has explicitly
opted in with HARNESS_ALLOW_HIGH_RISK=1. For non-shell tools it performs only a
lightweight secret-pattern check on the hook payload.
"""

from __future__ import annotations

import json
import os
import re
import sys
from typing import Any


ALLOW_ENV = "HARNESS_ALLOW_HIGH_RISK"

BLOCK_PATTERNS: tuple[tuple[str, str], ...] = (
    (r"\brm\s+-[^;\n]*r[^;\n]*f\b", "recursive forced delete"),
    (r"\bgit\s+reset\s+--hard\b", "hard git reset"),
    (r"\bgit\s+clean\b", "git clean removes untracked files"),
    (r"\bgit\s+checkout\s+--\b", "checkout path can discard user changes"),
    (r"\bfirebase\s+deploy\b", "Firebase deploy is production-impacting"),
    (r"\bfastlane\s+(deliver|supply|pilot|deploy)\b", "Fastlane release action"),
    (r"\bgcloud\s+app\s+deploy\b", "GCP deploy is production-impacting"),
    (r"\bkubectl\s+(apply|delete|rollout|scale)\b", "Kubernetes production-impacting action"),
    (r"\bterraform\s+(apply|destroy)\b", "Terraform mutates infrastructure"),
    (r"\bsupabase\s+db\s+(reset|push)\b", "database mutation"),
)

SECRET_PATTERNS: tuple[tuple[str, str], ...] = (
    (r"(?i)(api[_-]?key|secret|token|password)\s*[:=]\s*['\"][^'\"]{12,}", "possible secret literal"),
    (r"AIza[0-9A-Za-z\-_]{20,}", "possible Google API key"),
    (r"sk-[A-Za-z0-9_\-]{20,}", "possible API key"),
)


def load_payload() -> Any:
    raw = sys.stdin.read()
    if not raw.strip():
        return {}
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        return {"raw": raw}


def iter_strings(value: Any) -> list[str]:
    found: list[str] = []
    if isinstance(value, str):
        found.append(value)
    elif isinstance(value, dict):
        for item in value.values():
            found.extend(iter_strings(item))
    elif isinstance(value, list):
        for item in value:
            found.extend(iter_strings(item))
    return found


def extract_command(payload: Any) -> str:
    if isinstance(payload, dict):
        for key in ("command", "cmd"):
            value = payload.get(key)
            if isinstance(value, str):
                return value
        tool_input = payload.get("tool_input")
        if isinstance(tool_input, dict):
            for key in ("command", "cmd"):
                value = tool_input.get(key)
                if isinstance(value, str):
                    return value
    return ""


def main() -> int:
    payload = load_payload()
    command = extract_command(payload)

    if command and os.environ.get(ALLOW_ENV) != "1":
        for pattern, reason in BLOCK_PATTERNS:
            if re.search(pattern, command):
                print(
                    f"[harness-policy] Blocked {reason}. "
                    f"Confirm the risk, use the matching skill verification script, "
                    f"or set {ALLOW_ENV}=1 for an explicit one-off override.",
                    file=sys.stderr,
                )
                return 2

    payload_text = "\n".join(iter_strings(payload))
    for pattern, reason in SECRET_PATTERNS:
        if re.search(pattern, payload_text):
            print(
                f"[harness-policy] Blocked {reason} in tool payload. "
                "Move secrets to local env/config and avoid committing or logging them.",
                file=sys.stderr,
            )
            return 2

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
