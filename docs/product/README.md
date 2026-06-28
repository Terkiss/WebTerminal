# Product Docs

제품 문제, 사용자, 범위, 성공 기준, roadmap을 기록한다. 기능의 의도나 우선순위가 불명확할 때 먼저 참고한다.

## Document Index

| Document | Role |
| --- | --- |
| `problem.md` | 앱이 해결하려는 사용자 문제, desired outcome, non-goal을 기록한다. |
| `target-users.md` | primary/secondary/excluded user와 사용 맥락, accessibility note를 기록한다. |
| `mvp-scope.md` | must/should/could/out-of-scope 기준으로 MVP 범위를 작게 유지한다. |
| `success-metrics.md` | 성공 기준, supporting metric, guardrail, learning goal을 기록한다. |
| `roadmap.md` | now/next/later, dependency, deferred decision을 기록한다. |

## Rules

- 제품 결정은 사용자 확인 없이 확정하지 않는다.
- 기능 요청이 모호하면 `problem.md`, `target-users.md`, `mvp-scope.md` 순서로 확인한다.
- 구현을 막는 제품 질문은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 남긴다.
