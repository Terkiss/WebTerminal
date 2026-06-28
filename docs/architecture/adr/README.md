# Architecture Decision Records

## Purpose

이 문서는 ADR 작성 기준, timing, file naming, template을 정의한다.

## Read When

- 되돌리기 어렵거나 팀이 반복해서 참조할 architecture decision이 생겼는지 판단할 때
- API, data, auth, storage, migration, package, state management, router 구조 결정이 장기 영향을 가질 때
- 새 ADR 파일을 작성하거나 기존 ADR을 supersede해야 할 때
- 짧은 결정 로그와 ADR 중 어디에 기록할지 판단할 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 실제 architecture decision 기록 방식과 이 README가 충돌함을 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다. 새 numbered ADR은 실제 결정이 확정된 경우에만 추가한다.

## Related Docs

| Topic | Source |
| --- | --- |
| Architecture overview | `docs/architecture/overview.md` |
| API contract decisions | `docs/architecture/api.md` |
| Data model decisions | `docs/architecture/data-model.md` |
| Auth and permission decisions | `docs/architecture/auth-permissions.md` |
| Decision handoff | `docs/handoff/decisions.md`, `docs/handoff/open-questions.md` |

## When To Write ADR

ADR을 작성하는 기준은 결정의 유지보수 비용, 되돌리기 비용, 영향 범위다. Antigravity/AGY는 후보를 식별하고 초안을 도울 수 있지만, 최종 결정권은 사용자 또는 프로젝트 팀에 있다.

Write an ADR when a decision:

- 여러 layer, module, feature, screen에 영향을 준다.
- API contract, DTO/Entity, storage schema, migration, cache policy에 영향을 준다.
- Auth, permission, privacy, secret, payment, data deletion, release config에 영향을 준다.
- Rollback, recovery, backward compatibility가 어렵다.
- 특정 package, backend, Firebase/service structure, state management, router structure에 장기적으로 묶인다.
- 팀이 나중에 "왜 이렇게 했는가?"를 반복해서 물을 가능성이 높다.

Do not write an ADR for:

- 작은 UI/copy/style 변경.
- 단일 파일 리팩터링.
- 테스트 추가나 테스트 fixture 정리.
- 기존 architecture와 project convention을 그대로 따르는 구현.
- 쉽게 되돌릴 수 있고 반복 참조 가치가 낮은 변경.

## Timing

- 구현 전에 architecture 단계에서 ADR 필요 여부를 판단한다.
- 구현 중 ADR 대상 decision이 발견되면 작업을 멈추고 `plan-architecture`로 되돌린다.
- Blocking question이 있으면 `docs/harness/prompt-routing.md` 기준에 따라 사용자에게 먼저 질문한다.
- 결정이 확정되면 ADR 작성 필요성을 보고하고, 사용자 요청 또는 승인 후 ADR을 작성한다. 짧은 결정 로그가 필요하면 사용자 요청 또는 승인 후 `docs/handoff/decisions.md`에도 링크한다.

## File Naming

새 ADR은 다음 형식을 사용한다.

```text
0001-short-title.md
0002-short-title.md
0003-short-title.md
```

- 번호는 4자리 zero-padding을 사용한다.
- 제목은 `snake_case`가 아니라 markdown 문서명에 읽기 쉬운 lowercase kebab-case를 사용한다.
- 기존 ADR을 덮어쓰기보다 새 ADR에서 supersede 관계를 기록한다.

## ADR Template

```md
# ADR 0001: Short Title

- Status: proposed | accepted | superseded
- Date: YYYY-MM-DD
- Decision owners:

## Context

What problem or constraint forced this decision?

## Decision

What did we decide?

## Options Considered

- Option A:
- Option B:
- Option C:

## Consequences

- Benefits:
- Tradeoffs:
- Migration or rollback considerations:
- Test or verification impact:

## Links

- Related docs:
- Related issue/PR:
- Supersedes:
```

## Rules

- Do not invent architecture decisions from assumptions.
- Keep ADRs focused on one decision.
- Prefer `docs/handoff/decisions.md` for short decision logs that do not need full context.
- Do not record unresolved assumptions as decisions. Report unresolved questions or options and record them in `docs/handoff/open-questions.md` only after user request or approval.
- Report update needs for related `api.md`, `data-model.md`, `auth-permissions.md`, or `overview.md` when an ADR changes those contracts, then update after user request or approval.
