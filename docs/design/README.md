# Design Docs

UX/UI 원칙, 화면 흐름, 사용자-visible 상태, design system 기준을 기록한다. 사용자에게 보이는 화면이나 interaction이 바뀔 때 참고한다.

## Document Index

| Document | Role |
| --- | --- |
| `ux-principles.md` | 사용자 목표, primary action, friction, feedback, recovery, copy 판단 기준을 기록한다. |
| `ui-principles.md` | layout, visual hierarchy, density, responsive behavior, interaction, accessibility 기준을 기록한다. |
| `design-system.md` | color, typography, spacing, breakpoint, responsive layout token, component contract와 공용 UI 기준을 기록한다. |
| `screen-flows.md` | 화면 이동, user journey, route/guard/parameter, display 크기별 layout variant, entry/exit behavior를 기록한다. |
| `states.md` | loading, empty, error, success, disabled, permission 상태와 검증 기준을 기록한다. |

## Rules

- 앱별 화면, 브랜드, token, component 기준은 추측으로 확정하지 않는다.
- 화면 흐름은 `screen-flows.md`, 상태 의미는 `states.md`, 공용 component 기준은 `design-system.md`에 기록한다.
- Display 크기별 화면 구조 차이는 `screen-flows.md`, 공용 breakpoint/layout token은 `design-system.md`에 기록한다.
- 사용자-visible UI 변경은 가능한 visual verification 또는 대체 검증을 남긴다.
