# Project Docs

프로젝트 전체의 배경, 용어, 제약을 기록한다. 제품, 디자인, 아키텍처, 운영 판단보다 앞서는 전역 맥락이 있을 때 참고한다.

## Document Index

| Document | Role |
| --- | --- |
| `overview.md` | 실제 앱의 이름, 목적, 대상 platform, 현재 단계, Antigravity/AGY가 알아야 할 기본 배경을 기록한다. |
| `glossary.md` | 프로젝트 고유 용어, 도메인 용어, 약어, 모호한 용어의 의미를 기록한다. |
| `constraints.md` | platform, product, technical, safety 제약처럼 전체 판단에 영향을 주는 조건을 기록한다. |

## Rules

- 앱별 배경과 제약은 추측으로 채우지 않는다.
- 모호한 용어는 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 `glossary.md`에 남긴다.
- 해결되지 않은 전역 제약은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 남긴다.
