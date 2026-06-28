---
name: plan-product
description: Plan or refine app/project product direction, including problem definition, target users, user journeys, MVP scope, out-of-scope items, success metrics, roadmap, open questions, options, prioritization, and handoff decisions. Use before design or implementation when product intent, scope, users, or success criteria are unclear; do not use for straightforward code, test, UI, or release tasks with clear requirements.
---

# Plan Product

제품 문제, 대상 사용자, MVP 범위, 성공 지표, 로드맵을 명확히 할 때 사용한다.
구현이나 디자인으로 넘어가기 전에 "무엇을, 누구에게, 왜, 어느 범위까지" 만들지 정리한다.

## Use When

- 문제 정의, 사용자 세그먼트, user journey, MVP 범위, success metric, roadmap을 만들거나 수정할 때.
- 기능 요청이 들어왔지만 목표 사용자, 해결 문제, 성공 기준, 제외 범위가 불명확할 때.
- 제품 결정이 디자인, 아키텍처, 구현 범위에 영향을 줄 때.
- open question, decision, next action을 handoff 문서에 남겨야 할 때.

## Context Loading

먼저 사용자 요청과 현재 대화 맥락을 확인한다. 아래 문서는 조건이 맞을 때만 읽는다.

- 프로젝트 배경이 필요할 때: `docs/project/overview.md`
- 용어가 불명확할 때: `docs/project/glossary.md`
- 제약이 제품 범위에 영향을 줄 때: `docs/project/constraints.md`
- 문제 정의가 필요할 때: `docs/product/problem.md`
- 사용자/페르소나가 필요할 때: `docs/product/target-users.md`
- MVP, 우선순위, 제외 범위가 필요할 때: `docs/product/mvp-scope.md`
- 성공 기준이나 측정이 필요할 때: `docs/product/success-metrics.md`
- 일정, 단계, 후속 범위가 필요할 때: `docs/product/roadmap.md`
- 이어받기나 미완료 맥락이 필요할 때: `docs/handoff/current-state.md`, `docs/handoff/decisions.md`, `docs/handoff/open-questions.md`, `docs/handoff/next-actions.md`
- 완료 전 제품 문서 검증이 필요할 때: `.agents/skills/plan-product/scripts/verify.ps1`

모든 제품 문서를 한 번에 읽지 않는다. 요청의 핵심 질문과 직접 연결된 문서부터 읽는다.

## Workflow

1. 요청을 product task type으로 분류한다: `problem`, `user`, `scope`, `metric`, `roadmap`, `decision`, `handoff`.
2. 현재 확인된 사실, 사용자 가정, 미확정 질문을 분리한다.
3. 구현 해결책보다 먼저 사용자 문제와 desired outcome을 명확히 한다.
4. MVP 범위는 `must`, `should`, `could`, `out-of-scope`로 나눈다.
5. 제품 결정은 success metric 또는 학습 목표와 연결한다.
6. 결정이 디자인, API, 데이터, 인증, 결제, 개인정보, 배포에 영향을 주는지 확인한다.
7. 문서 갱신 필요성을 보고하고, 사용자 요청 또는 승인 후 필요한 문서와 handoff 문서에 반영한다.
8. 다음 단계에 필요한 skill을 지정한다.
9. 완료 전 `.agents/skills/plan-product/scripts/verify.ps1`을 실행하거나, 실행하지 못한 이유를 남긴다.

## Decision Rules

- 사용자 문제와 대상 사용자가 불명확하면 기능 구현으로 바로 넘어가지 않는다.
- MVP 범위는 명시적으로 좁힌다. 조용한 scope expansion을 피한다.
- 성공 지표는 가능하면 관찰 가능한 행동이나 결과로 쓴다.
- 시장, 법률, 플랫폼 정책, 가격, 외부 서비스 상태처럼 변할 수 있는 사실은 확인 없이 확정하지 않는다.
- 인증, 권한, 결제, 개인정보, 데이터 삭제, 배포 정책에 영향을 주는 제품 결정은 high-risk로 보고 `AGENTS.md`의 Risk Policy와 Safety Policy를 따른다.
- 제품 문서만 바꾸더라도 실제 제품 동작이나 운영 정책이 바뀌면 risk를 다시 판단한다.

## Documentation Updates

문서 갱신이 필요하면 먼저 필요성을 보고하고, 사용자 요청 또는 승인 후 갱신한다.

- 문제 정의 변경: `docs/product/problem.md`
- 대상 사용자 변경: `docs/product/target-users.md`
- MVP 범위 변경: `docs/product/mvp-scope.md`
- 성공 지표 변경: `docs/product/success-metrics.md`
- 로드맵 변경: `docs/product/roadmap.md`
- 확정된 결정: `docs/handoff/decisions.md`
- 미확정 질문: `docs/handoff/open-questions.md`
- 다음 액션: `docs/handoff/next-actions.md`

단순 답변이나 브레인스토밍 요청에서는 문서를 과도하게 갱신하지 않는다.

## Handoff

- UI 흐름, 화면 상태, interaction이 필요하면 `design-ui`로 넘긴다.
- API, 데이터 모델, 인증/권한, 저장 구조 판단이 필요하면 `plan-architecture`로 넘긴다.
- 구현 가능한 작은 기능 단위가 명확하면 `implement-feature`로 넘긴다.
- 검증 기준이나 테스트 전략이 제품 성공 기준과 연결되어야 하면 `verify-change`를 함께 고려한다.
- 릴리스, rollout, rollback, monitoring 기준이 제품 결정에 포함되면 `prepare-release` 또는 `operate-app`으로 넘긴다.

## Completion Output

최종 응답에는 필요한 범위 안에서 다음을 포함한다.

- product decision: 확정한 문제, 사용자, 범위, 지표.
- docs changed: 갱신한 제품/handoff 문서.
- open questions: 아직 답이 필요한 질문.
- handoff: 다음에 사용할 skill 또는 작업 흐름.
- risk: 제품 결정이 만드는 risk와 남은 불확실성.
- verification script: 실행한 local script와 결과 또는 미실행 이유.
