# Implementation Plan - Phase 2 & 3. Authentication & PowerShell Session

## 1. 개요
본 계획서는 WebPowerShell MVP의 Phase 2(인증 및 보안) 및 Phase 3(PowerShell 세션 및 탭 관리) 기능 구현을 위한 설계 문서이다.

---

## 2. Phase 2. 인증 및 보안 (완료)

### Milestone 1: Password Hashing Infrastructure (done)
- **목적**: 비밀번호의 암호학적 해싱 및 검증 모듈 구축.
- **범위**:
  - `WebPowerShell.Application`에 `IPasswordHasher` 인터페이스 선언.
  - `WebPowerShell.Infrastructure`에 `BCryptPasswordHasher` (BCrypt.Net-Next 사용) 구현.

### Milestone 2: Login UseCase & DTOs (done)
- **목적**: 로그인 비즈니스 로직 구현.
- **범위**:
  - `LoginCommand` 및 `LoginCommandHandler` 구현 (Application).
  - 계정 활성 상태 검사 및 에러 처리 (비활성 또는 비밀번호 불일치 시 `AppFailure.Unauthorized` 반환).
  - 로그인 성공 시 DTO 및 비밀번호 만료 여부 판정 로직.

### Milestone 3: Change Password UseCase (done)
- **목적**: 비밀번호 변경 비즈니스 로직 구현.
- **범위**:
  - `ChangePasswordCommand` 및 `ChangePasswordCommandHandler` 구현 (Application).
  - 기존 비밀번호 일치 검증, 비밀번호 정책 검증.
  - 성공 시 `LastPasswordChangeDate`를 UTC 기준 현재 시각으로 업데이트.

### Milestone 4: Rate Limiting & Expiry Policy (done)
- **목적**: 보안 정책 및 WebAPI 통합.
- **범위**:
  - WebAPI 프로젝트에 HttpOnly 쿠키 발급 및 인증 핸들러/미들웨어 구성.
  - 로그인 실패 횟수 잠금 정책 (`FailedLoginCount`, `LockedUntil`) 및 로그인 API 구현.
  - 168시간(7일) 비밀번호 만료 검사 필터/미들웨어 연동.

---

## 3. Phase 3. PowerShell 세션 및 탭 관리 (진행 예정)

### Milestone 5: PowerShell Session Core Model & Interfaces
- **목적**: PowerShell 세션을 추상화하고 애플리케이션 서비스 경계를 설계한다.
- **범위**:
  - `WebPowerShell.Domain`에 `PowerShellSession` 클래스(세션 ID, Tab ID, 사용자 ID, 마지막 활성 시각 등 메타데이터 정의) 설계.
  - `WebPowerShell.Application`에 `IPowerShellSessionService` 인터페이스 정의.
    - 주요 메서드: `CreateSessionAsync`, `ExecuteCommandAsync`, `StopCommandAsync`, `CloseSessionAsync`, `GetSessionAsync`.
  - 명령 스트리밍 데이터 구조 정의.

### Milestone 6: PowerShell Runspace Infrastructure
- **목적**: `System.Management.Automation` SDK를 활용한 실제 Runspace 및 PowerShell 구동 인프라 구축.
- **범위**:
  - `WebPowerShell.Infrastructure` 프로젝트에 `System.Management.Automation` 패키지 추가.
  - `PowerShellSessionService` 실제 구현 클래스 정의.
  - 각 세션마다 독립된 `Runspace`를 동적으로 생성 및 캐싱 관리하는 In-Memory Session Storage 구축.
  - 세션별 동시 호출 예외 방지를 위해 `SemaphoreSlim`을 적용한 스레드 세이프 동시성 제어 적용.

### Milestone 7: Real-Time Stream Execution & Stop Command
- **목적**: 명령어 실시간 출력 수집 및 중지 기능 구현.
- **범위**:
  - PowerShell Output Stream (표준 출력, 에러 출력)을 비동기식 콜백(`Func<string, CancellationToken, Task>`)을 통해 실시간 스트리밍 처리.
  - `StopCommandAsync` 호출 시 진행 중인 CancellationToken 취소 신호 및 `PowerShell.Stop()` 연동을 통한 정상 중지 및 복구 정책 반영.
- **검증**: `dotnet test`로 로컬 세션 생성, 명령 실행(예: `Get-Location`, `dir`), 스트리밍 수집, 중지 시나리오 단위 테스트 작성.

### Milestone 8: Session Cleanup Worker & Lifetime Management
- **목적**: 유휴 세션 정리 및 리소스 누수 차단.
- **범위**:
  - `.NET BackgroundService` 기반 `SessionCleanupWorker` 클래스 구현 (5분 주기로 스캔, 30분 이상 유휴 세션 탐지하여 Runspace/PowerShell 리소스 Dispose 처리).
  - 애플리케이션 정상 종료 시 (호스트 종료 토큰 수신) 모든 활성 Runspace 세션 Graceful Shutdown 및 정리 처리.
- **검증**: 단위 테스트를 통한 백그라운드 워커 유휴 판정 및 Dispose 호출 검증.

## 4. 의존성 패키지
- `BCrypt.Net-Next` (Phase 2)
- `System.Management.Automation` (Phase 3)
