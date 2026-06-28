---
name: prepare-release
description: Prepare or review app/project release, deploy, build signing, store/distribution delivery, rollout, rollback, smoke tests, release checklist, monitoring, production configuration, release tooling paths, service deployment paths, and approval readiness. Use for planned release or production-impacting work; do not use for ordinary implementation, verification, or post-release incident handling unless release readiness or rollback is involved.
---

# Prepare Release

빌드, signing, deployment, release checklist, rollout, rollback, production configuration 변경에 사용한다. production-impacting 작업을 완료하기 전에 검증과 복구 기준을 정리한다.

## Use When

- release, deploy, build signing, rollout, rollback, production config를 만들거나 바꿀 때.
- 배포 전 checklist, smoke test, monitoring, rollback 기준이 필요할 때.
- 릴리스 자동화나 배포 스크립트가 바뀔 때.
- high-risk production-impacting action 여부를 판단해야 할 때.

## Context Loading

먼저 사용자 의도, 현재 변경사항, 배포 대상 환경을 확인한다. 아래 문서와 script는 조건이 맞을 때만 사용한다.

- release 단계, 승인, checklist가 필요할 때: `docs/operations/release-checklist.md`
- rollback 조건이나 절차가 필요할 때: `docs/operations/rollback.md`
- smoke test, monitoring, alert 기준이 필요할 때: `docs/operations/monitoring.md`
- build, analyze, test, deploy 명령 확인이 필요할 때: `docs/development/commands.md`
- quality gate가 필요할 때: `docs/harness/quality-gates.md`
- high-risk 판단이 필요할 때: `docs/harness/risk-policy.md`
- 이어받기나 미해결 release 상태가 필요할 때: `docs/handoff/current-state.md`, `docs/handoff/open-questions.md`, `docs/handoff/next-actions.md`
- release readiness 검증이 필요할 때: `.agents/skills/prepare-release/scripts/verify.ps1`
- 관련 테스트 증거가 필요할 때: `.agents/skills/verify-change/scripts/verify.ps1`

모든 운영 문서와 script를 한 번에 사용하지 않는다. deploy, rollback, monitoring 중 요청과 직접 연결된 항목부터 확인한다.

## Workflow

1. 요청을 release task type으로 분류한다: `build`, `signing`, `deploy`, `rollout`, `rollback`, `monitoring`, `release-note`.
2. production-impacting action인지 판단한다.
3. release checklist, smoke test, rollback 기준을 확인한다.
4. 필요한 검증 명령이나 script를 선택한다.
5. 배포 실행이 필요하면 명시적 사용자 의도를 확인한다.
6. release note, 결정, unresolved risk를 정리한다.
7. 운영 후속 조치를 handoff한다.
8. 완료 전 `.agents/skills/prepare-release/scripts/verify.ps1`을 실행하거나, 실행하지 못한 이유를 남긴다.

## Decision Rules

- release와 deployment 변경은 high-risk로 다룬다.
- 명시적인 사용자 의도 없이 production-impacting action을 수행하지 않는다.
- release 작업을 완료로 선언하기 전에 rollback 기준을 확인한다.
- monitoring 또는 smoke test 기대사항을 확인한다.
- signing, credential, secret, 외부 서비스 설정 변경은 `AGENTS.md`의 Safety Policy를 따른다.
- rollback 기준이 없거나 검증할 수 없으면 완료로 과장하지 않는다.

## Documentation Updates

문서 갱신이 필요하면 먼저 필요성을 보고하고, 사용자 요청 또는 승인 후 갱신한다.

- release 절차 변경: `docs/operations/release-checklist.md`
- rollback 기준 변경: `docs/operations/rollback.md`
- monitoring 변경: `docs/operations/monitoring.md`
- quality gate 변경: `docs/harness/quality-gates.md`
- 결정/미해결 위험: `docs/handoff/decisions.md`, `docs/handoff/open-questions.md`
- 다음 액션: `docs/handoff/next-actions.md`

단순 release 준비 확인만으로 문서를 과도하게 갱신하지 않는다. 반복 가능한 절차나 high-risk 결정은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 문서화한다.

## Handoff

- 코드 수정이 필요하면 `implement-feature`로 넘긴다.
- 검증 추가나 CI 확인이 필요하면 `verify-change`로 넘긴다.
- 배포 후 monitoring, incident, feedback follow-up은 `operate-app`으로 넘긴다.
- 제품 rollout 범위나 성공 기준이 불명확하면 `plan-product`로 되돌린다.

## Completion Output

최종 응답에는 필요한 범위 안에서 다음을 포함한다.

- release decision: 대상, 범위, rollout/rollback 기준.
- verification: 실행한 release 검증과 결과.
- docs changed: 갱신한 operations/handoff 문서.
- approval needed: 사용자 확인이 필요한 production-impacting action.
- residual risk: 남은 배포/운영 위험.
- verification script: 실행한 local script와 결과 또는 미실행 이유.
