# Design System

## Purpose

- 앱에서 반복 사용하는 UI token과 component 기준을 기록한다.
- 색상, typography, spacing, radius, elevation, icon, motion 같은 시각 기준을 한 곳에서 추적한다.
- 공용 component의 상태, 사용 조건, 금지 패턴, 검증 기준을 명확히 한다.
- Theme/component 구현과 문서가 서로 충돌하지 않게 한다.

## Read When

- 색상, typography, spacing, radius, icon, component 기준이 필요할 때
- breakpoint, content width, responsive spacing 기준이 필요할 때
- 공용 button, text field, dialog 같은 component를 추가하거나 바꿀 때
- UI 변경이 기존 design system과 맞는지 확인할 때
- 새 화면이 `docs/design/states.md`의 상태를 어떤 component로 표현할지 판단할 때
- theme, common UI 컴포넌트, shared component 변경이 여러 화면에 영향을 줄 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 실제 구현된 design token/component와 이 문서가 충돌함을 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다. 앱별 브랜드, 색상, typography를 추측으로 확정하지 않는다. 아래 조건은 갱신을 제안해야 하는 신호다.

Update this document when:

- 공용 color, typography, spacing, radius, elevation, icon, motion token이 추가되거나 바뀐다.
- breakpoint, content max width, responsive padding, grid 기준이 추가되거나 바뀐다.
- 공용 component의 상태, variant, size, accessibility contract가 바뀐다.
- 프로젝트의 theme 또는 공용 UI 컴포넌트 변경이 여러 화면에 영향을 준다.
- 특정 화면에서 반복될 가능성이 높은 UI pattern이 생긴다.
- UI 상태 표현 기준이 바뀌어 `docs/design/states.md`와 연결이 필요하다.

Do not update this document for:

- 한 화면에서만 쓰는 임시 layout 값.
- 브랜드/색상/폰트를 사용자 결정 없이 추측한 값.
- 단순 구현 로그나 테스트 실행 결과.
- component contract가 바뀌지 않는 작은 copy 수정.

미확정 design system 결정은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 둔다. 확정된 결정도 사용자 요청 또는 승인 후 `docs/handoff/decisions.md`에 남긴다.

## Related Docs

| Topic | Source |
| --- | --- |
| UX principles | `docs/design/ux-principles.md` |
| UI principles | `docs/design/ui-principles.md` |
| UI states | `docs/design/states.md` |
| Screen flows | `docs/design/screen-flows.md` |
| Implementation conventions | `docs/development/conventions.md` |

## System Boundary

이 문서는 반복 사용되는 design 기준만 기록한다.

| Belongs Here | Belongs Elsewhere |
| --- | --- |
| 공용 token, theme, component contract | 특정 화면의 user journey: `docs/design/screen-flows.md` |
| 공용 component state와 variant | 화면별 loading/empty/error 의미: `docs/design/states.md` |
| 반복되는 layout, spacing, icon 기준 | 실제 folder/layer 구조: `docs/architecture/overview.md` |
| component accessibility contract | 구현 naming/generated code 규칙: `docs/development/conventions.md` |

실제 구현이 이미 있으면 프로젝트의 theme, 공용 UI 컴포넌트, 관련 screen 컴포넌트를 먼저 확인한다. 문서가 코드와 다르면 코드를 기준으로 현재 동작을 파악하고, 갱신 필요성을 보고한다.

## Token Index

| Token Group | Source | Usage | Status |
| --- | --- | --- | --- |
| Color |  | Brand, semantic state, surface hierarchy |  |
| Typography |  | Text hierarchy, readability, density |  |
| Spacing |  | Padding, gap, list density, screen rhythm |  |
| Breakpoint/Layout |  | Display-size variant, content width, grid, panel behavior |  |
| Radius |  | Button, input, card, sheet, dialog shape |  |
| Elevation |  | App bar, sheet, dialog, floating element depth |  |
| Icon |  | Action, navigation, status, empty/error illustration |  |
| Motion |  | Transition, loading, feedback, state change |  |

Status examples: `planned`, `implemented`, `needs-review`, `deprecated`.

## Color Tokens

색상은 시각 브랜드와 의미 상태를 분리해서 기록한다.

| Token | Value | Usage | Contrast Notes |
| --- | --- | --- | --- |
| `primary` |  | Primary action, selected navigation |  |
| `onPrimary` |  | Text/icon on primary |  |
| `secondary` |  | Secondary action or accent |  |
| `background` |  | App background |  |
| `surface` |  | Card, sheet, dialog, input surface |  |
| `onSurface` |  | Text/icon on surface |  |
| `error` |  | Error state, destructive warning |  |
| `success` |  | Completed or positive state |  |
| `warning` |  | Caution or partial failure |  |
| `info` |  | Neutral guidance or system notice |  |

Color rules:

- 색상만으로 상태를 전달하지 않는다. Text, icon, shape, message를 함께 사용한다.
- Error, disabled, permission 상태는 `docs/design/states.md`의 의미와 맞춘다.
- Light/dark theme가 있으면 같은 token name으로 대응 값을 기록한다.
- Secret, environment, user role 같은 의미를 색상 이름에 직접 넣지 않는다.

## Typography Tokens

Typography는 화면 구조와 읽기 밀도를 기준으로 기록한다.

| Token | Size | Weight | Line Height | Usage |
| --- | --- | --- | --- | --- |
| `display` |  |  |  | 아주 드문 큰 제목 |
| `title` |  |  |  | Screen title, section title |
| `body` |  |  |  | 기본 본문 |
| `label` |  |  |  | Button, tab, chip, form label |
| `caption` |  |  |  | Helper text, metadata |
| `error` |  |  |  | Field error, validation message |

Typography rules:

- Font size를 screen width에 직접 비례시켜 조정하지 않는다.
- Letter spacing은 명확한 design decision이 없으면 `0`을 기본으로 한다.
- Compact panel, card, sidebar, toolbar 안에서는 hero-scale type을 사용하지 않는다.
- 긴 텍스트와 접근성 text scaling에서 overflow가 없는지 확인한다.

## Spacing Tokens

Spacing은 magic number를 줄이고 화면 밀도를 일정하게 유지하기 위해 기록한다.

| Token | Value | Usage |
| --- | --- | --- |
| `spaceXs` |  | Inline gap, tight icon/text gap |
| `spaceSm` |  | Small component padding |
| `spaceMd` |  | Default component gap |
| `spaceLg` |  | Section gap |
| `spaceXl` |  | Screen-level separation |
| `screenPadding` |  | Default page horizontal padding |

Spacing rules:

- 기존 프로젝트의 spacing/theme 구조가 있으면 그 token을 우선한다.
- Width/height spacing helper를 분리해 쓰는 프로젝트에서는 해당 helper를 따른다.
- Toolbar, grid, board, fixed-format UI는 stable dimensions를 먼저 정의한다.
- Pressed/focused state, loading text, badge, counter, dynamic label이 layout shift를 만들지 않게 한다.

## Breakpoint And Layout Tokens

Breakpoint는 display 크기에 따라 layout 구조나 content 폭이 달라질 때만 기록한다. 값은 실제 프로젝트 기준이 확인된 뒤 채운다.

| Token | Width Rule | Usage | Notes |
| --- | --- | --- | --- |
| `compact` |  | Small phone layout, single column |  |
| `medium` |  | Wide phone or small tablet layout |  |
| `expanded` |  | Tablet landscape or large display layout |  |
| `contentMaxWidth` |  | Prevent overly wide content |  |
| `compactPadding` |  | Compact screen horizontal padding |  |
| `mediumPadding` |  | Medium screen horizontal padding |  |
| `expandedPadding` |  | Expanded screen horizontal padding |  |
| `gridGap` |  | Multi-column or grid spacing |  |

Breakpoint rules:

- Breakpoint 값은 임의로 확정하지 않는다. 실제 design decision이나 구현 token이 확인되면 기록한다.
- Breakpoint는 단순 수치가 아니라 layout behavior와 함께 기록한다.
- 화면별 구조 차이는 `docs/design/screen-flows.md`에 기록하고, 공용 값은 이 문서에 기록한다.
- `MediaQuery` 또는 layout builder 구현 규칙은 `docs/development/conventions.md`를 따른다.
- Compact, medium, expanded 모두에서 primary action과 recovery path가 접근 가능해야 한다.

## Responsive Component Rules

공용 component가 display 크기에 따라 구조나 variant를 바꾸면 component contract에 기록한다.

| Component | Compact | Medium | Expanded | Notes |
| --- | --- | --- | --- | --- |
|  |  |  |  |  |

Rules:

- Component 내부 responsive behavior는 component contract에 둔다.
- Screen-level 구조 변경은 `docs/design/screen-flows.md`에 둔다.
- Component가 `Stack`/absolute position을 쓰면 overlap 가능성과 safe area를 검증한다.
- Button, tab, input처럼 text가 들어가는 fixed-size component는 min/max width, wrapping, ellipsis 기준을 기록한다.

## Shape And Elevation

| Token | Value | Usage |
| --- | --- | --- |
| `radiusXs` |  | Small controls |
| `radiusSm` |  | Button, input |
| `radiusMd` |  | Card, dialog, sheet |
| `elevationLow` |  | App bar, subtle surface |
| `elevationMd` |  | Dialog, bottom sheet |
| `elevationHigh` |  | Blocking modal only |

Shape rules:

- Card radius는 기존 design system이 다르게 정하지 않았다면 8 logical pixels 이하를 우선한다.
- Page section 자체를 floating card처럼 만들지 않는다.
- Card 안에 또 다른 card를 중첩하지 않는다.
- Elevation은 hierarchy나 blocking 상태를 설명할 때만 사용한다.

## Icon And Asset Rules

| Area | Rule |
| --- | --- |
| Icons | 기존 프로젝트 icon set을 우선한다. |
| Icon-only action | semantic label 또는 tooltip 기준을 기록한다. |
| Assets | File과 directory 이름은 `snake_case`를 따른다. |
| Pubspec/Config | Asset 추가/삭제 시 프로젝트 설정 파일 등록과 사용처를 함께 확인한다. |

Icon rules:

- 익숙한 action은 text button보다 icon button을 우선 고려한다. 예: back, close, search, save, download.
- 낯선 icon이나 위험 action은 text label, tooltip, confirmation을 함께 고려한다.
- Icon 색상만으로 상태를 전달하지 않는다.

## Component Index

공용 component는 반복 사용될 때만 이 문서에 기록한다.

| Component | Variants | States | Source | Notes |
| --- | --- | --- | --- | --- |
| Button | primary, secondary, text, icon, destructive | default, pressed, disabled, loading |  |  |
| Text field | single-line, multiline, search | default, focused, error, disabled |  |  |
| Dialog | default, destructive, loading | open, submitting, dismissed |  |  |
| Bottom sheet | default, action sheet | open, loading, disabled |  |  |
| Card/list item | default, selected, disabled | default, pressed, selected |  |  |
| Empty/error block | empty, error, permission | empty, error, retrying |  |  |

## Component Contract Template

새 공용 component를 추가하거나 component contract가 바뀌면 아래 항목을 기록한다.

```text
Component:
Purpose:
Source file:
Variants:
Required states:
Inputs:
Outputs/events:
Accessibility:
Layout constraints:
Responsive behavior:
Do:
Do not:
Related screens:
Related tests:
```

## Component State Rules

Component 상태는 `docs/design/states.md`의 사용자-visible state와 맞춘다.

| State | Component Requirement |
| --- | --- |
| Default | Primary action과 secondary action이 구분되어야 한다. |
| Pressed/focused | 입력 또는 touch feedback이 보여야 한다. |
| Disabled | 불가능한 이유가 화면 흐름에서 이해되어야 한다. |
| Loading | 중복 submit을 막고 layout width가 흔들리지 않아야 한다. |
| Error | raw exception을 노출하지 않고 복구 action을 제공해야 한다. |
| Permission | 로그인, 권한 요청, 설정 이동, 뒤로가기 중 가능한 action을 제공해야 한다. |

## Button Rules

- Primary button은 한 화면의 주 행동을 명확히 할 때 사용한다.
- Destructive action은 색상만으로 구분하지 않고 copy, icon, confirmation 또는 undo를 함께 고려한다.
- Loading button은 label width가 크게 변하지 않도록 stable size를 유지한다.
- Disabled button은 조건이 명확하지 않으면 helper text나 validation state와 연결한다.
- Icon button은 의미가 명확한 icon과 semantic label을 가진다.

## Form Component Rules

- Field error는 가능한 한 해당 field 가까이에 표시한다.
- Submit 가능 여부는 viewmodel/form state와 연결하고, 중복 submit을 막는다.
- Validation message와 API error message는 같은 위치에 무조건 섞지 않는다.
- Password, token, 개인정보 입력은 log, screenshot, fixture에 노출되지 않게 한다.

## Feedback Component Rules

| Component | Use For | Avoid |
| --- | --- | --- |
| Snackbar/toast | 짧은 non-blocking feedback | 중요한 오류를 잠깐만 보여주기 |
| Dialog | 사용자가 결정을 내려야 하는 blocking case | 단순 안내 남발 |
| Bottom sheet | 선택지, 보조 action, mobile-friendly task | 복잡한 multi-step form 남발 |
| Inline message | Field or section-level error/help | 화면 전체 오류를 작은 문구로 숨기기 |

## Layout Rules

- Controls는 영향을 주는 content 가까이에 둔다.
- 고정 형식 UI는 aspect ratio, min/max, grid track, fixed toolbar height 같은 안정적인 크기 기준을 둔다.
- 긴 텍스트, 작은 화면, text scaling에서 overlap과 overflow를 확인한다.
- 반복 작업 화면은 장식보다 scan, compare, repeated action 효율을 우선한다.
- Page section을 독립 floating card처럼 꾸미기보다 화면 구조와 정보 hierarchy를 우선한다.

## Accessibility Contract

| Area | Requirement |
| --- | --- |
| Touch target | 주요 touch target은 충분히 누르기 쉬운 크기를 유지한다. |
| Semantics | Icon-only, custom interactive widget은 의미를 제공한다. |
| Focus | Text field, dialog, sheet, navigation action은 focus 흐름을 고려한다. |
| Contrast | Text/icon이 배경과 구분되어야 한다. |
| Text scaling | 주요 flow가 큰 글자 설정에서도 사용 가능해야 한다. |
| Motion | 필수 정보가 animation에만 의존하지 않아야 한다. |

## Verification Notes

Design system 변경은 영향 범위가 넓을 수 있다.

Consider:

- 관련 UI 컴포넌트 test
- affected screen visual verification
- loading, empty, error, disabled 상태 확인
- 작은 화면과 긴 텍스트 확인
- accessibility label, touch target, contrast 확인
- `docs/design/states.md`와 `docs/design/screen-flows.md` 갱신 필요 여부

## Rules

- Reuse existing UI components and local components before adding new ones.
- Add a component rule only when it will be reused.
- Document user-visible state changes in `docs/design/states.md`.
- Do not invent app-specific brand, color, typography, or icon decisions.
- Keep component rules close to actual reusable implementation.
- Prefer small targeted updates over broad design-system rewrites.
