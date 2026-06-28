# API

## Purpose

- 앱이 의존하는 외부 API와 service contract를 기록한다.
- 앱 밖에서 들어오거나 앱 밖으로 나가는 request, response, event, error contract를 기록한다.
- API contract가 DTO, Entity, auth, permission, cache, retry, test에 미치는 영향을 추적한다.
- REST/HTTP API와 Firebase/serverless/service SDK 계약을 같은 문서 안에서 비교 가능하게 유지한다.
- Breaking change, rollback, migration, release risk를 구현 전에 확인한다.

## Read When

- REST/HTTP, Firebase/serverless, external SDK contract를 추가하거나 바꿀 때
- DTO, Entity, mapper, repository, data source가 외부 contract와 연결될 때
- API error, auth requirement, permission, cache, retry, timeout 기준이 필요할 때
- API 변경이 test, release, rollback, migration에 영향을 줄 수 있을 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 외부 contract가 코드/backend 명세/Firebase 또는 service 설정과 충돌함을 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다. 아래 조건은 갱신을 제안해야 하는 신호다.

Update this document when:

- REST/HTTP endpoint, method, path, query, header, request body, response body가 추가, 삭제, 변경된다.
- Firebase Functions, callable function, HTTP function, trigger, input, output, error code가 바뀐다.
- Firestore/Realtime Database collection path, document path, query, write shape, index, listener contract가 바뀐다.
- Cloud Storage path, upload/download/delete rule, metadata, file size/type 제한이 바뀐다.
- Remote Config, feature flag, A/B test key, default value, rollout behavior가 바뀐다.
- External SDK 호출, callback payload, webhook-like event, error handling contract가 바뀐다.
- Auth requirement, permission, privacy, rate limit, retry, timeout, cache behavior가 바뀐다.
- API 변경이 DTO, Entity, mapper, `AppFailure`, test, release, rollback에 영향을 준다.

Do not update this document for:

- API contract와 무관한 UI-only 변경.
- DTO/Entity 내부 구현만 바뀌고 외부 request/response가 바뀌지 않는 변경.
- 단순 구현 로그나 임시 debugging 내용.
- 아직 backend나 service 설정으로 확인되지 않은 추측성 contract.

미확정 API 질문은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 둔다. 확정된 API 결정도 사용자 요청 또는 승인 후 `docs/handoff/decisions.md`에 남긴다.

## Related Docs

| Topic | Source |
| --- | --- |
| DTO, Entity, mapper, storage, migration | `docs/architecture/data-model.md` |
| Auth, permission, privacy, credential handling | `docs/architecture/auth-permissions.md` |
| Layer boundary and data source placement | `docs/architecture/overview.md` |
| Failure mapping and coding rules | `docs/development/conventions.md` |
| API/data/release risk and verification gates | `docs/harness/risk-policy.md`, `docs/harness/quality-gates.md` |

이 문서는 실제 backend나 외부 service의 source of truth를 대체하지 않는다. OpenAPI, Swagger, Firebase rules, console 설정, provider docs가 따로 있으면 그 출처와 확인 일자를 함께 기록한다.

## Common Contract Fields

모든 외부 contract는 플랫폼과 무관하게 아래 항목을 기준으로 기록한다.

| Field | Meaning |
| --- | --- |
| Name | 앱 안에서 부르는 contract 이름 |
| Type | `rest`, `function`, `firestore`, `realtime_db`, `storage`, `remote_config`, `sdk`, `fcm`, 기타 |
| Source | Backend service, Firebase product, external provider, SDK name |
| Input | Request body, query, params, callable input, write payload, config key |
| Output | Response body, document shape, callback payload, file metadata, config value |
| Auth | Required auth, role, owner rule, token requirement |
| Errors | External error/status code and expected app handling |
| Related DTO/Entity | 연결되는 DTO, Entity, mapper |
| Cache/Retry | Cache, pagination, retry, timeout, offline/stale behavior |
| Verification | 필요한 unit/widget/integration/mock/manual verification |

## Contract Index

| Name | Type | Source/Path | Auth | Related DTO/Entity | Status |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Status examples: `planned`, `implemented`, `deprecated`, `breaking-change-pending`, `unknown`.

## REST/HTTP Contracts

자체 backend, external REST API, HTTP endpoint는 이 섹션에 기록한다.

| Name | Method | Path | Auth | Request | Response | Errors | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |  |  |

### REST/HTTP Template

```text
Name:
Base URL/source:
Method:
Path:
Path parameters:
Query parameters:
Headers:
Auth:
Request body:
Response body:
Success status:
Error status/codes:
Rate limit:
Timeout/retry:
Pagination:
Related DTO:
Related Entity:
Related data source:
Verification:
```

## Firebase Functions Contracts

Firebase callable function, HTTP function, scheduled/background trigger를 이 섹션에 기록한다.

| Name | Function Type | Trigger/Endpoint | Auth | Input | Output | Errors | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |  |  |

Function type examples: `callable`, `https`, `scheduled`, `firestore-trigger`, `auth-trigger`, `storage-trigger`.

### Firebase Function Template

```text
Name:
Function type:
Region:
Trigger or endpoint:
Auth/context requirement:
Input payload:
Output payload:
External error codes:
AppFailure mapping:
Idempotency:
Timeout/retry:
Related DTO:
Related Entity:
Related data source:
Verification:
```

## Firebase Data Contracts

Firestore 또는 Realtime Database의 collection, document, query, listener, write contract를 이 섹션에 기록한다.

| Name | Product | Path | Access | Query/Write | Auth/Rule | Related DTO/Entity | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |  |  |

Product examples: `firestore`, `realtime_database`.
Access examples: `get`, `list`, `listen`, `create`, `set`, `update`, `delete`.

### Firebase Data Template

```text
Name:
Product:
Collection/document/path:
Access pattern:
Query/order/filter/limit:
Write payload:
Listener behavior:
Index requirement:
Security rule/auth:
Offline/cache behavior:
Related DTO:
Related Entity:
Related data source:
Verification:
```

## Storage Contracts

Cloud Storage, file upload/download/delete, signed URL, image/document metadata contract를 이 섹션에 기록한다.

| Name | Provider | Path/Bucket | Operation | Auth | Constraints | Metadata | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |  |  |

Operation examples: `upload`, `download`, `delete`, `list`, `metadata`.

## Remote Config And Feature Flags

Remote Config, feature flag, rollout key, app version gate는 이 섹션에 기록한다.

| Key | Provider | Type | Default | Used By | Rollout Risk | Notes |
| --- | --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |  |

Rollout risk examples: `low`, `medium`, `high`.

Remote config rules:

- Default value와 app fallback behavior를 함께 기록한다.
- 앱 시작, 강제 업데이트, 결제, 권한, 개인정보 흐름에 영향을 주면 high-risk로 본다.
- Remote value가 DTO/Entity/storage schema를 바꾸면 `docs/architecture/data-model.md`도 확인한다.

## External SDK Contracts

결제, 로그인, 지도, analytics, crash reporting, push, ads, search 같은 외부 SDK contract를 이 섹션에 기록한다.

| Name | SDK/Provider | Call/Event | Auth/Permission | Input | Output | Errors | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |  |  |

SDK rules:

- Native permission, tracking, analytics, payment, background behavior를 추가하면 사용자 확인이 필요하다.
- SDK error는 viewmodel이나 view에서 직접 파싱하지 않고 data/repository boundary에서 `AppFailure`로 변환한다.
- Provider dashboard 설정, console 설정, signing key, credential은 문서에 secret 값을 기록하지 않는다.

## Error Mapping

외부 error는 사용자에게 그대로 노출하지 않는다. Repository implementation 또는 mapper에서 `AppFailure`로 변환하고, viewmodel이 화면 상태로 변환한다.

| External Error | Source | AppFailure | User Impact | Recovery | Test |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Error rules:

- HTTP status, Firebase error code, SDK exception, timeout, permission-denied를 구분한다.
- Unauthorized, permission-denied, validation error, rate limit, network unavailable, server unavailable은 가능한 별도 failure로 둔다.
- API response body, auth header, token, 개인정보, 결제 정보는 로그에 남기지 않는다.
- Empty success response와 failure response를 구분한다.

## Auth And Permission Notes

API contract에 auth, role, owner check, permission, privacy 영향이 있으면 `docs/architecture/auth-permissions.md` 갱신 필요성을 보고하고, 사용자 요청 또는 승인 후 함께 갱신한다.

| Contract | Auth Requirement | Permission/Role | Privacy Impact | Related Doc |
| --- | --- | --- | --- | --- |
|  |  |  |  |  |

## Breaking Change Policy

Breaking API change는 request/response, path, query, payload, error code, auth requirement가 기존 앱 동작과 호환되지 않게 바뀌는 변경이다.

Breaking change checklist:

- Affected contract:
- Backward compatibility:
- App versions affected:
- DTO/Entity/mapper impact:
- Storage/cache/migration impact:
- Required tests:
- Rollback or mitigation:
- Release/monitoring impact:
- ADR required:

Rollback이 어렵거나 여러 module에 영향을 주는 breaking change는 `docs/architecture/adr/`에 기록한다.

## Verification Notes

API contract 변경 후 변경 범위에 맞춰 아래를 고려한다.

- DTO parsing test
- Mapper unit test
- Repository implementation test with mocked data source
- API client/data source test
- Viewmodel success/error/loading state test
- Firebase emulator or local mock verification when available
- 정적 분석 (예: `flutter analyze`, `eslint` 등)
- 테스트 (예: `flutter test`, `npm test` 등)

## API Rules

- Do not invent API contracts from assumptions.
- Do not change API contracts without updating tests and relevant docs.
- Record auth, permission, or privacy requirements in `docs/architecture/auth-permissions.md`.
- Record DTO, Entity, mapper, storage, or migration impact in `docs/architecture/data-model.md`.
- Record breaking API changes in an ADR when rollback is non-trivial.
- Do not document secrets, credentials, tokens, signing keys, or production service account values.
