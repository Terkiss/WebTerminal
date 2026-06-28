# Development Docs

개발 환경, 명령어, 코드 convention, 테스트 기준을 기록한다. 코드 작성이나 검증을 수행할 때 참고한다.

## Document Index

| Document | Role |
| --- | --- |
| `setup.md` | SDK, platform tool, external CLI, local config, dependency 준비 기준을 기록한다. |
| `commands.md` | dependency install, analyze, test, formatting, code generation, harness script 명령 기준을 기록한다. |
| `conventions.md` | naming, folder, 생성 기반 상태 관리(예: Riverpod 등), generated code, dependency, asset, error, navigation, logging, responsive layout, UI runtime issue 기준을 기록한다. |
| `testing.md` | unit/widget/integration/golden/manual verification 선택 기준, test placement, responsive verification, overflow, keyboard/text scaling, regression 기준을 기록한다. |

## Rules

- 실제 프로젝트 명령과 convention은 코드, 설정, 실행 결과로 확인된 것만 기록한다.
- 생성 파일(예: *.g.dart, *.generated.ts 등)은 직접 수정하지 않고 지시어와 선언부를 수정한 뒤 재생성한다.
- 기능 변경은 테스트, 분석, 빌드, 또는 합리적인 대체 검증 없이 완료로 보고하지 않는다.
- Responsive UI 변경은 display 크기, 긴 텍스트, text scaling, keyboard open 상태를 함께 확인한다.
