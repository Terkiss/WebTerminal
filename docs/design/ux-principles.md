# UX Principles

## Purpose

- 사용자 경험 판단 기준을 기록한다.
- 화면이나 flow가 사용자의 실제 목표를 빠르게 달성하게 하는지 확인한다.
- 실패, 권한 제한, 빈 상태, 취소, 되돌리기 같은 recovery path를 설계 전에 점검한다.
- Product 요구사항을 화면 흐름과 사용자 행동 기준으로 바꿀 때의 기준을 제공한다.

## Read When

- 화면이나 flow를 만들기 전에 사용자 목표와 실패 경로를 확인할 때
- primary action, recovery path, feedback, step count를 판단할 때
- product 요구사항을 UX 흐름으로 바꾸기 전에 기준이 필요할 때
- design review에서 사용자가 실제로 무엇을 할 수 있는지 확인할 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 실제 제품/UX 결정과 이 문서가 충돌함을 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다. 앱별 사용자, 문제, journey를 추측으로 확정하지 않는다. 아래 조건은 갱신을 제안해야 하는 신호다.

Update this document when:

- 반복되는 UX 판단 기준이 새로 합의된다.
- 주요 flow의 primary action, recovery path, step count 기준이 바뀐다.
- 사용자에게 보여줄 feedback, confirmation, undo 기준이 바뀐다.
- 제품 범위나 target user 변화가 UX 원칙에 영향을 준다.

Do not update this document for:

- 한 화면의 일회성 copy나 layout 조정.
- 사용자나 product problem을 추측해 만든 journey.
- design system token이나 component contract 변경.
- 단순 구현 로그나 테스트 실행 결과.

미확정 UX 결정은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 둔다. 확정된 결정도 사용자 요청 또는 승인 후 `docs/handoff/decisions.md`에 남긴다.

## Related Docs

| Topic | Source |
| --- | --- |
| Product problem and target users | `docs/product/problem.md`, `docs/product/target-users.md` |
| Screen flows | `docs/design/screen-flows.md` |
| UI states | `docs/design/states.md` |
| UI principles | `docs/design/ui-principles.md` |
| Design system | `docs/design/design-system.md` |

## Boundary

| This Document Owns | Other Documents Own |
| --- | --- |
| 사용자 목표, 흐름 판단, recovery path | 실제 화면 흐름: `docs/design/screen-flows.md` |
| UX-level feedback와 decision criteria | 구체 UI 상태: `docs/design/states.md` |
| Step count, user effort, interruption 기준 | 색상, spacing, component contract: `docs/design/design-system.md` |
| Product requirement를 UX로 해석하는 기준 | Product scope와 user facts: `docs/product/*` |

## Principles

- 사용자의 primary goal을 먼저 확인하고, 화면의 primary action을 그 목표에 맞춘다.
- 반복되는 workflow는 빠르게 수행할 수 있어야 한다.
- 사용자가 retry하거나 수정하기 전에 현재 system state를 알 수 있어야 한다.
- 실패를 숨기지 말고 사용자가 복구할 수 있는 경로를 제공한다.
- 혼란이나 위험을 줄이지 않는 단계는 추가하지 않는다.
- 인증, 권한, 개인정보, 결제, 데이터 삭제처럼 high-risk 행동은 사용자가 결과를 이해하고 확인할 수 있어야 한다.
- Empty, error, permission 상태에서도 다음 행동이 분명해야 한다.
- 사용자의 작업 맥락을 불필요하게 잃게 하지 않는다.

## User Goal

새 화면이나 flow를 설계하기 전에 사용자의 목표를 한 문장으로 정리한다.

```text
User wants to:
So they can:
Success means:
```

Goal rules:

- Goal은 UI 기능 이름이 아니라 사용자가 달성하려는 결과로 쓴다.
- 사용자 유형이 불명확하면 `docs/product/target-users.md`나 사용자에게 확인한다.
- Product scope가 불명확하면 구현 가능한 UI 결정을 확정하지 않는다.

## Primary Action

| Question | Rule |
| --- | --- |
| What is the main action? | 화면마다 primary action을 가능한 한 하나로 둔다. |
| Is the action reversible? | 되돌릴 수 없으면 confirmation, undo, extra review 중 하나를 고려한다. |
| Can the user act now? | 조건이 충족되지 않았으면 disabled 이유나 필요한 next step을 보여준다. |
| What happens after action? | success destination, feedback, refresh 여부를 정한다. |

Primary action rules:

- Primary action은 화면의 사용자 목표와 직접 연결되어야 한다.
- Secondary action은 primary action과 시각적/행동적으로 구분되어야 한다.
- Destructive action은 기본 선택이나 실수하기 쉬운 위치에 두지 않는다.

## Flow Friction

Friction은 사용자가 목표를 달성하기 위해 필요한 노력이다. 무조건 줄이는 것이 아니라, 위험과 혼란을 줄이는 경우에만 의도적으로 추가한다.

| Add Friction When | Reduce Friction When |
| --- | --- |
| 데이터 삭제, 결제, 개인정보 노출, 권한 변경 | 반복 입력, 단순 확인, 조회, 탐색 |
| 되돌릴 수 없는 action | 사용자가 같은 작업을 자주 수행 |
| 사용자가 결과를 오해하기 쉬움 | 단계가 product value와 무관 |
| 법적/정책/보안 확인이 필요 | 앱이 이미 필요한 맥락을 알고 있음 |

## Feedback

사용자 action 뒤에는 결과를 알 수 있어야 한다.

| Action Result | UX Requirement |
| --- | --- |
| Immediate success | 성공 상태나 변경된 data를 보여준다. |
| Delayed success | 진행 중임을 보여주고 중복 action을 막는다. |
| Recoverable failure | 이유와 retry 또는 수정 경로를 제공한다. |
| Permission failure | 로그인, 권한 요청, 설정 이동, 뒤로가기 중 가능한 action을 제공한다. |
| Destructive success | 결과를 명확히 알리고 필요하면 undo나 support path를 제공한다. |

Feedback rules:

- Toast/snackbar만으로 중요한 실패를 처리하지 않는다.
- 사용자가 무엇을 고쳐야 하는지 알 수 없는 error message를 피한다.
- Success popup은 다음 행동을 방해하지 않을 때만 사용한다.

## Recovery

Recovery path는 사용자가 실패나 막힘에서 벗어나는 방법이다.

| Case | Recovery |
| --- | --- |
| Network failure | Retry, later, offline 가능 여부 |
| Validation failure | Field-level correction |
| Unauthorized | Login or re-auth |
| Permission denied | Request permission, settings, previous screen |
| Empty result | Create, add, reset filter, explore |
| Unsaved changes | Save, discard, cancel |

Recovery rules:

- Error와 empty를 구분한다.
- Permission-denied를 crash나 red screen으로 끝내지 않는다.
- 취소와 뒤로가기가 같은 의미인지 flow마다 확인한다.

## Content And Copy

Copy는 사용자의 다음 행동을 돕는 기능이다.

- Button label은 action 결과를 설명한다.
- Error copy는 원인보다 사용자가 할 수 있는 행동을 우선한다.
- Empty copy는 비어 있는 대상과 가능한 next step을 알려준다.
- Permission copy는 왜 필요한지와 사용자가 선택할 수 있는 경로를 말한다.
- 내부 implementation detail, raw exception, provider error message를 그대로 노출하지 않는다.

## Decision Checklist

- What is the user's goal on this screen or flow?
- What is the primary action?
- What can go wrong before, during, and after the action?
- What feedback appears after the user acts?
- Can the user recover without support?
- Does this flow add friction for a real reason?
- Does the flow require product, architecture, auth, or data clarification before implementation?

## Verification Notes

UX decision은 구현 전후에 확인되어야 한다.

Consider:

- primary action이 화면에서 분명한지 확인
- loading, empty, error, disabled, permission 상태 확인
- 성공 후 destination 또는 feedback 확인
- back/cancel/unsaved changes behavior 확인
- high-risk action의 confirmation, undo, rollback 가능성 확인
