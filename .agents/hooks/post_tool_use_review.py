#!/usr/bin/env python3
"""Post-tool lightweight risk review for the Antigravity harness."""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import Any


HIGH_RISK_PATH_HINTS = (
    "firebase.json",
    ".firebaserc",
    "google-services.json",
    "GoogleService-Info.plist",
    "Info.plist",
    "build.gradle",
    "build.gradle.kts",
    "pubspec.yaml",
    "package.json",
    "pyproject.toml",
    ".env",
    "secrets",
    "auth",
    "permission",
    "migration",
    "release",
    "rollback",
)


def repo_root() -> Path:
    try:
        out = subprocess.check_output(
            ["git", "rev-parse", "--show-toplevel"],
            stderr=subprocess.DEVNULL,
            text=True,
        ).strip()
        if out:
            return Path(out)
    except Exception:
        pass
    return Path.cwd()


def load_payload() -> Any:
    raw = sys.stdin.read()
    if not raw.strip():
        return {}
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        return {"raw": raw}


def changed_paths(root: Path) -> list[str]:
    try:
        output = subprocess.check_output(
            ["git", "status", "--porcelain"],
            cwd=root,
            stderr=subprocess.DEVNULL,
            text=True,
        )
    except Exception:
        return []

    paths: list[str] = []
    for line in output.splitlines():
        if not line.strip():
            continue
        path = line[3:].strip()
        if " -> " in path:
            path = path.split(" -> ", 1)[1]
        paths.append(path)
    return paths


def main() -> int:
    _ = load_payload()
    root = repo_root()
    changed = changed_paths(root)
    risky = [
        path
        for path in changed
        if any(hint.lower() in path.lower() for hint in HIGH_RISK_PATH_HINTS)
    ]

    if risky:
        print("[harness-review] High-risk path changes detected:", file=sys.stderr)
        for path in risky[:20]:
            print(f"  - {path}", file=sys.stderr)
        print(
            "[harness-review] Confirm AGENTS.md risk classification and run the relevant "
            "skill verification script before completion.",
            file=sys.stderr,
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
