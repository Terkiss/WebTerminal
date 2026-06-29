# Implementation Plan: PTY/Process 기반 PowerShellSessionService 리팩토링

## 개요
기존 `Runspace` 기반의 `PowerShellSessionService`가 가진 TUI(Interactive Console) 프로그램 실행 시의 Hang 현상 및 백엔드 콘솔 침범 문제를 해결하기 위해, `System.Diagnostics.Process` 기반의 PTY(Pseudo-Terminal) 아키텍처로 백엔드 코어 엔진을 전면 리팩토링합니다. 브랜치: `feature/pty-backend`

## 마일스톤 및 Execution Cards

### Milestone 1: Process 기반 PTY 래퍼 설계 및 기본 구현
- **목표**: `powershell.exe` 프로세스를 백그라운드에서 안전하게 실행하고 관리하는 코어 래퍼 클래스 구현.
- **범위**:
  - `Process` 객체 초기화 로직 작성 (`UseShellExecute = false`, `RedirectStandardInput = true`, `RedirectStandardOutput = true`, `RedirectStandardError = true`, `CreateNoWindow = true`).
  - 프로세스 생명주기(Start, Stop, Dispose) 관리 및 예외 처리.
  - 프로세스 비정상 종료 감지(`Exited` 이벤트 연동).

### Milestone 2: 비동기 스트림 중계 파이프라인 구현
- **목표**: 프로세스의 I/O 스트림을 비동기적으로 읽고 쓸 수 있는 안전한 스트림 핸들링 구조 구축.
- **범위**:
  - `RedirectStandardOutput`, `RedirectStandardError`의 비동기 읽기 루프 구현 (별도 Task 또는 비동기 이벤트 기반).
  - TUI 프로그램의 ANSI 이스케이프 시퀀스 처리를 위한 문자열/바이트 인코딩(UTF-8) 및 버퍼링 최적화.
  - `RedirectStandardInput`으로 명령어 및 제어 문자(Ctrl+C 등)를 전달하는 비동기 쓰기 인터페이스 구현.

### Milestone 3: 기존 인터페이스 연동 및 SignalR 통합
- **목표**: 새 Process 래퍼를 기존 웹 터미널 백엔드 구조에 연동하고 SignalR 클라이언트로 데이터 스트리밍.
- **범위**:
  - 기존 `IPowerShellSession` 및 `PowerShellSessionService`를 Process 래퍼 기반으로 전면 교체.
  - SignalR Hub 연동 로직 수정 (기존의 `PSObject` 기반에서 Raw String/Bytes 기반 이벤트로 전환).
  - 프론트엔드(xterm.js)로 스트림 데이터를 실시간 중계하는 Payload 포맷 통일 및 최적화.

### Milestone 4: TUI 애플리케이션 지원 및 안정성 확보
- **목표**: `agy`, `vim` 등 인터랙티브 콘솔 프로그램 동작 확인 및 리소스 정리(안정성) 보장.
- **범위**:
  - 메모리 누수 방지(`IDisposable` 패턴 철저 구현), 좀비 프로세스 방지 로직 강화.
  - 클라이언트 연결 끊김(SignalR Disconnect) 시 할당된 프로세스의 강제 종료(Kill) 및 자원 회수.

---

## Technical Risk 및 보완책
- **Windows PTY 한계**: 단순 `System.Diagnostics.Process`의 `RedirectStandard*` 리다이렉션만으로는 `vim`과 같은 복잡한 TUI 프로그램이 터미널 모드를 제대로 인식하지 못해 화면이 깨지거나 동작하지 않을 수 있습니다. 
- **대안 전략**: 만약 기본 스트림 리다이렉션으로 TUI가 정상 구동되지 않는다면, C#에서 Windows ConPTY (Pseudo Console) API (`CreatePseudoConsole`)를 직접 P/Invoke 하거나 관련 라이브러리(`Pty.Net` 등)를 도입하는 방향으로 Milestone 4에서 아키텍처를 확장해야 합니다. 초기 리팩토링은 `Process` 리다이렉션 기반으로 진행하되, 이 리스크를 염두에 두고 스트림 중계 계층을 유연하게 설계합니다.
