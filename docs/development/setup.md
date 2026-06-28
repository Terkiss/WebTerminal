# Setup

## Purpose

이 문서는 앱 개발 환경을 준비할 때 확인할 공통 기준을 기록한다.

## Read When

- 새 프로젝트에 harness를 붙인 뒤 초기 개발 환경을 확인할 때
- SDK, platform tool, external service CLI 기준이 필요할 때
- local config, env var, flavor, generated file 기준을 확인할 때
- dependency 설치나 setup 실패 원인을 정리할 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 실제 프로젝트 파일/명령/설정에서 확인된 setup 기준을 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다.
앱별 버전, flavor, external service, secret은 추측으로 채우지 않고, secret value는 절대 기록하지 않는다.

## Related Docs

| Topic | Source |
| --- | --- |
| Project commands | `docs/development/commands.md` |
| Code conventions and dependency policy | `docs/development/conventions.md` |
| Test commands and placement | `docs/development/testing.md` |
| Secret and credential handling | `docs/architecture/auth-permissions.md` |
| Documentation ownership | `docs/harness/documentation-ownership.md` |

## Requirements

| Item | Value |
| --- | --- |
| Project SDK |  |
| Language SDK | Project SDK bundled version |
| Project package manager |  |
| Project-required platform tools |  |
| Code generation tools |  |
| External service CLIs |  |

## First Run

기본 순서는 아래와 같다. 프로젝트에 별도 bootstrap script가 있으면 `docs/development/commands.md`에 기록하고 그 명령을 우선한다.

```bash
# Project dependency install command (예: flutter pub get, npm install, pip install -r)
# Project analyze command (예: flutter analyze, npm run lint, ruff check)
# Project test command (예: flutter test, npm test, pytest)
```

## Environment

| Item | Rule |
| --- | --- |
| Env vars | 이름만 기록하고 실제 secret value는 쓰지 않는다. |
| Local config files | 예: `.env`, generated config, local-only plist/json. 실제 파일명은 프로젝트 확인 후 기록한다. |
| Flavors | 프로젝트에 flavor가 있을 때만 기록한다. |
| External services | Firebase, analytics, crash reporting, payment 등 실제 사용이 확인된 것만 기록한다. |

## Generated Files

프로젝트가 code generation을 사용하면 생성 명령과 산출물 위치를 `docs/development/commands.md`에 기록한다.

- Generated file (예: `*.g.dart`, `*.generated.ts` 등)은 직접 수정하지 않는다.
- Source annotation, `part`, provider/notifier 선언을 수정한 뒤 재생성한다.
- Generated file 참조 (예: `part '{file_name}.g.dart';`)는 source file name과 일치해야 한다.

## Dependencies

새 package 추가나 version 변경은 사용자 허락 후 진행한다.

허락을 요청할 때는 다음 정보를 함께 전달한다.

- package name and purpose
- package registry link
- latest version 또는 최종 업데이트 일자
- likes 수
- 기존 dependency로 대체 가능한지 여부
- 예상 영향 범위와 verification plan

## Antigravity/AGY Rules

- 의존성을 새로 설치하거나 버전을 바꾸기 전에 이유와 영향 범위를 확인한다.
- setup 실패는 추측으로 해결하지 말고 missing tool, failed command, relevant output을 보고한다.
- secret, token, credential value는 문서나 로그에 쓰지 않는다.
- 프로젝트에 패키지 설정 파일 (예: `pubspec.yaml`, `package.json`, `requirements.txt` 등)이 없으면 프로젝트 명령을 실행하지 말고 템플릿/문서 검증으로 대체한다.
