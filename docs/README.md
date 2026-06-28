# Docs Index

이 폴더는 프로젝트 harness가 참고하는 프로젝트 문서 공간이다. 공식 Antigravity/AGY 필수 구조는 아니며, 이 템플릿에서 사용자 결정, 프로젝트 기준, Antigravity/AGY 작업 상태를 분리하기 위한 규약이다. 사용자의 직접 변경은 언제든 가능하다.

## Folder Index

| Folder | Role |
| --- | --- |
| `project/` | 앱 배경, 용어, 전역 제약을 기록한다. |
| `product/` | 문제, 사용자, MVP 범위, 지표, roadmap을 기록한다. |
| `design/` | UX/UI 원칙, 화면 흐름, 상태, design system을 기록한다. |
| `architecture/` | 프로젝트 구조, API, data model, auth/permission, ADR을 기록한다. |
| `development/` | 개발 환경, 명령어, 코드 convention, testing 기준을 기록한다. |
| `operations/` | release, rollback, monitoring, incident 대응 기준을 기록한다. |
| `handoff/` | Antigravity/AGY 작업 중 현재 상태, 결정, 열린 질문, 다음 액션을 기록한다. |
| `harness/` | Antigravity/AGY harness의 routing, risk, quality, documentation 정책을 기록한다. |

## Rules

- 앱별 사실은 사용자 결정, 코드, 설정, 외부 contract로 확인된 것만 기록한다.
- 문서 수정 권한과 기록 기준은 `harness/documentation-ownership.md`를 따른다.
- 각 폴더의 문서 목록은 해당 폴더의 `README.md`를 확인한다.
