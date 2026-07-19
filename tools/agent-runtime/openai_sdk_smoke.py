#!/usr/bin/env python3
"""Smoke-test WebTerminal's OpenAI-compatible AGY provider API using the official OpenAI SDK."""

from __future__ import annotations

import argparse
import os
import sys

try:
    import openai
    HAS_SDK = True
except ImportError:
    HAS_SDK = False

def main() -> int:
    parser = argparse.ArgumentParser(description="Run an external-harness smoke using openai-python SDK.")
    parser.add_argument("--base-url", default=os.environ.get("WEBTERMINAL_PROVIDER_BASE_URL", "http://127.0.0.1:5255/v1"))
    parser.add_argument("--api-key", default=os.environ.get("WEBTERMINAL_PROVIDER_API_KEY"))
    args = parser.parse_args()

    if not HAS_SDK:
        print("SKIP: openai python SDK is not installed.", file=sys.stderr)
        return 0

    if not args.api_key:
        print("WEBTERMINAL_PROVIDER_API_KEY or --api-key is required", file=sys.stderr)
        return 2

    client = openai.OpenAI(
        base_url=args.base_url.rstrip("/"),
        api_key=args.api_key,
        max_retries=0
    )

    print("Running Chat Completions Non-Streaming...")
    try:
        response = client.chat.completions.create(
            model="agy",
            messages=[{"role": "user", "content": "Hello AGY!"}],
            temperature=0.0
        )
        print("OK:", response.choices[0].message.content)
    except Exception as exc:
        print(f"FAIL Chat Completions: {exc}", file=sys.stderr)
        return 1

    print("Running Chat Completions Streaming...")
    try:
        stream = client.chat.completions.create(
            model="agy",
            messages=[{"role": "user", "content": "Hello AGY!"}],
            stream=True
        )
        content = ""
        for chunk in stream:
            if chunk.choices and chunk.choices[0].delta.content:
                content += chunk.choices[0].delta.content
        print("OK:", content)
    except Exception as exc:
        print(f"FAIL Chat Streaming: {exc}", file=sys.stderr)
        return 1

    print("ALL SMOKE TESTS PASSED")
    return 0

if __name__ == "__main__":
    sys.exit(main())
