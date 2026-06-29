# Implementation Plan - Revised (MVP Roadmap)

## 1. 개요
이전 PTY 백엔드 리팩토링 과정에서 발생한 자원 고갈 및 에이전트 교착 상태를 교훈 삼아, 프로젝트 안정성을 최우선으로 확보하고 중단된 인증(Phase 2)과 프론트엔드 연동(Phase 4)을 완수하기 위한 재조정된 마일스톤 계획서입니다.

## 2. 현재 상태 요약
- **성공 완료**: Phase 1(기본 구조), Phase 3(기존 PowerShell Session), PTY 백엔드 리팩토링 일부 (Milestone 1~3, Commit: `fac85344`)
- **중단 및 미완료**: Phase 2(인증/보안), Phase 4(프론트엔드/SignalR), PTY Milestone 4(안정성 및 자원 회수)

## 3. 남은 마일스톤 재정렬 (실행 순서)

### Milestone A: PTY 백엔드 안정성 확보 및 자원 회수 (최우선)
*기존 PTY Milestone 4에 해당*
- **목표**: 에이전트 교착 및 자원 고갈의 원인이 된 프로세스 관리 및 생명주기 문제 해결.
- **범위**:
  - 클라이언트 연결 끊김(SignalR Disconnect) 또는 비정상 종료 시 할당된 PTY 프로세스 강제 종료(Kill) 및 자원 완전 회수.
  - 메모리 누수 및 좀비 프로세스 방지 로직(`IDisposable` 패턴 철저 구현) 강화.
  - `agy`, `vim` 등 TUI 프로그램 실행 시 예외 상황 방어 및 엣지 케이스 처리.

### Milestone B: Phase 2 인증 및 보안 완수
*기존 Phase 2 잔여 과제*
- **목표**: WebPowerShell 접근 제어 및 사용자 보안 확립.
- **범위**:
  - JWT 또는 Cookie 기반 Login API 및 세션 발급 로직 완료.
  - 사용자 비밀번호 변경(Change Password) API 기능 구현.
  - 무차별 대입 공격 방지를 위한 로그인 Rate Limiting 적용.

### Milestone C: Phase 4 SignalR Hub & 인증 통합
*기존 Phase 4 Milestone 9, 10, 11 통합*
- **목표**: 안전한 실시간 통신 채널 구축 및 세션 격리.
- **범위**:
  - `TerminalHub` 구축 및 HttpOnly 쿠키/토큰을 통한 연결 인증(Hub Authentication) 적용.
  - 탭 세션 소유권 검증 (다른 사용자의 세션 조작 원천 차단).
  - PTY 백엔드의 스트림 데이터(출력/에러)를 클라이언트로 라우팅(Real-Time Stream Event Routing) 및 명령어 제어.

### Milestone D: Phase 4 프론트엔드 완성 (xterm.js & Multi-Tab UI)
*기존 Phase 4 Milestone 12*
- **목표**: 사용자 대면 다중 탭 웹 터미널 인터페이스 제공.
- **범위**:
  - 로그인 및 비밀번호 변경 UI 페이지 구성.
  - 정적 리소스(HTML, CSS, JS) 기반 xterm.js 동적 초기화.
  - 다중 탭 UI 구현 및 탭 전환 시 터미널 버퍼/상태 유지.
  - SignalR 클라이언트 연동을 통한 터미널 입출력 실시간 스트리밍 시각화.

## 4. 추천 진행 순서 (빠르고 안정적인 MVP 달성 로드맵)
1. **백엔드 안정화 (Milestone A)**: 가장 치명적이었던 시스템 자원 고갈 문제를 우선 차단합니다. 백엔드가 클라이언트의 이탈이나 오류에도 스스로를 보호(안전한 자원 회수)할 수 있어야 이후 개발과 테스트가 안전합니다.
2. **보안 인프라 확립 (Milestone B)**: 프론트엔드와 통신 Hub를 완성하기 전에, 기반이 되는 인증 체계(Login, Rate Limiting)를 먼저 완성합니다.
3. **통신 채널 및 UI 완성 (Milestone C -> D)**: 인증이 보장된 상태에서 SignalR 통신을 열고, 최종적으로 브라우저(xterm.js)에 UI를 렌더링하여 프론트엔드 연동을 마무리합니다.
