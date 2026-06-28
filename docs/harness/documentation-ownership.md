# Documentation Ownership

이 문서는 docs 폴더의 소유권과 Antigravity/AGY 기록 기준을 정의한다. 공식 Antigravity 형식은 아니며, 이 앱 harness 템플릿의 문서 운영 규칙이다.

템플릿은 앱별 사실을 미리 가정하지 않는다. Antigravity/AGY는 확인되지 않은 제품, 디자인, 아키텍처, 운영 결정을 문서에 확정값처럼 쓰지 않는다. 모든 문서 갱신은 갱신 필요성을 먼저 보고하고, 사용자 요청 또는 승인 후 진행한다.

## Ownership Types

| Type | Meaning |
| --- | --- |
| User-owned | 사용자가 결정권을 가진 문서. Antigravity/AGY는 추측으로 확정 기록하지 않는다. |
| Team-owned | 실제 프로젝트 팀의 관례가 기준인 문서. Antigravity/AGY는 확인된 명령과 규칙만 기록한다. |
| Antigravity-assisted | Antigravity/AGY가 초안, 정리, 반영을 도울 수 있지만 결정권은 사용자에게 있다. |
| Antigravity-updated | Antigravity/AGY가 상태, 질문, 다음 액션 갱신 필요성을 능동적으로 보고할 수 있다. 실제 기록은 사용자 요청 또는 승인 후 수행한다. |

## Folder Policy

| Folder | Role | Ownership | Antigravity/AGY Write Rule |
| --- | --- | --- | --- |
| `docs/project/` | 프로젝트 배경, 용어, 전역 제약 | User-owned | 사용자가 알려준 사실이나 확인된 제약만, 사용자 요청 또는 승인 후 반영한다. |
| `docs/product/` | 문제, 사용자, MVP 범위, 지표, 로드맵 | User-owned / Antigravity-assisted | 제품 결정은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 기록한다. Antigravity/AGY는 초안과 선택지 정리를 도울 수 있다. |
| `docs/design/` | UX/UI 원칙, 화면 흐름, 상태, 디자인 시스템 | User-owned / Antigravity-assisted | 합의된 디자인 기준과 구현 중 확인된 UI 상태만, 사용자 요청 또는 승인 후 반영한다. |
| `docs/architecture/` | 구조, API, 데이터 모델, 인증/권한, ADR | User-owned / Antigravity-assisted | 되돌리기 어려운 기술 결정, API/data/auth 변경은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 기록한다. |
| `docs/development/` | 개발 환경, 명령어, 코드 컨벤션, 테스트 전략 | Team-owned / Antigravity-assisted | 실제 실행하거나 프로젝트에서 확인한 명령과 규칙만, 사용자 요청 또는 승인 후 반영한다. |
| `docs/operations/` | 릴리스, 롤백, 모니터링, 장애 대응 | User-owned / Antigravity-assisted | 배포, 롤백, 운영 기준은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 기록한다. incident 사실은 진단과 분리한다. |
| `docs/handoff/` | 현재 상태, 결정, 열린 질문, 다음 액션 | Antigravity-updated | 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 기록한다. 결정은 사용자 발화나 합의된 내용만 기록한다. |
| `docs/harness/` | Antigravity/AGY harness 정책, skill 선택, risk, quality 기준 | User-owned / Antigravity-assisted on request | harness 구조나 정책 변경 요청 또는 승인 후에만 반영한다. |

## Document Metadata

핵심 문서는 가능한 한 아래 공통 섹션으로 시작한다. 본문 섹션은 문서 성격에 맞게 유지한다.

| Section | Purpose |
| --- | --- |
| `Purpose` | 이 문서가 무엇을 정의하거나 기록하는지 설명한다. |
| `Read When` | Antigravity/AGY가 어떤 작업 상황에서 이 문서를 읽어야 하는지 설명한다. |
| `Update Policy` | Antigravity/AGY가 어떤 조건에서 수정 제안 또는 수정을 할 수 있는지 설명한다. |
| `Related Docs` | 함께 확인해야 하는 문서를 연결한다. |

`Purpose`는 문서의 책임이고, `Read When`은 문서 로딩 조건이다. 두 섹션에 같은 내용을 반복하지 않는다.

## Write Timing

| Timing | Write when |
| --- | --- |
| Before work | 필요한 문서가 비어 있거나 충돌하면 추측하지 말고, 갱신 필요성을 보고하거나 blocking question/선택지를 제시한다. |
| During work | 확인된 결정, 미해결 질문, 다음 액션은 사용자 요청 또는 승인 후 handoff 문서에 남긴다. |
| After work | 반복해서 참조할 명령, 정책, 구조, release 기준이 생기면 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 해당 docs에 반영한다. |

## Write Rules

- Do not invent product, architecture, release, or operations decisions.
- Do not replace domain docs without a user request or approval after reporting a confirmed conflict or gap.
- If a document or situation is missing or ambiguous, provide options instead of filling the gap with assumptions.
- Put unresolved questions or options in `docs/handoff/open-questions.md` only after user request or approval.
- Put confirmed decisions in `docs/handoff/decisions.md` only after user request or approval.
- Put follow-up work in `docs/handoff/next-actions.md` only after user request or approval.
- Keep domain docs focused on reusable knowledge, not one-off command logs.
- Prefer small targeted updates over broad documentation rewrites.
- If docs and code conflict, inspect code first and report the conflict.
