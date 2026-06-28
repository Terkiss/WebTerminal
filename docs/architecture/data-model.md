# Data Model

## Purpose

- 앱에서 실제로 사용하는 Entity, DTO, 저장소, cache, migration 영향을 기록한다.
- 앱별 domain data의 source of truth를 기록한다.
- Entity와 DTO의 관계, mapper, repository boundary를 추적한다.
- Local storage, cache, migration, 삭제/복구 기준을 기록한다.
- 데이터 구조 변경 시 구현, 테스트, rollback 영향을 확인한다.

## Read When

- Entity, DTO, mapper, repository, data source가 추가되거나 바뀔 때
- API/service payload와 앱 내부 data structure의 관계를 확인할 때
- local storage, cache, migration, deletion, recovery 기준이 필요할 때
- 데이터 변경이 AppFailure, permission/privacy, test, rollback에 영향을 줄 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 실제 코드/API contract/storage schema와 충돌함을 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다. 아래 조건은 갱신을 제안해야 하는 신호다.

Update this document when:

- Entity가 추가, 삭제, rename되거나 key field, required field, nullable field, derived field가 바뀐다.
- DTO, API response/request, platform SDK payload, local storage payload가 Entity mapping에 영향을 줄 정도로 바뀐다.
- Mapper behavior가 바뀌어 field 변환, default value, null handling, validation, failure mapping이 달라진다.
- Domain repository interface, repository implementation, data source boundary가 새로 생기거나 의미 있게 바뀐다.
- Local storage mechanism, cache key, schema version, retention, invalidation 기준이 바뀐다.
- Migration, data deletion, import/export, recovery path, rollback 기준이 생기거나 바뀐다.
- 데이터 변경으로 새로운 `AppFailure`, permission/privacy rule, test strategy가 필요해진다.

Do not update this document for:

- 단순 구현 로그나 일회성 작업 진행 상황.
- UI-only 변경처럼 data contract, storage, mapper에 영향이 없는 변경.
- 내부 refactor지만 Entity/DTO/repository/storage 계약이 바뀌지 않는 변경.
- 아직 사용자 결정이나 코드로 확인되지 않은 추측성 데이터 구조.

미확정 데이터 결정은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 둔다. 확정된 결정도 사용자 요청 또는 승인 후 `docs/handoff/decisions.md`에 남긴다.

## Related Docs

| Topic | Source |
| --- | --- |
| Layer boundary, `data/`, `domain/`, `view/` 구조 | `docs/architecture/overview.md` |
| DTO/Entity naming, Result boundary, generated code rules | `docs/development/conventions.md` |
| API request/response contract | `docs/architecture/api.md` |
| Auth, permission, privacy data handling | `docs/architecture/auth-permissions.md` |
| Migration, deletion, release risk | `docs/harness/risk-policy.md`, `docs/harness/quality-gates.md` |

이 문서는 위 규칙을 다시 정의하지 않는다. 실제 앱에서 확인된 데이터 항목과 변경 이력을 기록한다.

## Entities

| Entity | Feature | Key Fields | Source | Persistence | Notes |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

### Entity Template

새 Entity를 추가하거나 주요 필드가 바뀌면 아래 항목을 기록한다.

```text
Entity:
Feature:
Purpose:
Identity/key:
Required fields:
Nullable fields:
Derived fields:
Validation/domain rules:
Created from DTO:
Persisted in:
Deletion/privacy notes:
Related tests:
```

## DTO And Mapping

DTO는 API, remote service, local storage, platform SDK 같은 외부 입출력 구조를 표현한다. View는 DTO를 직접 사용하지 않는다.

| DTO | External Source | Maps To Entity | Mapper | Notes |
| --- | --- | --- | --- | --- |
|  |  |  |  |  |

Mapping rules:

- DTO는 `data/{feature}/dto/`에 둔다.
- Mapper는 `data/{feature}/mapper/`에 둔다.
- Repository implementation은 DTO를 Entity로 변환해 domain boundary로 넘긴다.
- API contract가 바뀌면 `docs/architecture/api.md`도 함께 확인한다.
- DTO와 Entity의 필드와 책임이 완전히 같다면 중복 class 생성이 필요한지 먼저 검토한다.
- DTO parsing 실패, schema mismatch, external exception은 `AppFailure`로 변환할 수 있어야 한다.

## Repository And Data Source Boundary

| Domain Repository | Implementation | Data Source | Return Contract | Side Effects |
| --- | --- | --- | --- | --- |
|  |  |  |  |  |

Boundary rules:

- Domain repository interface는 `domain/{feature}/repository/`에 둔다.
- Repository implementation은 `data/{feature}/repository/`에 둔다.
- Data source는 외부 API, Firebase, database, local storage, platform SDK 접근을 담당한다.
- Repository implementation은 external exception을 catch하고 `Result<Entity>` 또는 `Result<List<Entity>>`로 변환한다.
- 단순 pass-through가 아니거나 business flow가 있으면 `domain/{feature}/use_case/`를 둔다.

## Result And Failure Boundary

실패 가능한 외부 작업은 repository interface 또는 use case에서 `Result<T>`로 표현한다.

```text
data_source -> DTO/raw data or external exception
repository_impl -> AppFailure mapping -> Result<Entity>
use_case -> Result<T> when fallible
viewmodel -> AsyncValue or screen-specific state
view -> rendered state
```

Data model 변경 시 다음을 확인한다.

- 새 실패 유형이 `AppFailure`에 필요한가?
- DTO parsing 실패와 empty data를 구분하는가?
- 삭제, 권한 없음, 인증 만료, migration 실패가 별도 failure로 필요한가?
- View에 raw exception, DTO, `Result<T>`가 노출되지 않는가?

## Local Storage

| Data | Storage Mechanism | Key/Path | Schema Version | Sensitive | Invalidation |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Storage rules:

- Sensitive data 저장이 필요하면 secure platform storage 필요 여부를 먼저 확인한다.
- Secret, token, credential, 개인정보, 결제 정보는 로그, fixture, screenshot, 문서에 노출하지 않는다.
- Cache key, expiration, invalidation 기준을 기록한다.
- Offline 또는 stale data 허용 여부를 명시한다.
- Local schema가 바뀌면 migration 필요 여부를 판단한다.

## Migration Notes

Migration, data deletion, import/export는 high-risk로 다룬다.

| Version | Change | Forward Migration | Rollback/Recovery | Test Evidence | Status |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Migration checklist:

- Current schema/version:
- Target schema/version:
- Backward compatibility:
- Data loss possibility:
- Rollback or recovery path:
- Test data and migration test:
- User confirmation required:
- Release/monitoring impact:

데이터 삭제 또는 되돌리기 어려운 migration은 rollback/recovery 기준 없이 완료로 보지 않는다.

## Data Lifecycle

| Data | Created By | Updated By | Deleted By | Retention | Export/Import |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

Lifecycle rules:

- 데이터 생성, 수정, 삭제 주체를 기록한다.
- User-owned data와 app/internal data를 구분한다.
- 삭제 요청이 실제 storage, cache, remote source에 어떻게 반영되는지 기록한다.
- 개인정보나 계정 삭제와 연결되면 `docs/architecture/auth-permissions.md` 갱신 필요성을 보고하고, 사용자 요청 또는 승인 후 함께 갱신한다.

## Data Rules

- Data deletion, import, export, and migration are high-risk.
- Sensitive data must not be logged, committed, or captured in fixtures/screenshots.
- DTO is external boundary data; Entity is app-internal domain data.
- Repository implementation owns DTO-to-Entity mapping.
- Viewmodel consumes `Result<T>`; View does not consume DTO or `Result<T>`.
- Update tests when model shape, mapper behavior, storage schema, or migration changes.

## Verification Notes

Data model 변경 후 변경 범위에 맞춰 아래를 고려한다.

- Mapper unit test
- Repository implementation test with data source mock
- Use case test when domain rule exists
- Viewmodel async/error state test
- Local storage migration test
- 정적 분석 (예: `flutter analyze`, `eslint` 등)
- 테스트 (예: `flutter test`, `npm test` 등)
