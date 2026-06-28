# Prompt Routing

이 문서는 사용자 요청을 intent, scope, risk로 분류하고 필요한 skill과 문서를 선택하는 기준이다. 중앙 router skill을 강제하지 않는다. Antigravity/AGY는 각 skill의 `name`과 `description`을 보고 필요한 최소 skill을 선택한다.

## Routing Order

1. `AGENTS.md`를 읽고 최상위 정책을 확인한다.
2. 사용자 요청의 intent를 하나 이상 고른다.
3. 변경 범위인 scope를 고른다.
4. risk를 분류하고, 필요하면 `docs/harness/risk-policy.md`를 확인한다.
5. 필요한 최소 skill set을 선택한다.
6. 선택된 skill의 `Context Loading`에서 필요한 문서만 읽는다.
7. Blocking question이 있으면 파일 수정이나 명령 실행 전에 사용자에게 질문한다.
8. 작업 후 `docs/harness/quality-gates.md`와 skill script 기준에 따라 검증한다.
9. 완료 응답에는 변경 내용, 검증 결과, 남은 위험을 짧게 보고한다.

## Intent

| Intent | Use when | Primary skill |
| --- | --- | --- |
| `product` | 문제, 사용자, MVP, 우선순위, 지표, 로드맵을 정리할 때 | `.agents/skills/plan-product/SKILL.md` |
| `design` | UX, UI, 화면 흐름, 컴포넌트, 상태, 접근성이 바뀔 때 | `.agents/skills/design-ui/SKILL.md` |
| `architecture` | 앱 아키텍처 패턴, API, data, auth, storage, module boundary가 바뀔 때 | `.agents/skills/plan-architecture/SKILL.md` |
| `implementation` | 코드, 설정, 리팩터링, 기능 구현이 필요할 때 | `.agents/skills/implement-feature/SKILL.md` |
| `test` | 테스트 작성, 회귀 검증, 실패 triage, CI 검증이 필요할 때 | `.agents/skills/verify-change/SKILL.md` |
| `deploy` | release, signing, rollout, deploy, rollback readiness가 필요할 때 | `.agents/skills/prepare-release/SKILL.md` |
| `operations` | monitoring, incident, support, post-release feedback가 필요할 때 | `.agents/skills/operate-app/SKILL.md` |
| `harness` | `AGENTS.md`, `.agents/skills/`, `docs/harness/`를 바꿀 때 | 직접 파일과 관련 script를 확인한다 |

## Scope

| Scope | Meaning | Default behavior |
| --- | --- | --- |
| `tiny` | 오탈자, 한 문장, 작은 문서 정리, 국소 스타일 수정 | 필요한 파일만 수정하고 좁은 검증을 한다. |
| `small` | 단일 화면, 단일 함수, 단일 문서 묶음, 작은 테스트 추가 | 관련 skill 하나와 해당 script를 우선한다. |
| `medium` | 여러 파일, 기능 흐름, API/data/UI 상태 영향 | 여러 skill을 순서대로 사용하고 관련 문서를 확인한다. |
| `large` | 앱 구조, 큰 기능, release, migration, 운영 절차 영향 | 작업을 단계로 나누고 검증과 handoff를 명확히 남긴다. |

## Skill Composition

기본 순서는 `product -> design -> architecture -> implementation -> test -> deploy -> operations`이다. 모든 요청에 모든 skill을 쓰지 않는다. 앞 단계 결정이 이미 명확하면 필요한 skill부터 시작한다.

| Request shape | Typical order |
| --- | --- |
| 새 기능이 모호함 | `product -> design/architecture -> implementation -> test` |
| UI만 바뀜 | `design -> implementation -> test 또는 visual verification` |
| API/data/auth가 바뀜 | `architecture -> implementation -> test` |
| 버그 수정 | `implementation -> test` |
| 테스트 보강 | `test` |
| release 준비 | `deploy -> test -> operations` |
| incident 대응 | `operations -> deploy` when rollback or production config may change |
| harness 변경 | related harness files -> syntax/path checks -> skill script checks |

## Document Loading

`AGENTS.md`는 전체 문서 목록을 담지 않는다. 문서 선택은 선택된 skill의 `Context Loading`을 따른다.

- 먼저 관련 코드와 현재 변경사항을 확인한다.
- 문서는 요청과 직접 연결된 것만 읽는다.
- 문서와 코드가 충돌하면 코드를 확인하고 충돌을 보고한다.
- 앱별 사실을 추측으로 채우지 않는다.
- 미확정 질문은 먼저 대화에서 확인하고, 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 남긴다.

## Blocking Questions

Blocking question은 답이 없으면 구현, release, 데이터 변경, 보안/권한 결정을 안전하게 진행할 수 없는 질문이다. Antigravity/AGY는 blocking question을 발견하면 코드 수정, 확정 문서 작성, destructive command, production-impacting action보다 먼저 사용자에게 직접 질문한다.

Blocking으로 보는 경우:

- 제품 범위나 acceptance criteria가 없어 구현 결과를 판단할 수 없다.
- API contract, Firebase/service contract, DTO/Entity mapping, storage schema가 불명확하다.
- Auth, permission, privacy, token/session, credential, payment, data deletion, migration 정책이 불명확하다.
- UI permission/error/empty/loading state가 사용자 행동을 막거나 보안 판단에 영향을 준다.
- 사용자 확인 없이 진행하면 되돌리기 어렵거나 high-risk 정책을 확정하게 된다.

Blocking question flow:

1. 먼저 기존 코드, docs, 사용자 발화에서 답을 찾는다.
2. 답이 없고 작업을 막는다면 사용자에게 1-4개의 짧은 질문으로 바로 확인한다.
3. 사용자가 답하면 갱신 필요성을 보고하고, 사용자 요청 또는 승인 후 관련 docs와 `docs/handoff/decisions.md`에 필요한 만큼 확정 기록한다.
4. 사용자가 나중에 정하겠다고 하면 선택지를 제공하고, 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 blocking으로 기록한다.
5. 사용자가 임시 진행을 원하면 mock, skeleton, draft plan처럼 reversible한 범위로 낮추고 residual risk를 보고한다.

Non-blocking question은 작업을 막지 않는 불확실성이다. Antigravity/AGY는 확정되지 않은 사실을 추측으로 채우지 않고, 선택지를 제공하거나 reversible한 범위로 좁혀 진행하며 앱별 사실처럼 확정 기록하지 않는다.

## Routing Output

작업이 복잡하거나 high-risk이면 내부적으로 다음 정보를 정리한 뒤 진행한다.

- Intent:
- Scope:
- Risk:
- Selected skill:
- Documents to read:
- Verification:
- Residual risk:
