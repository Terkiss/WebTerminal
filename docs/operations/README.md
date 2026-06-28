# Operations Docs

릴리스, 롤백, 모니터링, 장애 대응 기준을 기록한다. production-impacting 작업이나 운영 피드백을 다룰 때 참고한다.

## Document Index

| Document | Role |
| --- | --- |
| `release-checklist.md` | release target, required checks, approval, release readiness 기준을 기록한다. |
| `rollback.md` | rollback trigger, rollback plan, rollback 후 검증 기준을 기록한다. |
| `monitoring.md` | crash/error/latency/support signal, release watch, privacy 기준을 기록한다. |
| `incident-playbook.md` | incident triage, response step, communication, after action 기준을 기록한다. |

## Rules

- Release, deploy, rollback, production config 변경은 high-risk로 본다.
- 실제 배포나 rollback 실행은 사용자 확인 없이 진행하지 않는다.
- Incident 기록은 관찰된 사실, 진단, 결정, 후속 작업을 분리한다.
