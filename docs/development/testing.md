# Testing

## Purpose

이 문서는 앱 변경 후 어떤 검증을 선택할지 정하는 공통 기준을 기록한다.

## Read When

- 기능, UI, data, auth, navigation 변경 후 검증 범위를 고를 때
- unit/UI 컴포넌트/integration/golden/manual verification 중 무엇이 필요한지 판단할 때
- test placement, mock, fixture, CI 실패 대응 기준이 필요할 때
- overflow, red screen, Result/AppFailure, async state 회귀를 확인할 때
- responsive layout, display-size variant, keyboard/text scaling 회귀를 확인할 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 실제 프로젝트의 test framework, mock, fixture, CI 명령과 충돌을 발견해 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다. 일회성 테스트 실행 로그는 이 문서에 기록하지 않는다.

## Related Docs

| Topic | Source |
| --- | --- |
| Project commands | `docs/development/commands.md` |
| Development conventions | `docs/development/conventions.md` |
| UI states and visual verification | `docs/design/states.md` |
| API/data/auth verification | `docs/architecture/api.md`, `docs/architecture/data-model.md`, `docs/architecture/auth-permissions.md` |
| Quality gates | `docs/harness/quality-gates.md` |

## Test Types

| Type | Use for | Typical command |
| --- | --- | --- |
| Unit | Pure logic, mapper, validator, use case | project test command |
| UI 컴포넌트 | UI state, interaction, layout behavior | project test command |
| Integration | Multi-screen flow, plugin/service boundary | project-specific |
| Golden | Visual regression, component appearance | project-specific |
| Manual visual | Overflow, responsive layout, motion, platform rendering | project-specific |

## Selection Rules

- Logic change: unit test를 우선한다.
- UI 컴포넌트/UI state change: UI 컴포넌트 test 또는 visual verification을 고려한다.
- Navigation or screen flow change: UI 컴포넌트/integration level 검증을 고려한다.
- Responsive layout change: compact, medium, expanded 크기와 text scaling을 확인한다.
- API/data/auth change: mapper, repository, error path, permission path를 검증한다.
- Release-impacting change: release checklist, rollback, smoke test 기준을 함께 확인한다.

## Result And Failure Verification

- Repository implementation은 external exception을 `AppFailure`로 mapping하는 테스트를 고려한다.
- Use case는 `Success<T>`와 `Failure<T>` path를 모두 검증한다.
- Viewmodel은 `Result<T>`를 `AsyncValue` 또는 screen-specific state로 변환하는 path를 검증한다.
- View는 `Result<T>`가 아니라 loading, data, error state를 기준으로 테스트한다.
- Broad catch로 failure를 숨기지 않았는지 확인한다.

## Async State Verification

- Loading state가 표시되는지 확인한다.
- Success state가 데이터와 함께 표시되는지 확인한다.
- Empty state가 success-without-data로 처리되는지 확인한다.
- Failure state가 user-visible error state로 변환되는지 확인한다.
- Retry, disabled action, duplicate submit 방지가 필요한지 확인한다.
- View test는 raw exception이나 `Result<T>`보다 화면 state와 user-visible behavior를 기준으로 작성한다.

## Validation Verification

- Field-level validation은 error text, focus, submit button state를 확인한다.
- Viewmodel validation은 submit 가능 여부, validation trigger, duplicate submit 방지를 확인한다.
- Domain/use case validation은 success/failure path와 business rule을 확인한다.
- API/server validation은 repository의 `AppFailure` mapping과 viewmodel의 error state 변환을 확인한다.
- Client validation과 server validation이 서로 다른 메시지나 상태를 만들 때 둘 다 검증한다.

## Asset Verification

- Asset 추가/삭제 시 프로젝트 설정 파일 등록과 사용처를 확인한다.
- Missing asset으로 인한 red screen 또는 runtime exception이 없는지 확인한다.
- 아이콘, 이미지, animation 변경은 관련 화면의 visual verification을 고려한다.

## Logging Verification

- 운영 코드에 임시 `print()`나 noisy debug log가 남지 않았는지 확인한다.
- Secret, token, 개인정보, 결제 정보가 로그에 노출되지 않는지 확인한다.
- Error/failure path에서 필요한 operational log 또는 crash report가 누락되지 않았는지 확인한다.

## Test Placement

테스트 파일은 프로젝트 관례(예: src/와 test/ 분리, 또는 동일 폴더 내 *.test.ts 위치 등)를 따르되, 기본적으로 소스 구조와 동일한 트리를 유지한다.

```text
# 예시: src/와 test/가 분리된 프로젝트 구조
{src_dir}/data/auth/repository/auth_repository_impl.{ext}
{test_dir}/data/auth/repository/auth_repository_impl_test.{ext}

{src_dir}/domain/auth/use_case/login_use_case.{ext}
{test_dir}/domain/auth/use_case/login_use_case_test.{ext}

{src_dir}/view/home_screen/home_viewmodel.{ext}
{test_dir}/view/home_screen/home_viewmodel_test.{ext}

{src_dir}/view/home_screen/components/banner_carousel/banner_carousel_notifier.{ext}
{test_dir}/view/home_screen/components/banner_carousel/banner_carousel_notifier_test.{ext}
```

- Unit test는 대상 source와 같은 상대 경로를 따른다.
- UI 컴포넌트 test도 가능한 한 대상 screen/컴포넌트 경로를 따른다.
- 공용 UI 컴포넌트는 프로젝트 공용 컴포넌트 디렉터리에 대응해 테스트를 둔다.
- 기존 프로젝트 테스트 구조가 이미 있으면 기존 구조를 우선한다.

## Overflow Verification

Overflow fix는 단순히 에러가 사라졌는지만 보지 않는다.

- 긴 텍스트와 작은 화면에서 깨지지 않는지 확인한다.
- text scale 또는 접근성 설정 영향을 고려한다.
- `Stack`/`Positioned`를 사용했다면 display 크기별 overlap을 확인한다.
- keyboard open, orientation change, safe area 영향을 고려한다.
- loading, empty, error, success, disabled 상태 중 관련 상태를 확인한다.
- 가능한 경우 UI 컴포넌트 test, golden, screenshot, manual visual verification 중 하나 이상을 남긴다.

## Responsive Verification

Responsive layout 변경은 최소한 아래 조건을 고려한다.

| Case | Check |
| --- | --- |
| Compact | 작은 phone width에서 주요 content와 primary action이 잘리지 않는가? |
| Medium | 넓은 phone 또는 tablet급 폭에서 여백과 grouping이 유지되는가? |
| Expanded | content max width, side panel, multi-column이 의도대로 동작하는가? |
| Large text scale | 긴 label, button text, error text가 겹치거나 잘리지 않는가? |
| Keyboard open | 입력 field와 submit action이 가려지지 않는가? |
| Orientation change | 입력값, loading state, navigation state가 불필요하게 사라지지 않는가? |
| Loading/error/empty | display 크기별 상태 layout이 모두 사용 가능한가? |

Responsive verification rules:

- Display 크기별 구조가 다르면 `docs/design/screen-flows.md`의 responsive variant와 맞는지 확인한다.
- 공용 breakpoint나 spacing token이 바뀌면 `docs/design/design-system.md`와 맞는지 확인한다.
- 구현이 `LayoutBuilder` 또는 `MediaQuery` 기준을 따르는지 확인한다.
- `Positioned`, fixed size, absolute offset을 사용한 경우 overlap 회귀를 더 넓게 확인한다.
- 자동화가 어렵다면 확인한 크기, 상태, 남은 위험을 final response에 남긴다.

## Red Screen Verification

Red screen 또는 framework exception 수정은 root cause 검증이 필요하다.

- 실패를 재현한 뒤 수정한다.
- 동일 경로의 regression test를 추가하거나, 추가하지 못한 이유를 남긴다.
- exception을 숨기는 broad catch로 완료하지 않는다.
- production crash 성격이면 operations 문서와 monitoring/incident 기준을 확인한다.

## Verification Rules

- 기능 변경은 테스트, 분석, 빌드, 또는 합리적인 대체 검증 없이 완료로 보고하지 않는다.
- UI 변경은 상태와 overflow 가능성을 함께 확인한다.
- Auth, payment, privacy, migration, release, data deletion은 broader regression과 rollback 기준을 확인한다.
- CI 실패는 가능하면 로컬에서 가장 작은 명령으로 재현한다.

## Skill Scripts

```bash
.agents/skills/verify-change/scripts/verify.ps1
HARNESS_STRICT=1 .agents/skills/verify-change/scripts/verify.ps1
```

UI 검증이 필요하면 다음 script를 함께 고려한다.

```bash
.agents/skills/design-ui/scripts/verify.ps1
```
