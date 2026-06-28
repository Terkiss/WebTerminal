# Harness Docs

Antigravity/AGY harness의 routing, risk, quality, lifecycle, documentation ownership 정책을 기록한다. `AGENTS.md`, `.agents/`와 함께 동작 기준을 이룬다.

## Document Index

| Document | Role |
| --- | --- |
| `prompt-routing.md` | intent, scope, risk 분류와 skill/document 선택 순서, blocking question flow를 기록한다. |
| `risk-policy.md` | low/medium/high risk 기준, high-risk trigger, 사용자 확인이 필요한 작업을 기록한다. |
| `quality-gates.md` | 변경 유형별 완료 기준, 프로젝트 검증, skill verification script 기준을 기록한다. |
| `event-map.md` | user prompt부터 skill, docs, hooks, scripts, completion까지 lifecycle 연결을 기록한다. |
| `documentation-ownership.md` | docs 폴더별 ownership, write timing, metadata, Antigravity/AGY write rule을 기록한다. |

## Rules

- Harness 정책 변경은 사용자 요청이나 승인 없이 진행하지 않는다.
- Runtime 우선순위는 `AGENTS.md`, 실제 config, skill, hook, script 구현을 우선한다.
- 이 폴더의 문서는 상세 기준이고, `AGENTS.md`는 항상 먼저 읽는 최상위 지침이다.
