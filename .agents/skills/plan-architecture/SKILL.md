---
name: plan-architecture
description: Plan or review app/project architecture, module boundaries, API contracts, data models, authentication, authorization, privacy, storage, migrations, security, dependency boundaries, and ADRs. Use when implementation depends on technical structure, API/data/auth decisions, or high-risk privacy/security choices; do not use for straightforward code edits that fit existing architecture.
---

# Plan Architecture

앱 아키텍처, 모듈 경계, API 계약, 데이터 모델, 인증, 권한, 저장소, ADR을 명확히 할 때 사용한다. 구현 전에 되돌리기 비싼 기술 결정을 정리한다.

## Use When

- 앱 구조, 모듈 경계, API contract, data model, storage, auth, permission을 만들거나 바꿀 때.
- 제품/디자인 요구가 구현 전에 기술 구조 판단을 요구할 때.
- migration, 개인정보, 권한, 보안 정책처럼 high-risk 기술 결정이 있을 때.
- ADR로 남겨야 할 되돌리기 비싼 결정이 있을 때.

## Context Loading

먼저 사용자 요청, 관련 코드 구조, 제품/디자인 요구를 확인한다. 아래 문서는 조건이 맞을 때만 읽는다.

- 프로젝트 제약이 구조 결정에 영향을 줄 때: `docs/project/constraints.md`
- 아키텍처 패턴(예: MVVM, Clean Architecture 등), module boundary가 필요할 때: `docs/architecture/overview.md`
- API contract, endpoint, request/response가 필요할 때: `docs/architecture/api.md`
- entity, DTO, storage, migration이 필요할 때: `docs/architecture/data-model.md`
- auth, permission, privacy, credential 경계가 필요할 때: `docs/architecture/auth-permissions.md`
- 기존 결정이나 새 구조 결정이 필요할 때: `docs/architecture/adr/`
- 코드 규칙이나 폴더 구조가 함께 바뀔 때: `docs/development/conventions.md`
- 테스트 전략이 구조 결정에 영향을 줄 때: `docs/development/testing.md`
- 완료 전 아키텍처 검증이 필요할 때: `.agents/skills/plan-architecture/scripts/verify.ps1`

모든 아키텍처 문서를 한 번에 읽지 않는다. API, data, auth, module boundary 중 요청과 직접 연결된 문서부터 읽는다.

## Workflow

1. 요청을 architecture task type으로 분류한다: `structure`, `api`, `data`, `auth`, `storage`, `migration`, `adr`.
2. 제품/디자인 요구와 기술 결정의 연결을 명확히 한다.
3. 변경 범위와 module boundary를 확인한다.
4. API, data, auth, permission 영향과 backward compatibility를 검토한다.
5. 필요한 테스트, rollback, migration 우려를 식별한다.
6. 되돌리기 비싼 결정은 ADR 생성 또는 갱신 대상으로 둔다.
7. 구현 가능한 단위와 검증 기준을 handoff한다.
8. 완료 전 `.agents/skills/plan-architecture/scripts/verify.ps1`을 실행하거나, 실행하지 못한 이유를 남긴다.

## Decision Rules

- cross-cutting 결정은 구현 전에 명시한다.
- API, 데이터, 권한 변경은 제품 요구와 추적 가능해야 한다.
- 인증, 권한, 개인정보, 삭제, migration, secret/credential은 high-risk로 다룬다.
- migration이나 데이터 삭제는 rollback 또는 recovery 기준 없이 완료로 보지 않는다.
- 기존 아키텍처와 코드 스타일을 우선하고, 새 abstraction은 실제 복잡도를 줄일 때만 추가한다.
- 변경이 release, production config, rollout에 영향을 주면 `prepare-release`를 함께 고려한다.

## Documentation Updates

문서 갱신이 필요하면 먼저 필요성을 보고하고, 사용자 요청 또는 승인 후 갱신한다.

- 아키텍처 개요 변경: `docs/architecture/overview.md`
- API 변경: `docs/architecture/api.md`
- 데이터 모델 변경: `docs/architecture/data-model.md`
- 인증/권한 변경: `docs/architecture/auth-permissions.md`
- 구조적 결정: `docs/architecture/adr/`
- 테스트 영향: `docs/development/testing.md`
- 미확정 질문: `docs/handoff/open-questions.md`
- 다음 액션: `docs/handoff/next-actions.md`

ADR은 되돌리기 비싸거나 팀이 반복해서 참조할 결정에만 작성한다.

## Handoff

- 구현 가능한 단위가 정리되면 `implement-feature`로 넘긴다.
- 검증 전략이나 테스트 범위가 중요하면 `verify-change`를 함께 고려한다.
- UI 흐름이나 상태 정의가 바뀌면 `design-ui`로 넘긴다.
- release, migration, rollback, monitoring이 필요하면 `prepare-release` 또는 `operate-app`으로 넘긴다.

## Completion Output

최종 응답에는 필요한 범위 안에서 다음을 포함한다.

- architecture decision: 확정한 구조, API, 데이터, 권한 결정.
- docs changed: 갱신한 architecture/ADR/handoff 문서.
- implementation handoff: 구현 단위와 주의할 경계.
- verification need: 필요한 테스트, migration 검증, rollback 확인.
- risk: high-risk 여부와 남은 불확실성.
- verification script: 실행한 local script와 결과 또는 미실행 이유.
