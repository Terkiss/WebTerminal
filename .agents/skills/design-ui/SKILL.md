---
name: design-ui
description: Design or review app UX/UI changes, including screens, flows, layouts, buttons, interaction states, loading/empty/error/disabled states, accessibility, navigation, responsive behavior, visual verification, and design-system alignment. Use when user-visible UI/UX behavior may change or needs review; do not use for code-only changes with no visual, interaction, or accessibility impact.
---

# Design UI

화면 흐름, 레이아웃, 시각 상태, 상호작용, 접근성, 디자인 시스템을 명확히 할 때 사용한다. 구현 전에 사용자가 보게 될 경험과 상태를 먼저 정리한다.

## Use When

- UI/UX, navigation, responsive layout, interaction, accessibility, visual state를 만들거나 수정할 때.
- 화면이 empty, loading, error, success, disabled, permission 상태를 가져야 할 때.
- 제품 범위는 정해졌지만 화면 흐름이나 사용자 행동 경로가 불명확할 때.
- 사용자에게 보이는 변경이라 visual verification 여부를 판단해야 할 때.

## Context Loading

먼저 사용자 요청과 관련 화면/컴포넌트 코드를 확인한다. 아래 문서는 조건이 맞을 때만 읽는다.

- 사용자 맥락이 필요할 때: `docs/product/target-users.md`
- 제품 범위나 우선순위가 UI 결정에 영향을 줄 때: `docs/product/mvp-scope.md`
- UX 판단 기준이 필요할 때: `docs/design/ux-principles.md`
- 시각/UI 판단 기준이 필요할 때: `docs/design/ui-principles.md`
- 컴포넌트, 색상, 타이포, 간격 기준이 필요할 때: `docs/design/design-system.md`
- 화면 전환, navigation, user journey가 필요할 때: `docs/design/screen-flows.md`
- loading, empty, error, success, disabled 상태가 필요할 때: `docs/design/states.md`
- 프로젝트 제약이 UI에 영향을 줄 때: `docs/project/constraints.md`
- 완료 전 UI 검증이 필요할 때: `.agents/skills/design-ui/scripts/verify.ps1`

모든 디자인 문서를 한 번에 읽지 않는다. 화면 흐름, 상태, 컴포넌트 중 요청과 직접 연결된 문서부터 읽는다.

## Workflow

1. 요청을 design task type으로 분류한다: `flow`, `screen`, `component`, `state`, `accessibility`, `visual-check`.
2. 기존 앱 관례와 디자인 시스템을 확인한다.
3. 사용자 목표와 화면의 primary action을 정한다.
4. 필요한 상태를 정의한다: empty, loading, error, success, disabled, permission.
5. 화면 크기, 입력 방식, 접근성 제약을 함께 확인한다.
6. 새 데이터, API, 권한, 저장소 요구가 생기는지 확인한다.
7. 문서 갱신 필요성을 보고하고, 사용자 요청 또는 승인 후 반영하며 구현/검증 handoff를 정한다.
8. 완료 전 `.agents/skills/design-ui/scripts/verify.ps1`을 실행하거나, 실행하지 못한 이유를 남긴다.

## Decision Rules

- 사용자가 redesign을 요청하지 않았다면 기존 앱 관례를 보존한다.
- UI는 도메인 작업을 빠르게 수행하게 해야 하며, 일반적인 landing page 구성을 기본값으로 삼지 않는다.
- 화면 상태가 사용자 행동을 막거나 오해하게 만들면 상태 정의를 먼저 보강한다.
- UI copy/style만 바꾸는 tiny/low 작업은 문서 갱신 없이 처리할 수 있다.
- 화면 흐름, 접근성, 권한 상태, error handling이 바뀌면 `verify-change`를 함께 고려한다.
- UI 변경이 결제, 개인정보, 권한, 데이터 삭제에 영향을 주면 high-risk로 보고 `AGENTS.md`의 Risk Policy와 Safety Policy를 따른다.

## Documentation Updates

문서 갱신이 필요하면 먼저 필요성을 보고하고, 사용자 요청 또는 승인 후 갱신한다.

- UX 원칙 변경: `docs/design/ux-principles.md`
- UI 원칙 변경: `docs/design/ui-principles.md`
- 디자인 시스템 변경: `docs/design/design-system.md`
- 화면 흐름 변경: `docs/design/screen-flows.md`
- 상태 정의 변경: `docs/design/states.md`
- 미확정 질문: `docs/handoff/open-questions.md`
- 다음 액션: `docs/handoff/next-actions.md`

단순 구현 보조나 작은 스타일 변경에서는 문서를 과도하게 갱신하지 않는다.

## Handoff

- 새 데이터, API, 권한, 저장소 판단이 필요하면 `plan-architecture`로 넘긴다.
- 구현 가능한 UI 변경이면 `implement-feature`로 넘긴다.
- 사용자에게 보이는 UI 변경이면 `verify-change`에서 visual verification 또는 `.agents/skills/design-ui/scripts/verify.ps1`을 고려한다.
- 제품 범위나 사용자 문제가 다시 흔들리면 `plan-product`로 되돌린다.

## Completion Output

최종 응답에는 필요한 범위 안에서 다음을 포함한다.

- design decision: 확정한 흐름, 화면, 상태, 컴포넌트 기준.
- docs changed: 갱신한 design/handoff 문서.
- verification need: 필요한 visual verification 또는 UI 검증.
- handoff: 다음에 사용할 skill 또는 구현 흐름.
- risk: UI 결정이 만드는 risk와 남은 불확실성.
- verification script: 실행한 local script와 결과 또는 미실행 이유.
