# Implementation Plan - Phase 2, 3 & 4. Authentication, Session & WebAPI/SignalR Integration

## 1. 개요
본 계획서는 WebPowerShell MVP의 Phase 2(인증 및 보안), Phase 3(PowerShell 세션 및 탭 관리), Phase 4(SignalR Hub 및 프론트엔드 연동) 기능 구현을 위한 설계 문서이다.

---

## 2. Phase 2 & 3. (완료)
... (Phase 2 & 3 마일스톤 1 ~ 8 완료 상태) ...

---

## 3. Phase 4. SignalR Hub 및 프론트엔드 연동 (진행 예정)

### Milestone 9: TerminalHub & Hub Authentication Setup
- **목적**: 실시간 통신을 위한 SignalR Hub 구축 및 인증 설정.
- **범위**:
  - `WebPowerShell.WebAPI` 프로젝트에 `TerminalHub` 생성 및 `/hubs/terminal` 라우팅 매핑.
  - SignalR 연결(Negotiate 및 WebSocket 연결) 시 HttpOnly 쿠키 인증 토큰을 통해 사용자를 식별하고 미인증 접속을 차단.
- **검증**: 모의 SignalR 클라이언트를 이용한 인증/미인증 접속 격리 단위/통합 테스트.

### Milestone 10: SignalR Hub Commands & Session Ownership
- **목적**: 클라이언트 요청 처리 및 세션 소유권 검증.
- **범위**:
  - `TerminalHub` 내에 `OpenTab`, `SendCommand`, `StopCommand`, `CloseTab` 메서드 정의.
  - 각 요청 시 `Context.ConnectionId`와 사용자 ID를 기반으로 해당 탭 세션에 대한 소유권을 확인하여 다른 사용자의 세션 조작 차단 (소유권 실패 시 `TerminalError` 반환).
  - `IPowerShellSessionService`와 Hub 메서드 유기적 결합.

### Milestone 11: Real-Time Stream Event Routing
- **목적**: PowerShell Output Stream을 SignalR 이벤트를 통해 해당 클라이언트로 전달.
- **범위**:
  - `IPowerShellSessionService`에서 반환되는 출력 채널 데이터를 읽어 Hub 컨텍스트의 `Clients.Caller`에게 `ReceiveOutput` 및 `ReceiveError` 이벤트로 실시간 송신.
  - 명령 시작 및 완료 이벤트를 `CommandStarted`, `CommandCompleted`로 실시간 스트리밍 피드백 처리.

### Milestone 12: Static Frontend with xterm.js & Multi-Tab UI
- **목적**: xterm.js 기반의 다중 탭 웹 터미널 UI 구현.
- **범위**:
  - `wwwroot` 디렉터리에 로그인 화면, 비밀번호 변경 화면, 메인 터미널 화면 정적 리소스(HTML, Vanilla CSS, JS) 구현.
  - xterm.js(CDN 호출) 터미널 객체 동적 초기화.
  - 다중 탭 생성, 탭 전환 시 기존 xterm.js 인스턴스 버퍼 유지, 탭 닫기 UI 연동.
  - SignalR JavaScript Client를 연동하여 터미널 입출력 스트림 바인딩.
- **검증**: 로컬 호스트 가동을 통한 실제 브라우저 다중 탭 동시 구동 및 입력 명령어 에코 스트리밍 작동 검증.

## 4. 의존성 패키지
- `BCrypt.Net-Next` (Phase 2)
- `System.Management.Automation` (Phase 3)
