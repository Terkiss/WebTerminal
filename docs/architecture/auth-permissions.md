# Auth And Permissions

## Purpose

- 앱의 인증, 권한, 개인정보, token/session, secret/credential 처리 기준을 기록한다.
- 인증 방식과 session/token lifecycle을 명확히 한다.
- 기능별 permission, role, owner rule, failure state를 기록한다.
- 개인정보와 민감정보가 API, data model, local storage, log, test fixture에 어떻게 다뤄지는지 추적한다.
- Secret, credential, signing key, service account 같은 민감 설정값이 코드나 문서에 노출되지 않게 한다.
- Auth, permission, privacy 변경의 test, rollback, release risk를 구현 전에 확인한다.

## Read When

- 로그인, session, token, logout, account deletion 흐름을 다룰 때
- role, owner rule, native permission, route guard, permission-denied state가 필요할 때
- 개인정보, 민감정보, secret, credential, logging 정책을 확인할 때
- auth/privacy 변경이 API, data model, storage, release, rollback에 영향을 줄 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 실제 코드/API/service 설정과 충돌함을 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다. 확인되지 않은 auth provider, 권한 정책, privacy rule, credential 처리 방식은 확정값으로 기록하지 않는다. 아래 조건은 갱신을 제안해야 하는 신호다.

Update this document when:

- Identity provider, login method, session model, token storage, token refresh, logout behavior가 추가되거나 바뀐다.
- Route guard, onboarding guard, permission guard, role/owner rule이 추가되거나 바뀐다.
- API, Firebase rule, backend policy, external SDK permission requirement가 바뀐다.
- 개인정보, 민감정보, user-owned data, 삭제/export/import 대상이 바뀐다.
- Secret, credential, signing key, service account, environment config 처리 방식이 바뀐다.
- Unauthorized, unauthenticated, permission-denied, expired-session, revoked-token failure state가 바뀐다.
- Auth/privacy 변경이 DTO, Entity, local storage, cache, migration, release, monitoring에 영향을 준다.

Do not update this document for:

- Auth, permission, privacy와 무관한 UI-only 변경.
- 실제 정책은 그대로이고 내부 코드만 정리한 refactor.
- 단순 구현 로그나 임시 debugging 내용.
- 사용자 결정, 코드, API/service 설정으로 확인되지 않은 추측성 정책.

미확정 auth/privacy 질문은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 둔다. 확정된 결정도 사용자 요청 또는 승인 후 `docs/handoff/decisions.md`에 남긴다.

## Related Docs

| Topic | Source |
| --- | --- |
| External API/service auth contract | `docs/architecture/api.md` |
| DTO, Entity, local storage, deletion, migration | `docs/architecture/data-model.md` |
| Router guard, layer boundary, DI placement | `docs/architecture/overview.md` |
| Secure coding, logging, failure handling | `docs/development/conventions.md` |
| Auth/data/privacy risk and quality gates | `docs/harness/risk-policy.md`, `docs/harness/quality-gates.md` |

이 문서는 provider console, Firebase rules, backend policy, security policy의 source of truth를 대체하지 않는다. 외부 설정이 따로 있으면 출처와 확인 일자를 기록하되, secret value는 기록하지 않는다.

## Auth Model

| Area | Current Contract | Source | Notes |
| --- | --- | --- | --- |
| Identity provider |  |  |  |
| Login methods |  |  |  |
| Session model |  |  |  |
| Token refresh |  |  |  |
| Logout behavior |  |  |  |
| Account deletion |  |  |  |

Auth model notes:

- Firebase Auth, custom JWT, OAuth, Apple/Google login, anonymous auth, SSO 등 실제 provider는 확인된 경우에만 기록한다.
- Logout은 memory state, local cache, secure storage, push token, analytics identity를 어디까지 해제하는지 기록한다.
- Account deletion, data deletion, re-authentication은 high-risk로 본다.

## Token And Session Storage

| Data | Storage Location | Sensitive | Lifetime | Cleared On Logout | Notes |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Storage rules:

- Access token, refresh token, session credential은 민감정보로 본다.
- 민감 token 저장이 필요하면 secure platform storage 필요 여부를 먼저 확인한다.
- Token, credential, auth header는 source, docs, logs, screenshots, test fixtures에 남기지 않는다.
- Local cache와 secure storage를 구분한다.
- Token refresh 실패, 만료, revoke, logout race condition을 failure state로 고려한다.

## Permission Matrix

| Capability | Actor/Role | Required Auth | Permission Rule | Failure State | Related Contract |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Permission examples:

- `public`: 인증 없이 접근 가능
- `authenticated`: 로그인 사용자만 가능
- `owner`: 본인 resource만 가능
- `admin`: 관리자만 가능
- `device-permission`: camera, photos, location, notification 같은 native permission 필요
- `unknown`: 아직 확인되지 않아 blocking question이 필요한 상태

Permission rules:

- `owner only`, `admin only` 같은 정책은 코드, backend/Firebase rule, 사용자 결정으로 확인된 경우에만 확정 기록한다.
- Native permission, tracking, analytics, background behavior, payment permission은 사용자 확인 없이 추가하지 않는다.
- Permission-denied 상태는 crash나 red screen이 아니라 명시적인 UI state로 처리한다.
- Permission 관련 route redirect는 screen마다 흩뿌리지 않고 `core/router` guard 기준을 따른다.

## Route And Guard Rules

| Route/Flow | Guard | Required State | Redirect/Failure | Notes |
| --- | --- | --- | --- | --- |
|  |  |  |  |  |

Guard rules:

- Auth, onboarding, permission redirect는 `core/router/route_guards.dart` 같은 guard 계층에서 관리한다.
- `viewmodel`은 `BuildContext`를 사용하지 않고 navigation decision을 직접 실행하지 않는다.
- Query/path parameter에서 user id, owner id, role이 들어오면 builder나 guard에서 검증하고 viewmodel에는 필요한 값만 넘긴다.

## Privacy Data Classification

| Data | Classification | Stored Locally | Logged | Export/Delete | Notes |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Classification examples:

- `public`: 공개 가능한 정보
- `internal`: 앱 내부 동작용 정보
- `user-owned`: 사용자가 소유하고 삭제/export 대상이 될 수 있는 정보
- `personal`: 이름, 이메일, 전화번호, 주소, 프로필 정보 등 개인정보
- `sensitive`: 위치, 결제, health, credential, token, private identifier 등 민감정보
- `secret`: API key, signing key, service account, private credential

Privacy rules:

- Personal, sensitive, secret data는 로그, screenshot, fixture, sample data에 남기지 않는다.
- User-owned data의 삭제, export, import 기준은 `docs/architecture/data-model.md`와 함께 확인한다.
- Privacy policy, consent, tracking, analytics에 영향을 주면 high-risk로 보고 사용자 확인이 필요하다.
- 실제 사용자 데이터나 production credential을 test fixture나 문서 예시에 넣지 않는다.

## Secrets And Credentials

| Secret/Credential Type | Location/Source | Owner | Rotation/Revocation | Notes |
| --- | --- | --- | --- | --- |
|  |  |  |  |  |

Secret rules:

- Secret value, token, credential, signing key, service account content는 이 문서에 기록하지 않는다.
- 필요한 경우 secret의 존재, owner, rotation/revocation 절차, storage location 종류만 기록한다.
- Source code, asset, docs, tests, fixtures, screenshots, logs에 secret을 넣지 않는다.
- External service 설정, signing, credential 변경은 사용자 확인 없이 하지 않는다.
- 유출 의심이 있으면 값을 재사용하지 않고 rotation/revocation 필요성을 보고한다.

## Failure States

| Failure | Source | AppFailure | User State | Recovery | Test |
| --- | --- | --- | --- | --- | --- |
| unauthenticated |  |  |  |  |  |
| unauthorized |  |  |  |  |  |
| permission-denied |  |  |  |  |  |
| expired-session |  |  |  |  |  |
| revoked-token |  |  |  |  |  |

Failure rules:

- External auth/permission error는 repository implementation 또는 mapper에서 `AppFailure`로 변환한다.
- View는 raw exception, token, auth response body를 직접 다루지 않는다.
- User-visible message는 raw external error message가 아니라 화면 상태로 변환된 값을 사용한다.
- Login required, session expired, permission denied, account disabled, re-auth required는 가능한 별도 state로 구분한다.

## Blocking Questions

Auth/privacy 관련 blocking question은 답변 전 구현하면 보안이나 개인정보 정책을 추측하게 되는 질문이다.

Blocking examples:

- 현재 인증 방식은 무엇인가?
- 어떤 route나 capability가 login required인가?
- 본인 resource만 접근 가능한가, admin/staff 역할이 있는가?
- Token/session은 어디에 저장하고 logout 시 어디까지 삭제하는가?
- 어떤 필드가 개인정보 또는 민감정보인가?
- 계정 삭제, 데이터 삭제, export/import가 필요한가?
- Firebase rules, backend permission, external SDK permission의 source of truth는 어디인가?

답이 없으면 `docs/harness/prompt-routing.md`의 Blocking Questions flow에 따라 사용자에게 먼저 질문한다.

## Verification Notes

Auth/permission/privacy 변경 후 변경 범위에 맞춰 아래를 고려한다.

- Auth state transition test
- Permission-denied failure path test
- Token refresh/logout behavior test
- Repository/data source auth error mapping test
- Viewmodel unauthorized/permission/error state test
- Route guard redirect test
- Secure storage behavior test when applicable
- Log/fixture/screenshot secret exposure check
- 정적 분석 (예: `flutter analyze`, `eslint` 등)
- 테스트 (예: `flutter test`, `npm test` 등)

## Rules

- Auth, permission, privacy, token, credential, payment, data deletion changes are high-risk.
- Do not invent auth, role, owner, token, privacy, or credential policies from assumptions.
- Ask blocking questions before implementation when auth/privacy policy is unclear.
- Use secure platform storage for sensitive tokens when required.
- Do not expose secret, credential, token, auth header, service account, signing key, or production config values.
- Record API auth requirements in `docs/architecture/api.md`.
- Record DTO, Entity, storage, deletion, export, import, or migration impact in `docs/architecture/data-model.md`.
- Add tests for auth state transitions, permission-denied states, token/session failure paths, and privacy-sensitive data handling.
