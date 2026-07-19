#!/usr/bin/env python3
"""Smoke-test WebTerminal's OpenAI-compatible AGY provider API."""

from __future__ import annotations

import argparse
import json
import os
import sys
import uuid
import urllib.error
import urllib.request
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description="Run an external-harness smoke against WebTerminal provider mode.")
    parser.add_argument("--base-url", default=os.environ.get("WEBTERMINAL_PROVIDER_BASE_URL", "http://127.0.0.1:5255/v1"))
    parser.add_argument("--api-key", default=os.environ.get("WEBTERMINAL_PROVIDER_API_KEY"))
    parser.add_argument("--workspace", default=os.getcwd())
    parser.add_argument("--prompt", default="Use the read_text_file tool to read README.md, then summarize it in one sentence.")
    parser.add_argument("--stream", action="store_true", help="Use SSE streaming for the first provider request.")
    parser.add_argument("--strict-tools", action="store_true", help="Fail if the first response does not request a tool.")
    parser.add_argument("--api", choices=["chat", "responses"], default="chat", help="Which API to smoke test")
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

    if args.api == "chat":
        return run_chat_smoke(client, workspace, args.prompt, tools, args.stream, args.strict_tools)
    else:
        return run_responses_smoke(client, workspace, args.prompt, tools, args.stream, args.strict_tools)


def run_chat_smoke(client: ProviderClient, workspace: Path, prompt: str, tools: list, stream: bool, strict_tools: bool) -> int:
    messages = [{"role": "user", "content": prompt}]
    first_payload = {"model": "agy", "messages": messages, "tools": tools, "stream": stream}
    first = client.post(
        "/chat/completions",
        first_payload,
        idempotency_key=f"provider-smoke-first-{uuid.uuid4().hex}",
        stream=stream,
    )
    choice = first["choices"][0]
    tool_calls = choice["message"].get("tool_calls") or []
    if not tool_calls:
        if strict_tools:
            print("Provider returned no tool calls in strict tool mode", file=sys.stderr)
            return 1
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

    final = client.post(
        "/chat/completions",
        {"model": "agy", "messages": messages},
        idempotency_key=f"provider-smoke-final-{uuid.uuid4().hex}",
    )
    validate_completion(final)
    print(final["choices"][0]["message"].get("content", ""))
    return 0


def run_responses_smoke(client: ProviderClient, workspace: Path, prompt: str, tools: list, stream: bool, strict_tools: bool) -> int:
    first_payload = {"model": "agy", "input": prompt, "tools": tools, "stream": stream}
    first = client.post(
        "/responses",
        first_payload,
        idempotency_key=f"provider-smoke-first-{uuid.uuid4().hex}",
        stream=stream,
    )
    
    outputs = first.get("output", [])
    function_calls = [item for item in outputs if item.get("type") == "function_call"]
    if not function_calls:
        if strict_tools:
            print("Provider returned no tool calls in strict tool mode", file=sys.stderr)
            return 1
        messages = [item for item in outputs if item.get("type") == "message"]
        if messages:
            content_arr = messages[0].get("content", [])
            if content_arr:
                print(content_arr[0].get("text", ""))
        return 0

    messages_payload = [{"role": "user", "content": prompt}]
    for call in function_calls:
        tool_call = {
            "function": {
                "name": call.get("name"),
                "arguments": call.get("arguments", "{}")
            }
        }
        result = execute_tool_call(workspace, tool_call)
        messages_payload.append({
            "type": "function_call_output",
            "call_id": call.get("call_id"),
            "output": result
        })

    final = client.post(
        "/responses",
        {"model": "agy", "input": messages_payload},
        idempotency_key=f"provider-smoke-final-{uuid.uuid4().hex}",
    )
    final_outputs = final.get("output", [])
    final_messages = [item for item in final_outputs if item.get("type") == "message"]
    if final_messages:
        content_arr = final_messages[0].get("content", [])
        if content_arr:
            print(content_arr[0].get("text", ""))
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

    def post(self, path: str, payload: dict, idempotency_key: str | None = None, stream: bool = False) -> dict:
        return self._request("POST", path, payload, idempotency_key=idempotency_key, stream=stream)

    def _request(
        self,
        method: str,
        path: str,
        payload: dict | None = None,
        idempotency_key: str | None = None,
        stream: bool = False,
    ) -> dict:
        data = None if payload is None else json.dumps(payload).encode("utf-8")
        headers = {
            "Authorization": f"Bearer {self.api_key}",
            "Content-Type": "application/json",
        }
        if idempotency_key:
            headers["Idempotency-Key"] = idempotency_key

        request = urllib.request.Request(
            self.base_url + path,
            data=data,
            method=method,
            headers=headers,
        )

        try:
            with urllib.request.urlopen(request, timeout=180) as response:
                body = response.read().decode("utf-8")
                return parse_sse_completion(body, "/responses" in path) if stream else json.loads(body)
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"HTTP {exc.code}: {body}") from exc


def parse_sse_completion(body: str, is_responses_api: bool = False) -> dict:
    if is_responses_api:
        response_obj: dict = {"output": []}
        for line in body.splitlines():
            if not line.startswith("data:"):
                continue
            data = line[5:].strip()
            if data == "[DONE]":
                continue
            event = json.loads(data)
            evt_type = event.get("type")
            if evt_type == "response.created":
                pass
            elif evt_type == "response.output_item.added":
                item = event.get("item", {})
                response_obj["output"].append(item)
            elif evt_type == "response.output_text.delta":
                delta = event.get("delta", "")
                idx = event.get("output_index", 0)
                if idx < len(response_obj["output"]):
                    content_arr = response_obj["output"][idx].setdefault("content", [{"type": "text", "text": ""}])
                    if content_arr:
                        content_arr[0]["text"] += delta
            elif evt_type == "response.function_call_arguments.delta":
                delta = event.get("delta", "")
                idx = event.get("output_index", 0)
                if idx < len(response_obj["output"]):
                    args = response_obj["output"][idx].get("arguments", "")
                    response_obj["output"][idx]["arguments"] = args + delta
            elif evt_type == "response.completed":
                response_obj["status"] = event.get("response", {}).get("status")
        return response_obj
    else:
        message: dict = {"role": "assistant"}
        finish_reason = None
        for line in body.splitlines():
            if not line.startswith("data:"):
                continue

            data = line[5:].strip()
            if data == "[DONE]":
                continue

            event = json.loads(data)
            choice = (event.get("choices") or [{}])[0]
            delta = choice.get("delta") or {}
            finish_reason = choice.get("finish_reason") or finish_reason
            if "content" in delta:
                message["content"] = (message.get("content") or "") + (delta.get("content") or "")
            if "tool_calls" in delta:
                message["tool_calls"] = delta["tool_calls"]

        return {"choices": [{"message": message, "finish_reason": finish_reason}]}


def validate_completion(completion: dict) -> None:
    choices = completion.get("choices") or []
    if not choices:
        raise RuntimeError("Provider response did not include choices")
    message = choices[0].get("message") or {}
    if not isinstance(message, dict):
        raise RuntimeError("Provider response choice did not include a message object")


if __name__ == "__main__":
    raise SystemExit(main())