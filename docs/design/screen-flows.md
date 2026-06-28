# Screen Flows

## Purpose

- 앱의 화면 이동, 사용자 작업 흐름, navigation contract를 기록한다.
- 사용자가 어떤 화면에서 시작해 어떤 행동을 완료하는지 기록한다.
- Router route, guard, parameter, query, redirect 흐름을 UI 관점에서 추적한다.
- 화면 전환이 API, data, auth, permission, local state에 미치는 영향을 확인한다.
- 구현 후 navigation test, UI 컴포넌트 test, visual verification 범위를 정한다.

## Read When

- 새 screen, modal, dialog, tab, bottom sheet, drawer flow를 설계하거나 구현할 때
- entry point, primary action, success destination, cancel/back behavior가 필요할 때
- display 크기에 따라 screen 구조나 primary action 위치가 달라질 때
- route name/path, guard, parameter, query, deep link 흐름을 확인할 때
- 화면 흐름 변경이 state, API, data, auth, permission에 영향을 줄 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 실제 UI flow/route 구현/디자인 결정과 이 문서가 충돌함을 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다. 실제 앱별 화면이나 navigation 정책을 추측으로 추가하지 않는다. 아래 조건은 갱신을 제안해야 하는 신호다.

Update this document when:

- 새 screen, modal, dialog, tab, bottom sheet, drawer flow가 추가된다.
- Entry point, primary action, success destination, cancel/back behavior가 바뀐다.
- Compact, medium, expanded layout에서 화면 구조나 action 위치가 달라진다.
- Auth/onboarding/permission guard 또는 redirect 흐름이 바뀐다.
- Path parameter, query parameter, deep link, route argument가 바뀐다.
- Loading, empty, error, success, disabled, permission state가 flow에 영향을 준다.
- Flow가 API, data, storage, release, rollback에 영향을 준다.

Do not update this document for:

- 화면 흐름이 바뀌지 않는 작은 copy/style 수정.
- route contract가 바뀌지 않는 내부 UI 컴포넌트 refactor.
- 추측으로 만든 앱별 navigation 정책.
- 단순 구현 로그나 테스트 실행 결과.

미확정 flow 결정은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 둔다. 확정된 결정도 사용자 요청 또는 승인 후 `docs/handoff/decisions.md`에 남긴다.

## Related Docs

| Topic | Source |
| --- | --- |
| UI state criteria | `docs/design/states.md` |
| Design system and component rules | `docs/design/design-system.md` |
| UX principles | `docs/design/ux-principles.md` |
| Navigation architecture and router constants | `docs/architecture/overview.md` |
| Auth, permission, guard behavior | `docs/architecture/auth-permissions.md` |
| Navigation implementation rules | `docs/development/conventions.md` |

이 문서는 route constant나 router implementation의 source of truth가 아니다. 실제 route name/path는 `core/router`의 상수를 따른다.

## Flow Index

| Flow | Entry | Primary Action | Success Destination | Risk | Status |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Status examples: `planned`, `implemented`, `needs-design`, `needs-architecture`, `deprecated`.

## Flow Template

```text
Flow name:
User goal:
Entry point:
Primary screen:
Primary action:
Success destination:
Cancel/back behavior:
Error recovery:
Permission/auth behavior:
Required states:
Responsive variants:
Related route:
Related API/data/auth docs:
Verification:
```

## Screen Contract

| Screen | Route Name | Route Path | Entry From | Exit To | Required State |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Screen rules:

- Screen 이름은 실제 프로젝트의 screen/page 구조와 맞춘다.
- Route name/path 문자열은 screen에서 직접 쓰지 않고 `core/router` 상수를 사용한다.
- Screen은 프레임워크 컨텍스트 기반 navigation decision을 viewmodel에 넘기지 않는다.
- Screen-specific UI 컴포넌트 flow는 해당 screen section에 기록하고, 전역 flow는 Flow Index에 기록한다.

## Responsive Layout Variants

Display 크기에 따라 화면 구조, primary action 위치, navigation pattern이 달라질 때 기록한다.

| Screen/Flow | Compact | Medium | Expanded | Primary Action | Notes |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Variant template:

```text
Screen or flow:
Compact layout:
Medium layout:
Expanded layout:
Primary action location:
Secondary action location:
Navigation pattern:
State differences:
Content hidden or revealed:
Verification:
```

Variant rules:

- Compact, medium, expanded 모두에서 user goal과 primary action이 접근 가능해야 한다.
- Expanded에서만 보이는 핵심 정보가 있으면 compact에서 접근 가능한 경로를 기록한다.
- Layout 구조가 달라져도 loading, empty, error, disabled, permission 의미는 동일해야 한다.
- Breakpoint 값이나 공용 spacing token은 `docs/design/design-system.md`에 기록한다.
- 구현 시 layout builder, media query, 절대/상대 위치 기준은 `docs/development/conventions.md`를 따른다.

## Navigation Contract

| Route | Params | Query | Guard | Builder Input | Notes |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Navigation rules:

- Path parameter는 명확한 이름을 쓴다. 예: `:userId`, `:orderId`.
- Query parameter는 route builder에서 파싱하고, viewmodel에는 필요한 값만 넘긴다.
- Auth, onboarding, permission redirect는 `core/router` guard에서 처리한다.
- Flow가 deep link를 지원하면 entry state와 fallback state를 함께 기록한다.
- Invalid parameter, missing query, unauthorized entry는 error 또는 redirect state로 정의한다.

## Entry And Exit Behavior

| Flow | Entry Condition | Exit Condition | Back Behavior | Cancel Behavior |
| --- | --- | --- | --- | --- |
|  |  |  |  |  |

Behavior rules:

- Back과 cancel은 같은 의미인지 먼저 구분한다.
- 저장되지 않은 변경사항이 있으면 discard confirm 필요 여부를 기록한다.
- 성공 후 이전 화면을 refresh할지, replace할지, pop할지, 새 route로 이동할지 정한다.
- Flow 중복 진입, double submit, stale response 가능성을 확인한다.

## State Mapping

Flow는 `docs/design/states.md`의 상태 정의를 따른다.

| Flow | Loading | Empty | Error | Success | Disabled | Permission |
| --- | --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |  |

State rules:

- Loading 중 route 전환을 허용할지 막을지 정한다.
- Empty와 error를 섞지 않는다.
- Permission-denied나 unauthorized는 crash가 아니라 명시적인 UI state나 redirect로 처리한다.
- Submit flow는 validation, disabled, submitting, success, failure 상태를 함께 확인한다.

## Permission And Guard Flows

| Flow | Required Auth/Permission | Denied State | Recovery | Related Doc |
| --- | --- | --- | --- | --- |
|  |  |  |  |  |

Permission rules:

- Auth/permission 정책이 불명확하면 blocking question으로 사용자에게 먼저 질문한다.
- Login required, session expired, permission denied, native permission denied를 구분한다.
- Owner-only, admin-only, staff-only 같은 정책은 코드, backend/Firebase rule, 사용자 결정으로 확인된 경우에만 기록한다.
- Permission flow가 privacy, data deletion, payment에 영향을 주면 high-risk로 본다.

## Form And Submit Flows

| Flow | Editable Fields | Validation | Submit Target | Success | Failure |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Submit rules:

- Submit 전 client validation과 submit 후 server/domain validation을 구분한다.
- Submit 중 중복 실행을 막는다.
- Success 후 이동, stay, refresh, snackbar/dialog 여부를 기록한다.
- Failure는 raw API error가 아니라 화면 state로 변환한다.

## Handoff Notes

- 새 data, API, auth, storage 판단이 필요하면 `plan-architecture`로 넘긴다.
- UI state 기준이 불명확하면 `docs/design/states.md`를 먼저 보강한다.
- Flow가 release, rollout, rollback behavior를 바꾸면 `docs/operations/`와 `prepare-release`를 확인한다.
- 제품 범위나 acceptance criteria가 불명확하면 `plan-product`로 되돌린다.

## Verification Notes

Screen flow 구현 후 변경 범위에 맞춰 아래를 고려한다.

- Route builder parameter parsing test
- Route guard redirect test
- UI 컴포넌트 test for entry, success, cancel/back, error state
- Viewmodel test for navigation intent or state transition
- Manual visual verification for main flow and failure states
- Compact, medium, expanded responsive layout check when structure changes by display size
- Deep link test when supported
- Large font, small screen, long text overflow check
- `.agents/skills/design-ui/scripts/verify.ps1`
- Related project test command (예: `flutter test`, `npm test` 등)

## Rules

- 모든 flow가 이 문서에 기록될 필요는 없지만, 반복 참조되거나 구현 판단에 영향을 주는 flow는 기록한다.
- Flow를 확정하기 전에 entry, primary action, success, cancel/back, error, permission 상태를 확인한다.
- Route name/path는 `core/router` 상수를 따르고 screen에 path string을 흩뿌리지 않는다.
- Viewmodel은 프레임워크 컨텍스트를 사용하지 않는다.
- Flow가 API, data, auth contract를 바꾸면 `plan-architecture`로 넘긴다.
- Display 크기에 따라 화면 구조가 달라지면 responsive variant를 기록한다.
