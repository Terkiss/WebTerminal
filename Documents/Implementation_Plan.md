# Implementation Plan - Phase 2. Authentication & Security

## 1. 개요
본 계획서는 WebPowerShell MVP의 Phase 2 (인증 및 보안) 기능 구현을 위한 설계 문서이다. HttpOnly 쿠키 기반 인증, 로그인 제한, 비밀번호 해싱 및 7일 만료 정책 검사를 포함한다.

## 2. 마일스톤 및 마일스톤 범위

### Milestone 1: Password Hashing Infrastructure
- **목적**: 비밀번호의 암호학적 해싱 및 검증 모듈 구축.
- **범위**:
  - `WebPowerShell.Application`에 `IPasswordHasher` 인터페이스 선언.
  - `WebPowerShell.Infrastructure`에 `BCryptPasswordHasher` (BCrypt.Net-Next 사용) 혹은 Microsoft Identity `PasswordHasher` 구현.
- **검증**: 비밀번호 해싱 후 올바른 비밀번호로 일치 여부 판정 단위 테스트.

### Milestone 2: Login UseCase & DTOs
- **목적**: 로그인 비즈니스 로직 구현.
- **범위**:
  - `LoginCommand` 및 `LoginCommandHandler` 구현 (Application).
  - 계정 활성 상태 검사 및 에러 처리 (User Entity 비활성 또는 비밀번호 불일치 시 `AppFailure.Unauthorized` 반환).
  - 로그인 성공 시 발급할 DTO 및 비밀번호 만료 여부 판정 로직.
- **검증**: `IUserRepository` Mock을 활용한 로그인 UseCase 단위 테스트.

### Milestone 3: Change Password UseCase
- **목적**: 비밀번호 변경 비즈니스 로직 구현.
- **범위**:
  - `ChangePasswordCommand` 및 `ChangePasswordCommandHandler` 구현 (Application).
  - 기존 비밀번호 일치 검증, 비밀번호 정책 검증.
  - 성공 시 `LastPasswordChangeDate`를 현재 시각(UTC)으로 업데이트하고 DB 저장.
- **검증**: 비밀번호 변경 UseCase 단위 테스트.

### Milestone 4: Rate Limiting & Expiry Policy Implementation
- **목적**: 보안 정책(Rate Limiting, 비밀번호 만료일 제한) 및 WebAPI 통합.
- **범위**:
  - WebAPI 프로젝트에 HttpOnly 쿠키 발급 및 인증 핸들러/미들웨어 구성.
  - 로그인 실패 횟수 잠금 정책 (`FailedLoginCount`, `LockedUntil`) 및 로그인 API 구현.
  - 168시간(7일) 비밀번호 만료 검사 필터/미들웨어 연동.
- **검증**: 모의 로그인 API 호출 통합 테스트 및 Rate Limiting 예외 확인.

## 3. 의존성 패키지
- `BCrypt.Net-Next` (비밀번호 해싱용)
