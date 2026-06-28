# Conventions

## Purpose

이 문서는 앱 구현 시 Antigravity/AGY가 따를 공통 개발 관례를 정의한다.

## Read When

- 프로젝트 코드를 작성하거나 수정할 때
- naming, folder placement, generated code, dependency 처리 기준이 필요할 때
- 상태 관리(예: Riverpod, Redux), Result/AppFailure, 라우팅, 로깅 규칙을 확인할 때
- UI runtime issue, overflow, red screen을 개발 관점에서 처리할 때
- responsive layout, display-size variant, `Stack`/`Positioned` 사용 기준이 필요할 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 실제 코드/설정/문서와 충돌하는 규칙을 발견해 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다.
앱별 architecture, state management, folder structure는 실제 코드와 `docs/architecture/overview.md`에서 확인한 뒤 반영한다.

## Related Docs

| Topic | Source |
| --- | --- |
| Project structure and layer boundary | `docs/architecture/overview.md` |
| UI state and visual behavior | `docs/design/states.md`, `docs/design/design-system.md` |
| Test selection and placement | `docs/development/testing.md` |
| Project commands and code generation | `docs/development/commands.md` |
| Documentation ownership | `docs/harness/documentation-ownership.md` |

## Source Of Truth

1. 현재 코드와 테스트를 먼저 확인한다.
2. 구조 결정은 `docs/architecture/overview.md`를 확인한다.
3. UI 표현 기준은 `docs/design/design-system.md`와 `docs/design/states.md`를 확인한다.
4. 문서와 코드가 충돌하면 코드를 기준으로 현재 동작을 파악하고, 문서 갱신 필요성을 보고한다.

## Code Style

- 기존 파일, 폴더, naming, state management 패턴을 우선한다.
- 변경은 요청 범위에 가깝게 유지한다.
- 의미 있는 중복 제거가 아니면 새 abstraction을 만들지 않는다.
- public API, route, model, storage schema는 가볍게 바꾸지 않는다.
- 주석은 의도가 코드만으로 명확하지 않을 때만 짧게 남긴다.

## Naming

- Class, enum, typedef, extension 이름은 `PascalCase`를 사용한다.
- File과 directory 이름은 `snake_case`를 사용한다.
- Function, method, variable, parameter 이름은 `camelCase`를 사용한다.
- Private member는 언어 관례대로 접근 제한자를 사용한다. 예: Dart에서는 `_` prefix, TypeScript에서는 `private` keyword.
- Test file은 대상 파일 이름을 기준으로 프로젝트 테스트 naming convention을 따른다. 예: `*_test.dart`, `*.test.ts`, `test_*.py` 등.
- Asset file과 asset directory 이름은 `snake_case`를 사용한다.

## Data Naming

- Domain object는 `Entity` suffix를 사용한다. 예: `UserEntity`, `OrderEntity`.
- External input/output object는 `Dto` suffix를 사용한다. 예: `UserDto`, `CreateOrderRequestDto`.
- File name은 class name을 프로젝트 naming convention에 맞춘다. 예: `user_entity.dart`, `UserEntity.ts`, `user_entity.py` 등.
- 기본값은 `Dto`와 `Entity`만 사용한다.
- `Model` suffix는 기본적으로 사용하지 않는다.
- `Document`, `Payload`, `CacheModel`, `LocalModel`, `UiModel` 같은 추가 suffix는 실제 프로젝트에서 필요성이 확인되기 전까지 만들지 않는다.
- Layer purity만을 이유로 필드와 책임이 같은 data class를 중복 생성하지 않는다.

## Implementation

- UI 컴포넌트는 테스트와 재사용이 가능할 정도로 작게 유지한다.
- 컴포넌트화할 때 하나의 소스 파일은 가능한 한 300줄을 넘지 않도록 한다.
- 300줄 기준은 hard limit이 아니다. 책임이 선명하고 분리 비용이 더 크면 유지할 수 있지만, 300줄을 넘으면 UI 컴포넌트 분리, helper 분리, state 로직 분리 가능성을 검토한다.
- User-visible state는 loading, empty, error, success, disabled를 명시적으로 다룬다.
- 비동기 작업은 loading, cancellation, retry, error path를 함께 고려한다.
- null safety를 우회하는 강제 unwrap은 근거가 명확할 때만 사용한다.
- shared component 변경은 medium risk 이상으로 보고 영향 범위를 확인한다.

## Responsive Layout Implementation

Responsive UI는 design 원칙과 구현 constraint를 함께 확인한다.

| Need | Prefer | Avoid |
| --- | --- | --- |
| Parent size에 따라 layout 변경 | `LayoutBuilder` constraints | 전체 화면 크기만 보고 local layout 결정 |
| Platform/screen-level 판단 | Platform query (예: `MediaQuery`) or project breakpoint helper | 여러 UI 컴포넌트에서 같은 breakpoint 수치 반복 |
| Content가 길어질 수 있음 | `Flexible`, `Expanded`, `Wrap`, `ConstrainedBox`, `TextOverflow` | 고정 width/height만으로 버티기 |
| List or long content | `ListView`, `CustomScrollView`, `Sliver` | 전체 화면을 무조건 `SingleChildScrollView`로 감싸기 |
| Fixed-format UI | `AspectRatio`, min/max constraints, stable dimensions | 자식 크기에 따라 board/grid/tool bar가 흔들리게 두기 |
| Safe area or keyboard | `SafeArea`, inset-aware padding, scroll boundary | keyboard open 상태에서 submit path 가림 |

Rules:

- Local UI 컴포넌트 layout은 가능하면 parent constraint 기반 layout (예: `LayoutBuilder`)을 우선한다.
- App-wide breakpoint가 필요하면 `core/theme/` 또는 공용 responsive helper에 모으고, 수치를 screen마다 반복하지 않는다.
- `MediaQuery`는 전체 화면, orientation, safe area, text scale 같은 screen-level 정보가 필요할 때 사용한다.
- Display 크기에 따라 화면 구조가 달라지면 `docs/design/screen-flows.md`에 compact/medium/expanded 차이를 기록한다.
- 공용 breakpoint, max width, responsive padding은 `docs/design/design-system.md`에 기록한다.
- Responsive layout에서도 `viewmodel`은 `BuildContext`를 사용하지 않는다.

## Stack And Positioned

`Stack`과 `Positioned`는 겹침이 의도된 layout에 제한적으로 사용한다.

Good uses:

- Badge, overlay, floating action, tutorial highlight, image caption
- Dialog/sheet 내부의 close button처럼 위치 의미가 명확한 보조 action
- Decorative element that does not affect core content

Avoid:

- 주요 content, form field, primary action을 절대 좌표로 배치
- screen size 변화에 따라 content가 겹칠 수 있는 hard-coded `top`, `left`, `right`, `bottom`
- 긴 텍스트나 text scaling을 고려하지 않은 fixed-position label
- Keyboard open 상태에서 submit button이나 field가 가려지는 구조

Rules:

- 주요 content 배치는 constraint-aware layout (예: `Column`, `Row`, `Flex`, `Wrap`, `Grid`, `ListView`, `CustomScrollView`)을 우선한다.
- `Positioned`를 쓰면 compact screen, large text scale, keyboard open, orientation change에서 overlap을 확인한다.
- `Stack` 안의 interactive element는 touch target과 semantic label을 유지한다.
- `Stack`으로 overflow를 숨기지 않는다. Overflow root cause를 먼저 해결한다.

## State Management Generation

- 코드 생성 기반 상태 관리(예: Riverpod 등)를 사용할 때 적용한다.
- Viewmodel class는 `{ScreenName}Viewmodel` 형식을 사용한다. 예: `HomeViewmodel`.
- Viewmodel file은 `{screen_name}_viewmodel.{ext}` 형식을 사용한다. 예: `home_viewmodel.dart`.
- UI 컴포넌트 notifier class는 `{ComponentName}Notifier` 형식을 사용한다. 예: `QuantitySelectorNotifier`.
- UI 컴포넌트 notifier file은 `{component_name}_notifier.{ext}` 형식을 사용한다. 예: `quantity_selector_notifier.dart`.
- 상태 객체 인스턴스는 직접 선언하지 않고 생성 결과를 사용한다.
- 상태를 읽을 때는 지정된 패턴을 사용한다 (예: ref.watch, useSelector).
- 생성 파일 참조 지시어(예: part, import)는 source file name과 일치시킨다.
- 생성 파일(예: *.g.dart, *.generated.ts 등)은 직접 수정하지 않는다.
- Page 또는 screen의 viewmodel 전용 provider/store는 `core/di/view/` 아래에 screen 구조에 맞게 둔다.
- Repository, data source, use case provider는 레이어에 맞는 `core/di/` 하위 폴더에 둔다.
- 단 하나의 screen 내부 UI 컴포넌트에서만 쓰는 상태 생성자는 해당 컴포넌트 folder에 둘 수 있다.
- 여러 screen에서 쓰는 provider는 screen-local UI 컴포넌트 folder에 두지 않는다.

## Generated Code

- Generated file은 직접 수정하지 않는다.
- 생성 파일은 annotation, 지시어, 선언을 수정한 뒤 재생성한다.
- 생성 파일 참조는 source file name과 정확히 일치해야 한다.
- 코드 생성을 트리거하는 변경이 있었는지 먼저 판단한다.
- 상태 생성자(예: provider, reducer 등) 추가, 삭제, 이름 변경, 파라미터 변경이 있으면 code generation을 실행한다.
- 기존 상태를 소비하는 UI 코드만 바뀐 경우에는 code generation을 실행하지 않는다.
- 상태 갱신 로직 본문만 바뀐 경우에는 보통 code generation을 실행하지 않는다. analyze/build가 stale generated code를 보고하면 실행한다.
- Code generation 명령은 `docs/development/commands.md`를 따른다.

## Dependency Policy

- 새 package 추가 또는 version 변경은 사용자 허락 없이 하지 않는다.
- 허락을 요청할 때는 package 목적, 대안, 영향 범위를 함께 설명한다.
- 허락을 요청할 때는 package registry 기준 최신 정보를 확인해 package link, latest version 또는 최종 업데이트 일자, likes 수를 함께 전달한다.
- package가 유지보수 중인지, null safety와 현재 SDK에 맞는지 확인한다.
- 기존 dependency로 해결할 수 있으면 새 package를 추가하지 않는다.

## Assets

- Asset file과 directory 이름은 `snake_case`를 사용한다.
- Asset을 추가하면 프로젝트 설정 파일 등록 여부를 확인한다.
- Asset을 삭제하면 사용처와 프로젝트 설정 파일 entry를 함께 확인한다.
- 가능하면 개별 파일보다 directory 단위 등록을 우선하되, 기존 프로젝트 등록 방식을 따른다.
- Asset path string을 screen 곳곳에 직접 흩뿌리지 않는다. 프로젝트에 asset constants가 있으면 사용한다.
- Secret, credential, production config는 asset으로 추가하지 않는다.
- Asset 변경 후에는 dependency install, analyze, 관련 UI 확인을 고려한다.

## Secure Coding

- secret, token, API key, signing key, service account 정보, production credential은 하드코딩하지 않는다.
- secret은 source code, asset, docs, test, fixture, screenshot, log에 남기지 않는다.
- auth header, 전체 API response body, local config 전체, 결제 정보, 개인정보는 로그에 남기지 않는다.
- 외부 exception message를 사용자에게 그대로 보여주지 않는다.
- 외부 error는 repository implementation 또는 mapper에서 `AppFailure`로 변환한 뒤 viewmodel로 전달한다.
- auth, permission, payment, privacy, data deletion, migration, production config 변경은 high-risk로 다룬다.
- 민감한 token 저장이 필요하면 secure platform storage 필요 여부를 먼저 확인한다.
- client-side validation은 UX 보조용이며 server/domain validation을 대체하지 않는다.
- permission-denied, unauthorized 상태는 crash나 red screen이 아니라 명시적인 UI state로 처리한다.
- native permission, background behavior, tracking, analytics, network access를 추가하는 package는 사용자 승인 없이 추가하지 않는다.
- test, fixture, sample data에는 실제 사용자 정보, production token, private key, 실제 service config를 넣지 않는다.

## Error And Failure Handling

- 실패 가능한 외부 작업은 repository interface 또는 use case에서 `Result<T>`로 표현한다.
- `Result<T>`는 `Success<T>`와 `Failure<T>` sealed class를 기본으로 한다.
- `Failure<T>`는 raw exception이 아니라 `AppFailure`를 담는다.
- `AppFailure`와 공통 exception/failure mapper는 `core/error/`에 둔다.
- `data_source`는 외부 SDK/API exception을 throw할 수 있다.
- `repository_impl`은 exception을 catch하고 `AppFailure`로 mapping한 뒤 `Result<Entity>`를 반환한다.
- `use_case`는 실패 가능하거나 여러 repository를 조합할 때 `Result<T>`를 반환한다.
- 단순 repository pass-through이고 추가 business flow가 없으면 viewmodel이 domain repository interface를 직접 사용할 수 있다.
- `viewmodel`은 `Result<T>`를 푸는 마지막 경계다.
- `viewmodel`은 성공을 data state로, 실패를 `AsyncError` 또는 screen-specific error state로 변환한다.
- `view`는 `Result<T>`를 직접 다루지 않는다.
- `repository`와 `use_case`는 상태 관리 비동기 타입 (예: Riverpod AsyncValue 등)을 반환하지 않는다.
- Pure mapper, formatter, local calculation에는 실패가 계약이 아닌 한 `Result<T>`를 사용하지 않는다.

## Async State Handling

- Viewmodel은 async 작업 시작 시 loading state를 명확히 만든다.
- Viewmodel은 `Result<T>`를 소비해 success를 data state로, failure를 `AsyncError` 또는 screen-specific error state로 변환한다.
- View는 loading, data, error, empty, disabled 상태를 명시적으로 처리한다.
- View는 raw exception, DTO, `Result<T>`를 직접 다루지 않는다.
- Empty state는 성공했지만 표시할 데이터가 없는 상태로 보고, failure와 구분한다.
- Error state는 retry 가능 여부와 사용자에게 보여줄 message를 구분한다.
- Async 작업이 중복 실행될 수 있으면 cancellation, stale response, disabled action을 고려한다.
- Error를 숨기기 위한 broad catch, 빈 fallback, silent failure는 사용하지 않는다.

## Validation Boundary

- View는 입력 표시, field-level feedback, error text 노출을 담당한다.
- Viewmodel은 form state, submit 가능 여부, validation trigger, async submit 흐름을 담당한다.
- Domain 또는 use case는 앱 규칙과 business validation을 담당한다.
- Data layer는 DTO parsing, external schema mismatch, API error mapping을 담당한다.
- API error를 view에서 직접 파싱하지 않는다.
- Viewmodel은 raw exception, DTO, API response body를 직접 다루지 않는다.
- User-visible validation message는 viewmodel 또는 view에서 `AppFailure`나 validation result를 화면 상태로 변환해 표시한다.
- Submit 전 client validation과 submit 후 server validation을 구분한다.
- Auth, payment, privacy, data deletion 관련 validation은 high-risk로 보고 테스트와 failure path를 확인한다.

## Navigation

- 라우팅은 프로젝트에서 지정한 라이브러리/방식을 사용한다.
- `viewmodel`은 `BuildContext`를 사용하지 않는다.
- Path 문자열을 screen에서 직접 쓰지 않는다.
- Route name/path는 `core/router` 상수만 사용한다.
- Auth, onboarding 같은 전역 redirect는 개별 screen에 흩뿌리지 않고 guard에서 처리한다.
- Path parameter는 명확한 이름을 쓴다. 예: `:userId`, `:orderId`.
- Query parameter는 route builder에서 파싱하고, viewmodel에는 필요한 값만 넘긴다.

## Logging

- `print()`를 운영 코드에 남기지 않는다.
- 임시 debug log는 작업 완료 전 제거한다.
- Debug log는 개발 중 진단용이며, 운영 추적이나 사용자 행동 분석 용도로 사용하지 않는다.
- Operational log는 장애, 오류, 중요한 boundary event를 추적하기 위한 로그다.
- Operational log와 crash/error reporting은 `core/error/` 또는 프로젝트의 monitoring integration 경로로 모은다.
- Secret, token, credential, 개인정보, 결제 정보는 로그에 남기지 않는다.
- API response body, auth header, local config 전체를 그대로 로그에 남기지 않는다.
- `AppFailure` mapping 시 운영 추적이 필요한 경우에만 log/report를 남긴다.
- UI rebuild, 단순 클릭, 반복 polling 같은 noisy event는 운영 로그로 남기지 않는다.
- Crash나 incident 성격이면 `docs/operations/monitoring.md`와 `docs/operations/incident-playbook.md`를 따른다.

## UI Runtime Issues

Overflow와 red screen은 현상 해결 관점에서는 development/implementation에 속한다. 사용자에게 보이는 표현 기준은 design과 함께 확인한다.

| Issue | Primary owner | Also check |
| --- | --- | --- |
| `RenderFlex overflowed`, text clipping, layout overflow | implementation | `design-ui`, `docs/design/design-system.md`, `docs/design/states.md` |
| Debug red screen caused by exception | implementation | `verify-change` |
| Production crash or user-reported red/error screen | operations | implementation, `verify-change` |
| Error/empty/loading UI 표현 기준 | design | implementation |

## Overflow Rules

- Constraint 문제인지, content 길이 문제인지, scroll 필요성인지 먼저 구분한다.
- 긴 텍스트, 큰 font scale, 번역 길이, 작은 화면을 고려한다.
- 무조건 `SingleChildScrollView`, `Expanded`, `Flexible`, `OverflowBox`를 추가하지 않는다. 원인에 맞는 layout constraint를 선택한다.
- 버튼, 탭, 입력창처럼 고정 크기 UI는 text overflow, wrapping, ellipsis, min/max width 기준을 확인한다.
- 수정 후 UI 컴포넌트 test, golden, screenshot, manual visual verification 중 가능한 검증을 선택한다.

## Red Screen And Exceptions

- red screen은 보통 framework exception, build/layout exception, async error, null/state error의 신호다.
- root cause를 고치지 않고 catch-all, 빈 fallback, error 무시로 덮지 않는다.
- 사용자에게 보여야 하는 error state는 `docs/design/states.md` 기준을 따른다.
- 운영 중 발생한 crash는 `docs/operations/incident-playbook.md`와 `docs/operations/monitoring.md`도 확인한다.

## Documentation

- 반복해서 참조할 명령, 구조, 결정, 운영 기준은 문서화 필요성을 보고하고, 사용자 요청 또는 승인 후 반영한다.
- 단순 구현 로그를 문서에 남기지 않는다.
- 미확정 질문은 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 둔다.
- 확정 결정은 사용자 요청 또는 승인 후 `docs/handoff/decisions.md`에 둔다.
