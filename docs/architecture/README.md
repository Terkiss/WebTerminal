# Architecture Docs

앱 구조, API/service contract, data model, auth/permission, ADR 기준을 기록한다. 구현이 기술 구조나 high-risk 경계에 영향을 줄 때 참고한다.

## Document Index

| Document | Role |
| --- | --- |
| `overview.md` | `lib/core`, `data`, `domain`, `view`, `widgets` 구조와 layer boundary, DI, router, state management 기준을 기록한다. |
| `api.md` | REST, Firebase/serverless, storage, remote config, external SDK contract와 error/auth 영향을 기록한다. |
| `data-model.md` | Entity, DTO, mapper, repository/data source boundary, storage, migration, lifecycle을 기록한다. |
| `auth-permissions.md` | auth model, token/session, permission matrix, privacy, secret/credential, failure state를 기록한다. |
| `adr/README.md` | ADR을 언제 작성하는지, numbered ADR template과 기록 기준을 정의한다. |

## Rules

- 구조, API, data, auth 결정은 사용자 결정이나 코드/contract 확인 없이 확정하지 않는다.
- 되돌리기 어렵거나 반복 참조될 architecture decision은 ADR 필요 여부를 판단한다.
- Auth, privacy, data deletion, migration, secret, release 영향은 high-risk로 본다.
