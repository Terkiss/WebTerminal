#!/usr/bin/env python3
"""Relay AGY hook events to WebTerminal's internal Agent Runtime endpoint."""

from __future__ import annotations

import argparse
import hashlib
import hmac
import json
import os
import sys
import time
import urllib.error
import urllib.request
import uuid


def main() -> int:
    parser = argparse.ArgumentParser(description="Relay one AGY hook event to WebTerminal.")
    parser.add_argument("--endpoint", default=os.environ.get("WEBTERMINAL_AGENT_EVENT_ENDPOINT", "http://127.0.0.1:5255/api/internal/agent-events"))
    parser.add_argument("--secret", default=os.environ.get("WEBTERMINAL_AGENT_EVENT_SECRET"))
    parser.add_argument("--provider-session-id", default=os.environ.get("WEBTERMINAL_PROVIDER_SESSION_ID"))
    args = parser.parse_args()

    if not args.secret:
        print("WEBTERMINAL_AGENT_EVENT_SECRET is required", file=sys.stderr)
        return 2
    if not args.provider_session_id:
        print("WEBTERMINAL_PROVIDER_SESSION_ID is required", file=sys.stderr)
        return 2

    try:
        hook_input = json.load(sys.stdin)
    except json.JSONDecodeError as exc:
        print(f"Invalid hook JSON: {exc}", file=sys.stderr)
        return 2

    event = build_event(hook_input, args.provider_session_id)
    body = json.dumps(event, separators=(",", ":"), ensure_ascii=False)
    timestamp = str(int(time.time()))
    nonce = uuid.uuid4().hex
    signature = hmac.new(
        args.secret.encode("utf-8"),
        f"{timestamp}.{nonce}.{body}".encode("utf-8"),
        hashlib.sha256,
    ).hexdigest()

    request = urllib.request.Request(
        args.endpoint,
        data=body.encode("utf-8"),
        method="POST",
        headers={
            "Content-Type": "application/json",
            "X-Agent-Event-Timestamp": timestamp,
            "X-Agent-Event-Nonce": nonce,
            "X-Agent-Event-Signature": signature,
        },
    )

    try:
        with urllib.request.urlopen(request, timeout=2) as response:
            response.read()
    except urllib.error.HTTPError as exc:
        print(f"WebTerminal rejected hook event: HTTP {exc.code}", file=sys.stderr)
        return 1
    except OSError as exc:
        print(f"Failed to relay hook event: {exc}", file=sys.stderr)
        return 1

    return 0


def build_event(hook_input: dict, provider_session_id: str) -> dict:
    hook_name = str(
        first_present(
            hook_input,
            "hookName",
            "event",
            "eventType",
            "type",
            ("hook", "name"),
            ("hook", "type"),
        )
        or ""
    )
    event_type = {
        "PreInvocation": "conversation.started",
        "PostInvocation": "invocation.completed",
        "Stop": "conversation.stopped",
    }.get(hook_name, hook_name or "agy.hook")

    conversation_id = first_present(hook_input, "conversationId", ("conversation", "id"), ("context", "conversationId"))
    step_idx = first_present(hook_input, "stepIdx", "stepIndex", "invocationIndex", ("invocation", "stepIdx"))
    transcript_path = first_present(hook_input, "transcriptPath", ("transcript", "path"), ("context", "transcriptPath"))
    workspace_paths = first_present(hook_input, "workspacePaths", ("context", "workspacePaths"))
    event_id = hook_input.get("eventId") or normalize_event_id(conversation_id, event_type, step_idx)

    return {
        "eventId": event_id,
        "eventType": event_type,
        "providerSessionId": provider_session_id,
        "conversationId": conversation_id,
        "stepIdx": step_idx,
        "transcriptPath": transcript_path,
        "workspacePaths": workspace_paths,
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }


def first_present(source: dict, *keys: object) -> object:
    for key in keys:
        if isinstance(key, tuple):
            value = source
            for part in key:
                if not isinstance(value, dict) or part not in value:
                    value = None
                    break
                value = value[part]
            if value is not None:
                return value
            continue

        if key in source and source[key] is not None:
            return source[key]

    return None


def normalize_event_id(conversation_id: object, event_type: str, step_idx: object) -> str:
    conversation = str(conversation_id or "none")
    step = str(step_idx if step_idx is not None else "none")
    return f"{conversation}:{event_type}:{step}"


if __name__ == "__main__":
    raise SystemExit(main())
