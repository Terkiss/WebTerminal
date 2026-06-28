# UI States

## Purpose

- 앱에서 사용자에게 보이는 화면 상태를 정의한다.
- 화면과 컴포넌트가 가져야 할 user-visible state를 명확히 한다.
- View, viewmodel, domain/data failure 사이의 표현 경계를 맞춘다.
- 비동기 작업, 권한 실패, 입력 validation, retry, disabled action을 누락하지 않게 한다.
- UI 구현 후 UI 컴포넌트 test, visual snapshot, screenshot, manual visual verification 기준을 정한다.

## Read When

- 새 화면이나 주요 component의 loading, empty, error, success, disabled, permission 상태를 설계할 때
- UI 구현 또는 변경이 사용자에게 보이는 상태를 바꿀 때
- validation, retry, duplicate submit, permission-denied, unauthorized 상태를 확인할 때
- UI test, screenshot, visual snapshot, manual visual verification 범위를 정할 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 실제 UI 구현/디자인 결정과 이 문서가 충돌함을 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다. 실제 앱별 화면을 추측으로 추가하지 않는다. 아래 조건은 갱신을 제안해야 하는 신호다.

Update this document when:

- 새 화면, 새 flow, 새 주요 component에 loading, empty, error, success, disabled, permission 상태가 필요하다.
- Error, permission-denied, validation, retry, submit 가능 여부가 바뀐다.
- Async loading, stale response, cancellation, optimistic update, refresh behavior가 바뀐다.
- API/data/auth 변경으로 사용자에게 보이는 failure state가 바뀐다.
- 공용 컴포넌트의 상태 표현 기준이 바뀐다.

Do not update this document for:

- 상태 의미가 바뀌지 않는 작은 copy/style 수정.
- 앱별 결정 없이 추측으로 만든 화면 상태.
- 단순 구현 로그나 테스트 실행 결과.

미확정 상태 결정은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 둔다. 확정된 결정도 사용자 요청 또는 승인 후 `docs/handoff/decisions.md`에 남긴다.

## Related Docs

| Topic | Source |
| --- | --- |
| UX 기준 | `docs/design/ux-principles.md` |
| UI/visual 기준 | `docs/design/ui-principles.md` |
| Component, color, typography, spacing | `docs/design/design-system.md` |
| Screen flow and navigation | `docs/design/screen-flows.md` |
| Async, Result, validation, error handling | `docs/development/conventions.md` |
| Auth, permission, privacy failure states | `docs/architecture/auth-permissions.md` |

이 문서는 `AppFailure`, DTO, raw exception을 직접 정의하지 않는다. 화면에 보여야 하는 state와 user action만 기록한다.

## Required State Matrix

새 화면이나 주요 컴포넌트는 아래 상태가 필요한지 먼저 판단한다.

| State | User Sees | User Can Do | Implementation Notes |
| --- | --- | --- | --- |
| Loading | 진행 중임을 알 수 있는 표시 | 기다리거나 취소/뒤로가기 | skeleton, spinner, progress, disabled action |
| Empty | 데이터가 없지만 정상 상태임을 알 수 있는 안내 | 첫 행동, 새로고침, 탐색 | empty는 failure가 아님 |
| Error | 실패 이유와 복구 가능성을 이해할 수 있는 안내 | retry, 수정, 이동, 문의 | raw exception 노출 금지 |
| Success | 작업 완료 또는 정상 data | 다음 행동, 확인, 계속 진행 | 불필요한 성공 팝업 남발 금지 |
| Disabled | 지금 action을 할 수 없는 이유 | 요구 조건 충족 | 이유 없이 버튼만 비활성화 금지 |
| Permission | 권한/인증이 필요하거나 거부됨 | 로그인, 권한 요청, 설정 이동 | auth/privacy 문서와 연결 |

## State Ownership

| Layer | Owns |
| --- | --- |
| View | 상태 표시, layout, copy, user action control |
| Viewmodel | loading/data/error/empty/disabled 상태 조합, retry/submit action |
| Domain/use case | business validation, permission rule 결과 |
| Data/repository | external error를 `AppFailure`로 mapping |

Rules:

- View는 DTO, raw exception, `Result<T>`를 직접 다루지 않는다.
- Viewmodel은 `Result<T>`를 화면 상태나 async state 객체로 변환하는 마지막 경계다.
- Empty는 성공했지만 표시할 데이터가 없는 상태로 보고, error와 구분한다.
- Permission-denied, unauthorized는 crash나 red screen이 아니라 명시적인 UI state로 처리한다.

## Loading State

Loading은 사용자가 앱이 멈춘 것으로 오해하지 않게 해야 한다.

| Case | Preferred Behavior | Avoid |
| --- | --- | --- |
| Initial load | 주요 영역에 skeleton, progress, 또는 안정적인 placeholder 표시 | 빈 화면만 표시 |
| Pull refresh | 기존 data를 유지하고 refresh 표시 | data를 즉시 지우기 |
| Submit | submit action을 중복 실행하지 못하게 처리 | 중복 요청 허용 |
| Pagination | list 하단 progress 또는 next page loading 표시 | 전체 화면 loading으로 list 제거 |

Loading rules:

- Loading 중 layout이 크게 흔들리지 않게 stable size를 유지한다.
- 이미 data가 있으면 가능한 한 data를 유지하고 refresh 상태를 겹쳐 표현한다.
- 취소, 뒤로가기, navigation 가능 여부를 화면 성격에 맞게 정한다.

## Empty State

Empty는 정상 응답이지만 보여줄 content가 없는 상태다.

| Case | User Message | Primary Action |
| --- | --- | --- |
| No data yet | 무엇이 비어 있는지 설명 | 생성, 추가, 탐색 |
| No search results | 검색 조건에 맞는 결과가 없음을 설명 | 검색어 수정, filter reset |
| No permission-visible data | 접근 가능한 항목이 없음을 설명 | 권한 요청 또는 다른 화면 이동 |

Empty rules:

- Empty를 error처럼 표현하지 않는다.
- 다음에 할 수 있는 유용한 행동을 제공한다.
- API failure나 permission failure를 empty로 숨기지 않는다.

## Error State

Error는 실패가 발생했고 사용자가 복구 가능성을 이해해야 하는 상태다.

| Error Type | User State | Recovery |
| --- | --- | --- |
| Network unavailable | 연결 문제 안내 | retry |
| Server unavailable | 일시적 문제 안내 | retry, later |
| Validation error | 입력 수정 안내 | field 수정 |
| Unauthorized | 로그인 필요 또는 세션 만료 안내 | login, re-auth |
| Permission denied | 접근 권한 없음 안내 | 권한 요청, 설정, 이전 화면 |
| Unknown error | 일반 실패 안내 | retry, support path |

Error rules:

- Raw exception, stack trace, API response body를 사용자에게 보여주지 않는다.
- Error message는 사용자 행동과 연결되어야 한다.
- Retry 가능 여부와 retry action을 명확히 한다.
- 같은 실패가 반복되면 운영 로그나 monitoring 필요성을 검토한다.
- Debug red screen은 root cause를 고치고, catch-all fallback으로 덮지 않는다.

## Success State

Success는 data 표시 또는 작업 완료를 의미한다.

Success rules:

- 성공 toast, dialog, snackbar는 사용자의 다음 행동을 방해하지 않을 때만 사용한다.
- 저장/수정 성공 후 이동, stay, refresh 여부를 화면 flow와 맞춘다.
- Optimistic update를 사용하면 실패 시 rollback 또는 error recovery를 정의한다.
- 성공 상태에서도 다음 가능한 action과 disabled 조건을 유지한다.

## Disabled State

Disabled는 사용자가 action을 실행할 수 없는 상태다.

Disabled rules:

- 버튼이나 control을 비활성화할 때 이유가 화면 맥락에서 이해되어야 한다.
- Form submit은 required field, validation, loading, permission 상태에 따라 disabled될 수 있다.
- Disabled control만 있고 설명이 없으면 사용자가 막힌 이유를 알 수 없다.
- Loading 중 disabled와 validation disabled를 구분한다.

## Permission State

Permission state는 auth, role, owner rule, native permission, privacy consent와 연결된다.

| Case | User State | Action |
| --- | --- | --- |
| Login required | 로그인 필요 | login route |
| Session expired | 세션 만료 | re-auth |
| Owner-only denied | 접근 권한 없음 | 이전 화면, 홈, 문의 |
| Native permission denied | 기기 권한 필요 | 권한 요청 또는 설정 이동 |
| Privacy consent required | 동의 필요 | 동의 flow |

Permission rules:

- Auth/permission 정책은 `docs/architecture/auth-permissions.md`에서 확인한다.
- 정책이 불명확하면 blocking question으로 사용자에게 먼저 질문한다.
- Permission state는 사용자에게 책임을 돌리는 표현을 피하고 가능한 다음 행동을 제시한다.
- Native permission 요청은 플랫폼 정책과 사용자 승인이 필요한지 확인한다.

## Form And Validation States

Form 화면은 submit 전 client validation과 submit 후 server/domain validation을 구분한다.

| State | Meaning |
| --- | --- |
| Pristine | 아직 사용자가 수정하지 않음 |
| Dirty | 사용자가 값을 수정함 |
| Valid | submit 가능한 상태 |
| Invalid | field 또는 form rule을 만족하지 않음 |
| Submitting | submit 진행 중 |
| Submit failed | submit 후 실패 |
| Submit succeeded | submit 후 성공 |

Validation rules:

- Field-level error는 사용자가 수정해야 할 입력과 가까운 곳에 둔다.
- Server/domain validation은 raw API error가 아니라 화면 상태로 변환한다.
- Submit 중 중복 submit을 막는다.
- Client validation은 UX 보조이며 server/domain validation을 대체하지 않는다.

## Verification Notes

UI state 구현 후 변경 범위에 맞춰 아래를 고려한다.

- UI 컴포넌트 test for loading, empty, error, success, disabled, permission states
- Viewmodel test for async success/failure/empty transitions
- Visual snapshot or screenshot verification for important visual states
- Accessibility check for focus, semantics, contrast, disabled controls
- Long text, small screen, large font scale overflow check
- `.agents/skills/design-ui/scripts/verify.ps1`
- Related project test command (예: `flutter test`, `npm test` 등)

## Rules

- 모든 화면이 모든 상태를 반드시 가질 필요는 없지만, 필요한 상태를 의도적으로 제외했는지 확인한다.
- Loading, empty, error, success, disabled, permission 상태를 서로 섞어 숨기지 않는다.
- 사용자에게 보이는 error와 permission 상태는 구현 전 design/architecture 경계를 함께 확인한다.
- 권한, 개인정보, 결제, 데이터 삭제와 연결된 상태 변경은 high-risk로 본다.
- 상태 정의가 API, data, auth contract를 바꾸면 `plan-architecture`로 넘긴다.
