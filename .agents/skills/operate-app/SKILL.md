---
name: operate-app
description: Handle app/project operations, monitoring, incidents, alerts, logs, support issues, user feedback loops, post-release follow-up, production impact, rollback handoff, and support-driven product follow-up. Use when diagnosing or organizing operational facts and next actions; do not use for ordinary feature implementation, test writing, or planned release preparation without operational feedback.
---

# Operate App

모니터링, 장애, 사용자 feedback loop, release 이후 follow-up, support workflow에 사용한다. 운영 중 관찰된 사실을 제품/기술 후속 작업으로 연결한다.

## Use When

- monitoring, incident, support issue, user feedback, post-release follow-up을 다룰 때.
- 운영 사실, 진단, 결정, next action을 분리해야 할 때.
- rollback, hotfix, production config 변경 가능성이 있을 때.
- feedback을 product/design/architecture/implementation 작업으로 연결해야 할 때.

## Context Loading

먼저 사용자 요청, 운영 증상, 로그/모니터링 요약, 최근 release context를 확인한다. 아래 문서는 조건이 맞을 때만 읽는다.

- 지표, 로그, alert 기준이 필요할 때: `docs/operations/monitoring.md`
- incident 분류, escalation, timeline이 필요할 때: `docs/operations/incident-playbook.md`
- rollback이나 recovery 판단이 필요할 때: `docs/operations/rollback.md`
- 최근 release 맥락이 필요할 때: `docs/operations/release-checklist.md`
- 현재 상태가 필요할 때: `docs/handoff/current-state.md`
- 과거 결정 근거가 필요할 때: `docs/handoff/decisions.md`
- 미해결 질문이 필요할 때: `docs/handoff/open-questions.md`
- 다음 액션이 필요할 때: `docs/handoff/next-actions.md`
- 사용자 피드백이 제품 후속 작업으로 이어질 때: `docs/product/problem.md`, `docs/product/roadmap.md`, `docs/product/success-metrics.md`
- 완료 전 운영 문서 검증이 필요할 때: `.agents/skills/operate-app/scripts/verify.ps1`

모든 운영 문서를 한 번에 읽지 않는다. incident, monitoring, rollback, feedback 중 요청과 직접 연결된 문서부터 읽는다.

## Workflow

1. 요청을 operations task type으로 분류한다: `monitoring`, `incident`, `feedback`, `support`, `post-release`, `rollback`.
2. 관찰된 사실, 진단, 추정, 결정을 분리한다.
3. 영향 범위와 사용자 impact를 확인한다.
4. high-risk production 변경이 필요한지 판단한다.
5. 즉시 조치, 후속 분석, 제품/기술 backlog를 분리한다.
6. 운영/handoff 문서 갱신 필요성을 보고하고, 사용자 요청 또는 승인 후 반영한다.
7. 다음 skill 또는 action을 handoff한다.
8. 완료 전 `.agents/skills/operate-app/scripts/verify.ps1`을 실행하거나, 실행하지 못한 이유를 남긴다.

## Decision Rules

- 관찰과 진단을 섞지 않는다.
- incident 사실, timestamp, 결정은 보존한다.
- rollback, hotfix, production config 변경은 high-risk로 보고 `prepare-release`로 올린다.
- feedback은 가능하면 제품 문제, UI 문제, 기술 문제, 운영 문제로 분류한다.
- 사용자 영향이 크거나 개인정보/결제/인증과 관련되면 risk를 높게 잡는다.
- 불확실한 원인은 확정처럼 말하지 않는다.

## Documentation Updates

문서 갱신이 필요하면 먼저 필요성을 보고하고, 사용자 요청 또는 승인 후 갱신한다.

- monitoring 기준 변경: `docs/operations/monitoring.md`
- incident 절차 변경: `docs/operations/incident-playbook.md`
- rollback 기준 변경: `docs/operations/rollback.md`
- 현재 상태: `docs/handoff/current-state.md`
- 확정 결정: `docs/handoff/decisions.md`
- 미확정 질문: `docs/handoff/open-questions.md`
- 다음 액션: `docs/handoff/next-actions.md`

단순 관찰만으로 장기 문서를 과도하게 갱신하지 않는다. 반복되는 운영 지식이나 incident 결정은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 문서화한다.

## Handoff

- 제품 범위나 우선순위 조정이 필요하면 `plan-product`로 넘긴다.
- UI/UX feedback이면 `design-ui`로 넘긴다.
- 구조/API/data/auth 문제가 확인되면 `plan-architecture`로 넘긴다.
- 코드 수정이 필요하면 `implement-feature`로 넘긴다.
- 검증이나 회귀 테스트가 필요하면 `verify-change`로 넘긴다.
- rollback, release, production config 변경이 필요하면 `prepare-release`로 넘긴다.

## Completion Output

최종 응답에는 필요한 범위 안에서 다음을 포함한다.

- observed: 확인된 운영 사실.
- diagnosis: 진단 또는 아직 가설인 내용.
- impact: 사용자/시스템 영향.
- docs changed: 갱신한 operations/handoff 문서.
- next actions: 즉시 조치와 후속 작업.
- risk: 남은 production risk와 escalation 필요 여부.
- verification script: 실행한 local script와 결과 또는 미실행 이유.
