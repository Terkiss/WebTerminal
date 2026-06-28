# Risk Policy

이 문서는 Antigravity/AGY가 앱 harness 작업을 시작하기 전에 risk를 분류하는 기준이다. Risk는 고정값이 아니다. 작업 중 auth, data, release, secret, production 영향이 발견되면 즉시 다시 분류한다.

## Levels

| Level | Meaning | Examples |
| --- | --- | --- |
| `low` | 되돌리기 쉽고 사용자 동작 영향이 작다. | 문서 정리, 오탈자, 테스트 추가, 동작 변경 없는 리팩터링, 작은 스타일 정리 |
| `medium` | 사용자 경험이나 앱 동작에 영향이 있다. | 기능 추가, 상태 관리 변경, API 호출 변경, navigation 변경, shared component 변경 |
| `high` | 보안, 데이터, 배포, 운영, 복구 가능성에 영향이 있다. | auth, permission, payment, privacy, data delete, migration, secret, release, deploy, rollback, incident |

## High-Risk Triggers

다음 중 하나라도 있으면 high-risk로 본다.

- 인증, 권한, 세션, 토큰, credential 처리
- 결제, 구독, 과금, 구매 복원
- 개인정보, 민감정보, privacy policy 영향
- 데이터 삭제, import, export, migration, destructive backfill
- 보안 규칙, secret, API key, signing key, service account
- release, deploy, signing, flavor, production config
- rollback, monitoring, alert, incident response
- 외부 서비스 설정 변경
- 사용자가 되돌리기 어려운 변경

## Required Actions

| Risk | Required action |
| --- | --- |
| `low` | 변경 범위에 맞는 가장 좁은 검증을 수행한다. |
| `medium` | 관련 skill 문서와 테스트 또는 분석을 확인한다. |
| `high` | 왜 high-risk인지 명시하고, 사용자 확인이 필요한 작업은 멈춘다. 검증, rollback 또는 mitigation, 남은 위험을 보고한다. |

## User Confirmation

다음 작업은 명시적 요청이나 확인 없이 실행하지 않는다.

- production deploy 또는 release upload
- 데이터 삭제, reset, migration 실행
- secret, credential, signing 설정 변경
- 결제, auth, permission 정책 변경
- destructive command
- 외부 인프라나 운영 설정 변경

## Hooks And Risk

`.agents/hooks/pre_tool_use_policy.py`는 명령 payload에서 obvious high-risk command와 secret-looking payload를 차단한다. `.agents/hooks/post_tool_use_review.py`는 변경된 파일 경로에 high-risk hint가 있으면 경고한다. `.agents/hooks/stop_quality_gate.py`는 완료 전 harness 구조와 high-risk changed path hint를 다시 확인한다.

Hooks는 risk 판단을 대신하지 않는다. Hooks는 공용 안전망이고, Antigravity/AGY는 요청과 변경 내용을 기준으로 별도로 risk를 분류해야 한다.

## Residual Risk

다음 상황에서는 최종 응답에 residual risk를 남긴다.

- 검증을 실행하지 못했다.
- 테스트는 통과했지만 실제 배포나 운영 확인은 하지 못했다.
- 문서와 코드가 충돌했다.
- high-risk 영역을 건드렸지만 사용자 결정이나 외부 환경 확인이 남았다.
