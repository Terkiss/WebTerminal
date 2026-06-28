# Rollback

rollback 기준과 복구 절차를 기록한다. rollback 실행은 high-risk 작업이다.

## Rollback Triggers

- Critical crash:
- Auth/payment/privacy issue:
- Data loss or migration failure:
- Severe performance degradation:

## Rollback Plan

- Detection source:
- Decision owner:
- Command or manual step:
- Expected recovery time:

## Verification After Rollback

- App launches:
- Core flow works:
- Monitoring returns to baseline:
- Users/support notified if needed:

## Notes

- If rollback criteria are unclear, do not report release work as complete.
