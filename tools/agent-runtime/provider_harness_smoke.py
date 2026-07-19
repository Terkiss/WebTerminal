#!/usr/bin/env python3
"""Smoke-test WebTerminal's OpenAI-compatible AGY provider API."""

from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description="Run an external-harness smoke against WebTerminal provider mode.")
    parser.add_argument("--base-url", default=os.environ.get("WEBTERMINAL_PROVIDER_BASE_URL", "http://127.0.0.1:5255/v1"))
    parser.add_argument("--api-key", default=os.environ.get("WEBTERMINAL_PROVIDER_API_KEY"))
    parser.add_argument("--workspace", default=os.getcwd())
    parser.add_argument("--prompt", default="Use the read_text_file tool to read README.md, then summarize it in one sentence.")
    args = parser.parse_args()

    if not args.api_key:
        print("WEBTERMINAL_PROVIDER_API_KEY or --api-key is required", file=sys.stderr)
        return 2

    workspace = Path(args.workspace).resolve()
    client = ProviderClient(args.base_url.rstrip("/"), args.api_key)
    models = client.get("/models")
    if not any(model.get("id") == "agy" for model in models.get("data", [])):
        print("Model 'agy' was not listed by provider", file=sys.stderr)
        return 1

    messages = [{"role": "user", "content": args.prompt}]
    tools = [
        {
            "type": "function",
            "function": {
                "name": "read_text_file",
                "description": "Read a UTF-8 text file from the external harness workspace.",
                "parameters": {
                    "type": "object",
                    "properties": {"path": {"type": "string"}},
                    "required": ["path"],
                },
            },
        }
    ]

    first = client.post("/chat/completions", {"model": "agy", "messages": messages, "tools": tools})
    choice = first["choices"][0]
    tool_calls = choice["message"].get("tool_calls") or []
    if not tool_calls:
        print(choice["message"].get("content", ""))
        return 0

    messages.append({"role": "assistant", "content": None, "tool_calls": tool_calls})
    for tool_call in tool_calls:
        result = execute_tool_call(workspace, tool_call)
        messages.append(
            {
                "role": "tool",
                "tool_call_id": tool_call["id"],
                "name": tool_call["function"]["name"],
                "content": result,
            }
        )

    final = client.post("/chat/completions", {"model": "agy", "messages": messages})
    print(final["choices"][0]["message"].get("content", ""))
    return 0


def execute_tool_call(workspace: Path, tool_call: dict) -> str:
    function = tool_call.get("function") or {}
    name = function.get("name")
    arguments_text = function.get("arguments") or "{}"
    try:
        arguments = json.loads(arguments_text)
    except json.JSONDecodeError as exc:
        return json.dumps({"error": f"Invalid tool arguments: {exc}"})

    if name == "read_text_file":
        return read_text_file(workspace, str(arguments.get("path") or ""))

    return json.dumps({"error": f"Unsupported tool: {name}"})


def read_text_file(workspace: Path, relative_path: str) -> str:
    target = (workspace / relative_path).resolve()
    try:
        target.relative_to(workspace)
    except ValueError:
        return json.dumps({"error": "Path escapes workspace"})

    if not target.is_file():
        return json.dumps({"error": "File not found"})

    return target.read_text(encoding="utf-8", errors="replace")


class ProviderClient:
    def __init__(self, base_url: str, api_key: str) -> None:
        self.base_url = base_url
        self.api_key = api_key

    def get(self, path: str) -> dict:
        return self._request("GET", path)

    def post(self, path: str, payload: dict) -> dict:
        return self._request("POST", path, payload)

    def _request(self, method: str, path: str, payload: dict | None = None) -> dict:
        data = None if payload is None else json.dumps(payload).encode("utf-8")
        request = urllib.request.Request(
            self.base_url + path,
            data=data,
            method=method,
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "Content-Type": "application/json",
            },
        )

        try:
            with urllib.request.urlopen(request, timeout=180) as response:
                return json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"HTTP {exc.code}: {body}") from exc


if __name__ == "__main__":
    raise SystemExit(main())
