# Handoff Docs

Antigravity/AGY가 작업 중 갱신 필요성을 보고하고, 사용자 요청 또는 승인 후 현재 상태, 확정 결정, 열린 질문, 다음 액션을 기록하는 공간이다. 이 폴더는 프로젝트-local convention이며 공식 Antigravity system directory가 아니다.

`docs/handoff/*`는 append-only log가 아니라 rolling snapshot이다. 오래 유지될 규칙과 반복 참조할 기준은 handoff에 계속 쌓지 않고, 사용자가 domain docs로 옮기기 쉽게 `Domain Insert Candidate` 형태로 정리한다.

## Document Index

| Document | Role |
| --- | --- |
| `current-state.md` | 다음 세션이 바로 이어가기 위한 최신 상태 snapshot |
| `decisions.md` | 현재 유효한 결정과 domain docs로 옮길 결정 후보 |
| `open-questions.md` | 답이 없으면 진행이 위험하거나 애매한 질문 |
| `next-actions.md` | 다음에 실행 가능한 후보 작업 |

## Rules

- Antigravity/AGY는 갱신 필요성을 능동적으로 보고할 수 있지만, 실제 기록은 사용자 요청 또는 승인 후 수행한다.
- Blocking question은 먼저 대화에서 사용자에게 직접 묻고, 사용자 요청 또는 승인 후 기록한다.
- 완료/폐기/이동된 항목은 오래 남기지 않는다.
- `current-state.md`는 최신 이어받기 상태만 남긴다.
- `decisions.md`, `open-questions.md`, `next-actions.md`는 현재 유효한 카드만 유지한다.
- Domain docs로 옮기는 최종 결정권은 사용자에게 있다. Antigravity/AGY는 후보를 정리하고, 사용자 요청 또는 승인 후에만 반영한다.

## Size Limit

| Document | Limit |
| --- | --- |
| `current-state.md` | 30줄 안팎 |
| `decisions.md` | active decision 3-5개 이하 |
| `open-questions.md` | active question 5개 이하 |
| `next-actions.md` | active action 5개 이하 |

## Domain Insert Candidate

오래 유지될 결정이나 규칙은 아래 형식으로 임시 정리한다. 사용자는 `Target doc`과 `Insert section`을 보고 `Content`만 domain docs로 옮기거나, Antigravity/AGY에게 승인 후 반영을 요청할 수 있다.

```md
### Candidate: Short title

- Status: proposed | approved | moved | rejected
- Target doc: `docs/...`
- Insert section: `## Section Name`
- Related doc:
- Reason:

#### Content

- Domain docs로 옮길 본문만 작성한다.
```
