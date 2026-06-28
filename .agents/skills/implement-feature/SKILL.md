---
name: implement-feature
description: Implement project code, features, bug fixes, refactors, configuration changes, dependencies, state management, API calls, and UI code while following existing project patterns. Use when requirements are clear enough to edit code; do not use for product planning, UX-only review, architecture decisions, test-only work, or release readiness unless code changes are required.
---

# Implement Feature

프로젝트 코드 변경, 설정 수정, 리팩터링, 기능 구현에 사용한다. 제품, 디자인, 아키텍처 결정이 실제 코드로 바뀌는 지점의 작업을 담당한다.

## Use When

- 프로젝트 코드 기능 구현, bug fix, refactor, config change를 수행할 때.
- 제품/디자인/아키텍처 결정이 구현 가능한 작은 작업 단위로 정리됐을 때.
- 기존 코드 패턴을 따라 파일을 수정하고 검증까지 연결해야 할 때.
- UI 변경, 상태 관리 변경, API 호출 변경이 코드에 반영되어야 할 때.

## Context Loading

먼저 사용자 요청, 현재 변경사항, 관련 코드 파일을 확인한다. 아래 문서는 조건이 맞을 때만 읽는다.

- setup이나 환경 전제가 필요할 때: `docs/development/setup.md`
- 실행, 분석, 테스트 명령이 필요할 때: `docs/development/commands.md`
- naming, folder, state, DI, error handling 규칙이 필요할 때: `docs/development/conventions.md`
- 테스트 추가나 검증 범위가 필요할 때: `docs/development/testing.md`
- 아키텍처 패턴(예: MVVM, Clean Architecture 등), module boundary가 영향을 받을 때: `docs/architecture/overview.md`
- API, data, auth, storage가 바뀔 때: `docs/architecture/api.md`, `docs/architecture/data-model.md`, `docs/architecture/auth-permissions.md`
- UI code, component, visual style이 바뀔 때: `docs/design/design-system.md`
- navigation이나 화면 흐름이 바뀔 때: `docs/design/screen-flows.md`
- loading, empty, error, success, disabled 상태가 바뀔 때: `docs/design/states.md`
- product scope나 acceptance criteria가 불명확할 때: `docs/product/mvp-scope.md`, `docs/handoff/decisions.md`, `docs/handoff/open-questions.md`
- 완료 전 구현 검증이 필요할 때: `.agents/skills/implement-feature/scripts/verify.ps1`

모든 문서를 한 번에 읽지 않는다. 먼저 주변 코드를 보고, 부족한 결정이나 규칙에 해당하는 문서만 읽는다.

## Workflow

1. 요청을 implementation task type으로 분류한다: `feature`, `bugfix`, `refactor`, `config`, `dependency`, `ui-code`.
2. 현재 git/worktree 상태와 사용자 변경사항을 확인한다.
3. 관련 코드의 기존 패턴, 상태 관리, 테스트 방식을 읽는다.
4. 변경 범위를 가장 작은 안전한 단위로 잡는다.
5. 코드를 수정하고 필요한 테스트 또는 fixture를 갱신한다.
6. UI 변경이면 상태와 visual verification 필요성을 확인한다.
7. 변경 종류에 맞는 검증을 실행하거나, 실행 불가 사유와 대체 검증을 남긴다.
8. 완료 전 `.agents/skills/implement-feature/scripts/verify.ps1`을 실행하거나, 실행하지 못한 이유를 남긴다.

## Decision Rules

- 새 abstraction을 만들기 전에 기존 프로젝트 패턴을 우선한다.
- 변경 범위를 요청에 맞게 좁게 유지한다.
- 사용자의 기존 변경을 되돌리지 않는다.
- 동작이 바뀌면 테스트를 갱신하거나 검증 필요성을 명시한다.
- 인증, 권한, 결제, 개인정보, 데이터 삭제, migration, 배포 설정 변경은 high-risk로 보고 `AGENTS.md`의 Risk Policy와 Safety Policy를 따른다.
- 관련 검증 또는 실행하지 못한 이유 없이 기능 작업을 완료로 보고하지 않는다.

## Documentation Updates

문서 갱신이 필요하면 먼저 필요성을 보고하고, 사용자 요청 또는 승인 후 갱신한다.

- 개발 명령 변경: `docs/development/commands.md`
- 코드 컨벤션 변경: `docs/development/conventions.md`
- 테스트 전략 변경: `docs/development/testing.md`
- 아키텍처 영향: `docs/architecture/`
- 디자인 시스템 영향: `docs/design/design-system.md`
- 미완료 상태/다음 액션: `docs/handoff/`

단순 코드 변경에서 문서를 과도하게 갱신하지 않는다. 반복해서 참조해야 할 규칙이나 high-risk 결정은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 문서화한다.

## Handoff

- 제품 범위가 불명확하면 `plan-product`로 되돌린다.
- UI 흐름/상태가 불명확하면 `design-ui`로 넘긴다.
- API, 데이터, 권한 구조가 불명확하면 `plan-architecture`로 넘긴다.
- 동작 변경이 있으면 `verify-change`를 포함한다.
- release/rollback 영향이 있으면 `prepare-release`를 포함한다.

## Completion Output

최종 응답에는 필요한 범위 안에서 다음을 포함한다.

- changed: 수정한 코드/설정 영역.
- behavior: 바뀐 동작.
- verification: 실행한 analyze/test/script와 결과.
- skipped: 실행하지 못한 검증과 이유.
- handoff: 추가로 필요한 design/architecture/test/release 작업.
- risk: 남은 위험과 주의할 점.
- verification script: 실행한 local script와 결과 또는 미실행 이유.
