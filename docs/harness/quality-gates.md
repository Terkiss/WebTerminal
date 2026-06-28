# Quality Gates

이 문서는 변경 유형별 완료 기준과 verification script 선택 기준이다. 검증은 변경 범위와 risk에 맞게 좁게 시작하고, 실패하거나 영향 범위가 커지면 넓힌다.

## Gate Matrix

| Change type | Minimum gate | Escalate when |
| --- | --- | --- |
| 문서만 변경 | 링크, 참조 경로, 정책 일관성 확인 | harness 정책, release, risk 기준이 바뀔 때 |
| 제품/기획 변경 | `plan-product` script 또는 관련 문서 일관성 확인 | MVP, metric, roadmap 결정이 구현 범위에 영향 줄 때 |
| UI/UX 변경 | `design-ui` script, 상태 정의 확인, 필요한 visual verification | navigation, shared component, accessibility가 바뀔 때 |
| 코드 구현 | project analyze command, 관련 project test command, `implement-feature` script | API, auth, data, shared state가 바뀔 때 |
| 테스트 변경 | `verify-change` script, 관련 test 실행 | test harness, CI, fixture, mock이 넓게 바뀔 때 |
| Architecture/API/data | `plan-architecture` script, 관련 test, ADR 필요 여부 확인 | auth, privacy, migration, storage가 바뀔 때 |
| Release/deploy | `prepare-release` script, checklist, rollback, monitoring 확인 | production-impacting action이 있을 때 |
| Operations/incident | `operate-app` script, monitoring, incident, rollback 기준 확인 | rollback, release config, user data 영향이 있을 때 |
| Harness 변경 | JSON/shell/Python syntax, skill frontmatter, referenced path 확인 | hooks, skill selection, risk policy가 바뀔 때 |

## Project Verification

실제 프로젝트에 패키지 설정 파일이 있으면 다음을 우선 고려한다.

- Static analysis: project analyze command (예: `flutter analyze`, `eslint`, `ruff` 등 프로젝트에 맞는 도구)
- Unit/component tests: project test command (예: `flutter test`, `npm test`, `pytest` 등 프로젝트에 맞는 도구)
- Targeted tests: 변경된 기능과 가장 가까운 테스트 파일 (예: `*_test.dart`, `*.test.ts`, `test_*.py` 등)
- UI-specific verification: component, snapshot, manual visual verification
- Release readiness: build, signing, flavor, smoke test는 release skill에서 확인

템플릿 상태처럼 패키지 설정 파일이 없으면 프로젝트 명령은 skip할 수 있다. 이 경우 문서, script, path, syntax 검증을 수행한다.

## Skill Verification Scripts

각 skill은 자체 bundled script를 가진다. Antigravity/AGY는 선택된 skill의 workflow에 따라 필요한 script만 직접 실행한다.

| Skill | Script |
| --- | --- |
| Product | `.agents/skills/plan-product/scripts/verify.ps1` |
| Design UI | `.agents/skills/design-ui/scripts/verify.ps1` |
| Architecture | `.agents/skills/plan-architecture/scripts/verify.ps1` |
| Implementation | `.agents/skills/implement-feature/scripts/verify.ps1` |
| Verification | `.agents/skills/verify-change/scripts/verify.ps1` |
| Release | `.agents/skills/prepare-release/scripts/verify.ps1` |
| Operations | `.agents/skills/operate-app/scripts/verify.ps1` |

## Exit Codes

- `0`: 검증 통과. Warning이 있을 수 있으므로 출력 내용을 확인한다.
- Non-zero: 검증 실패. 실패 원인을 고치거나, 실행 불가 사유와 남은 위험을 보고하기 전까지 완료로 말하지 않는다.
- `HARNESS_STRICT=1`: 일부 script는 warning을 failure로 취급할 수 있다.

## Hooks Versus Scripts

Hooks는 skill script를 자동 실행하는 scheduler가 아니다.

- Pre/Post hooks: tool 사용 전후 공용 safety check.
- Stop hook: 최종 응답 전 harness 구조, hook syntax, skill frontmatter, script syntax, high-risk changed path hint를 확인하는 lightweight final gate.
- Skill scripts: 선택된 skill workflow에서 명시적으로 실행하는 검증.
- Project commands: 실제 앱 코드 품질을 확인하는 프로젝트 검증.

Stop hook은 프로젝트 command나 skill verification script를 자동 실행하지 않는다. Warning은 최종 응답의 verification, skipped, residual risk 보고에 반영한다. `HARNESS_STOP_STRICT=1`이면 warning도 failure로 취급할 수 있다.

## Completion Gate

기능 변경은 테스트, 분석, 빌드, 또는 합리적인 대체 검증 없이 완료로 보고하지 않는다. High-risk 변경은 검증이 통과해도 rollback, monitoring, residual risk를 함께 보고한다.
