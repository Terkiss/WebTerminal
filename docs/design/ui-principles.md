# UI Principles

## Purpose

- 시각 UI 판단 기준을 기록한다.
- 화면의 정보 구조, 위계, 밀도, interaction feedback, 접근성 기준을 정리한다.
- 구체 token이나 component contract가 아직 없을 때 임시 판단 기준을 제공한다.
- 화면이 작은 screen, 긴 텍스트, text scaling, 다양한 상태에서 깨지지 않게 한다.

## Read When

- layout, spacing, hierarchy, interaction, accessibility 판단이 필요할 때
- display 크기에 따른 responsive layout 판단이 필요할 때
- UI 변경이 화면 밀도, 정보 구조, touch target, semantic label에 영향을 줄 때
- design system에 아직 없는 시각 결정을 임시로 판단해야 할 때
- visual verification 또는 UI review 범위를 정할 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 실제 디자인 결정/구현과 이 문서가 충돌함을 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다. 구체 token이나 component contract는 `docs/design/design-system.md`에 기록한다. 아래 조건은 갱신을 제안해야 하는 신호다.

Update this document when:

- 반복되는 layout, hierarchy, density, interaction, accessibility 판단 기준이 합의된다.
- display 크기에 따른 responsive behavior 또는 화면 구조 변경 기준이 합의된다.
- 특정 UI 패턴이 여러 화면에 반복되어 원칙으로 남길 가치가 생긴다.
- visual verification에서 같은 문제가 반복되어 예방 기준이 필요하다.
- design system token이 아니라 화면 구성 원칙 자체가 바뀐다.

Do not update this document for:

- 특정 색상, font size, radius, component prop 같은 token/contract 변경.
- 한 화면의 일회성 배치 조정.
- 앱별 브랜드나 스타일을 추측한 내용.
- 단순 구현 로그나 테스트 실행 결과.

미확정 UI 결정은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 둔다. 확정된 결정도 사용자 요청 또는 승인 후 `docs/handoff/decisions.md`에 남긴다.

## Related Docs

| Topic | Source |
| --- | --- |
| Design system | `docs/design/design-system.md` |
| UI states | `docs/design/states.md` |
| Screen flows | `docs/design/screen-flows.md` |
| UX principles | `docs/design/ux-principles.md` |
| UI conventions | `docs/development/conventions.md` |

## Boundary

| This Document Owns | Other Documents Own |
| --- | --- |
| Layout, hierarchy, density, interaction 원칙 | Token/component contract: `docs/design/design-system.md` |
| Accessibility와 visual verification 판단 기준 | 상태별 user meaning: `docs/design/states.md` |
| 작은 screen, 긴 텍스트, overflow 예방 기준 | 실제 screen journey: `docs/design/screen-flows.md` |
| Display 크기별 화면 구성 원칙 | Breakpoint/layout token: `docs/design/design-system.md` |
| 시각 구조의 일반 원칙 | Layer/file structure: `docs/architecture/overview.md` |

## Layout

- Controls는 영향을 주는 content 가까이에 둔다.
- 기존 앱 spacing, typography, component pattern을 우선 보존한다.
- 반복 작업 화면은 장식보다 scan, compare, repeated action 효율을 우선한다.
- 한 화면의 primary action과 secondary action이 시각적으로 구분되어야 한다.
- 주요 content는 loading, empty, error 상태에서도 같은 영역 안에서 안정적으로 바뀌어야 한다.
- 화면 section을 floating card처럼 남발하지 않는다.

## Visual Hierarchy

| Area | Rule |
| --- | --- |
| Screen title | 현재 위치와 목적을 이해하게 한다. |
| Section title | 내용 묶음의 차이를 빠르게 스캔할 수 있게 한다. |
| Primary action | 화면 목표와 직접 연결된 action을 가장 분명하게 한다. |
| Secondary action | primary action보다 낮은 위계로 표현한다. |
| Destructive action | 실수 가능성을 줄이는 위치, copy, confirmation을 고려한다. |
| Metadata | 본문보다 낮은 위계로 두되 읽을 수 있어야 한다. |

Hierarchy rules:

- Compact panel, card, toolbar 안에서는 hero-scale type을 사용하지 않는다.
- 같은 위계의 요소는 같은 시각 패턴을 사용한다.
- 중요도를 크기, 위치, spacing, label, state로 함께 표현한다.

## Density And Scanning

앱 화면은 사용자가 반복해서 읽고 조작할 수 있어야 한다.

- List, form, setting, dashboard형 화면은 과한 여백보다 스캔 효율을 우선한다.
- 관련 정보와 action은 같은 시각 그룹에 둔다.
- 긴 label이나 localization 가능성이 있으면 줄바꿈과 max width를 고려한다.
- 숫자, 상태, 날짜, 금액 같은 비교 정보는 정렬과 formatting을 일관되게 둔다.
- Empty나 error 화면에서도 다음 action이 눈에 보여야 한다.

## Responsive Behavior

| Case | Requirement |
| --- | --- |
| Small screen | 주요 content와 primary action이 잘리지 않아야 한다. |
| Medium screen | 여백이 과하게 늘어나지 않고 content grouping이 유지되어야 한다. |
| Expanded screen | content max width, multi-column, side panel 여부를 명확히 해야 한다. |
| Long text | overlap 없이 줄바꿈 또는 생략 기준이 있어야 한다. |
| Text scaling | 핵심 action과 message가 사용 가능해야 한다. |
| Keyboard open | 입력 field와 submit path가 가려지지 않아야 한다. |
| Orientation change | 중요한 state와 입력값을 잃지 않아야 한다. |

Responsive rules:

- Font size를 screen width에 직접 비례시켜 조정하지 않는다.
- Fixed-format UI는 aspect ratio, min/max, fixed toolbar height 같은 안정적인 기준을 둔다.
- Dynamic label, badge, counter, loading text가 layout shift를 만들지 않게 한다.
- Display 크기에 따라 구조가 바뀌어도 user goal, primary action, error recovery는 유지되어야 한다.
- 주요 content 배치에는 절대 위치 기반 layout을 기본값으로 삼지 않는다.
- 겹침이 의도되지 않은 UI는 screen 크기, text scale, keyboard open 상태에서 overlap이 없어야 한다.

## Responsive Structure

Display 크기에 따라 구조 자체가 달라지는 경우에는 화면별로 명확한 계약을 둔다.

| Structure Change | Use When | Record In |
| --- | --- | --- |
| Single column -> two column | 넓은 화면에서 목록과 상세를 동시에 보여줄 때 | `docs/design/screen-flows.md` |
| Bottom action -> side/header action | Primary action 위치가 화면 크기에 따라 바뀔 때 | `docs/design/screen-flows.md` |
| Modal -> side panel | 넓은 화면에서 context를 유지해야 할 때 | `docs/design/screen-flows.md` |
| Compact card -> table/list | 비교 정보가 넓은 화면에서 더 효율적일 때 | `docs/design/design-system.md` or screen doc |
| Hidden secondary content -> visible panel | 작은 화면에서는 접고 넓은 화면에서는 항상 보여줄 때 | `docs/design/screen-flows.md` |

Structure rules:

- 구조가 달라져도 같은 기능의 state 의미는 `docs/design/states.md`와 일치해야 한다.
- 구조 변경은 단순히 "넓어서 더 많이 보여주기"가 아니라 user goal을 더 잘 돕는 경우에만 사용한다.
- Compact layout에서만 접근 가능한 action을 만들지 않는다.
- Expanded layout에서만 보이는 정보가 핵심이면 compact layout의 접근 경로를 함께 제공한다.

## Interaction

- Button은 enabled, disabled, loading, pressed/focused behavior가 분명해야 한다.
- Destructive action은 필요하면 confirmation 또는 undo를 제공한다.
- Form error는 가능한 한 원인이 된 field나 action 가까이에 표시한다.
- Submit 중에는 중복 action을 막는다.
- Navigation이나 submit 후에는 사용자가 결과를 알 수 있어야 한다.
- Gesture-only action은 발견 가능성과 접근성을 함께 고려한다.

## State Presentation

시각 표현은 `docs/design/states.md`의 의미와 맞아야 한다.

| State | UI Principle |
| --- | --- |
| Loading | 화면이 멈춘 것처럼 보이지 않게 하고 layout shift를 줄인다. |
| Empty | 실패처럼 보이지 않게 하고 다음 action을 제공한다. |
| Error | 원인보다 복구 action을 이해하기 쉽게 보여준다. |
| Success | 결과를 확인하게 하되 불필요하게 flow를 막지 않는다. |
| Disabled | 왜 불가능한지 사용자가 추론할 수 있어야 한다. |
| Permission | 로그인, 권한 요청, 설정 이동 등 가능한 action을 제공한다. |

## Accessibility

- Icon-only control은 semantic label 또는 tooltip 기준을 가진다.
- 주요 touch target은 mobile에서 누르기 쉬운 크기를 유지한다.
- 색상만으로 상태를 전달하지 않는다.
- Text/icon contrast가 충분한지 확인한다.
- Focus 흐름은 dialog, bottom sheet, form, navigation에서 사용 가능해야 한다.
- Motion이나 animation에만 중요한 정보를 의존하지 않는다.
- Screen reader 사용자가 action의 결과를 이해할 수 있게 한다.

## Visual Verification

UI 변경은 가능한 범위에서 실제 렌더링을 확인한다.

Check:

- 작은 screen에서 overflow가 없는가?
- 긴 텍스트와 큰 text scale에서 overlap이 없는가?
- loading, empty, error, disabled 상태가 실제로 렌더링되는가?
- icon-only action에 의미가 있는가?
- primary action과 destructive action이 혼동되지 않는가?
- keyboard, dialog, bottom sheet가 주요 content를 가리지 않는가?

## Rules

- 기존 화면 관례가 있으면 redesign 요청 없이 새 시각 언어를 만들지 않는다.
- 구체 token과 component contract는 `docs/design/design-system.md`에 기록한다.
- 상태 의미는 `docs/design/states.md`와 충돌하지 않게 한다.
- 화면 흐름은 `docs/design/screen-flows.md`와 충돌하지 않게 한다.
- Visual issue를 broad layout rewrite로 해결하기 전에 가장 좁은 수정 범위를 찾는다.
