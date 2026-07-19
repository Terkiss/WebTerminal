#!/usr/bin/env python3
"""Create a provider session as an admin and run the provider smoke test."""

from __future__ import annotations

import argparse
import http.cookiejar
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

import provider_harness_smoke


def main() -> int:
    parser = argparse.ArgumentParser(description="Run a live WebTerminal AGY provider E2E smoke.")
    parser.add_argument("--origin", default=os.environ.get("WEBTERMINAL_ORIGIN", "http://127.0.0.1:5255"))
    parser.add_argument("--username", default=os.environ.get("WEBTERMINAL_ADMIN_USERNAME"))
    parser.add_argument("--password", default=os.environ.get("WEBTERMINAL_ADMIN_PASSWORD"))
    parser.add_argument("--workspace", default=os.getcwd())
    parser.add_argument("--prompt", default="Use the read_text_file tool to read README.md, then summarize it in one sentence.")
    parser.add_argument("--stream", action="store_true", help="Use SSE streaming for the first provider request.")
    parser.add_argument("--strict-tools", action="store_true", help="Fail if the provider does not request a tool.")
    parser.add_argument("--api", choices=["chat", "responses"], default="chat", help="Which API to smoke test")
    args = parser.parse_args()

    if not args.username or not args.password:
        print("WEBTERMINAL_ADMIN_USERNAME and WEBTERMINAL_ADMIN_PASSWORD are required", file=sys.stderr)
        return 2

    admin_client = AdminClient(args.origin.rstrip("/"))
    admin_client.login(args.username, args.password)
    provider = admin_client.create_provider_session()
    api_key = provider.get("apiKey")
    base_url = provider.get("baseUrl") or f"{args.origin.rstrip('/')}/v1"
    if not api_key:
        print("Provider session response did not include apiKey", file=sys.stderr)
        return 1

    smoke_args = [
        "--base-url",
        base_url,
        "--api-key",
        api_key,
        "--workspace",
        str(Path(args.workspace).resolve()),
        "--prompt",
        args.prompt,
    ]
    if args.stream:
        smoke_args.append("--stream")
    if args.strict_tools:
        smoke_args.append("--strict-tools")
    if args.api:
        smoke_args.extend(["--api", args.api])

    original_argv = sys.argv
    try:
        sys.argv = ["provider_harness_smoke.py", *smoke_args]
        return provider_harness_smoke.main()
    finally:
        sys.argv = original_argv
        session_id = provider.get("sessionId")
        if session_id:
            admin_client.revoke_provider_session(str(session_id))


class AdminClient:
    def __init__(self, origin: str) -> None:
        self.origin = origin
        self.cookie_jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.cookie_jar))

    def login(self, username: str, password: str) -> None:
        self._request(
            "POST",
            "/api/auth/login",
            {"username": username, "password": password},
        )

    def create_provider_session(self) -> dict:
        return self._request(
            "POST",
            "/api/agent/provider-sessions",
            {"profile": "agy-default", "displayName": "Live Provider E2E"},
        )

    def revoke_provider_session(self, session_id: str) -> None:
        try:
            self._request("DELETE", f"/api/agent/provider-sessions/{session_id}")
        except Exception as exc:
            print(f"Warning: failed to revoke provider session {session_id}: {exc}", file=sys.stderr)

    def _request(self, method: str, path: str, payload: dict | None = None) -> dict:
        data = None if payload is None else json.dumps(payload).encode("utf-8")
        request = urllib.request.Request(
            self.origin + path,
            data=data,
            method=method,
            headers={"Content-Type": "application/json"},
        )

        try:
            with self.opener.open(request, timeout=180) as response:
                body = response.read().decode("utf-8")
                return {} if not body else json.loads(body)
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"HTTP {exc.code}: {body}") from exc


if __name__ == "__main__":
    raise SystemExit(main())
