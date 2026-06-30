# WebTerminal ConPTY 기반 아키텍처 및 세션 관리 가이드라인

본 문서는 `agy` 등 네이티브 대화형 명령어 지원을 위해 도입한 ConPTY 기반 백엔드 아키텍처를 프로덕션 수준(Production-ready)으로 고도화하기 위한 설계 원칙과 가이드라인을 담고 있습니다. (AI 아키텍처 분석 결과 기반)

## 1. 기본 설계 원칙
**백엔드는 ‘명령어 실행 엔진’이 아니라 ‘원격 터미널 세션 엔진’으로 설계해야 합니다.**
`agy`를 특별한 방식으로 실행하는 로직을 엔진 내부에 하드코딩하는 대신, **ConPTY 기반 범용 터미널 엔진 위에 AGY 실행 프로필을 얹는 구조**가 가장 안정적입니다.

## 2. 권장 전체 구조

```text
Browser
 └─ xterm.js
      │
      │ SignalR (Input / Output / Resize / Close)
      ▼
TerminalHub
      │
      ▼
TerminalSessionManager
      │
      ├─ Session ownership
      ├─ Lifecycle / timeout
      ├─ Connection reconnect
      └─ Session limits
      │
      ▼
TerminalSession
      │
      ├─ Input queue
      ├─ Output pump
      ├─ Resize handling
      └─ Cancellation
      │
      ▼
ITerminalProcess
      │
      └─ WindowsConPtyProcess
            │
            ├─ CreatePseudoConsole
            ├─ CreateProcessW
            ├─ ResizePseudoConsole
            ├─ Input pipe
            └─ Output pipe
                  │
                  ▼
          pwsh.exe / agy.exe
```

> **핵심 원칙**: SignalR 연결 하나가 프로세스 하나를 직접 관리하면 안 됩니다. 독립적인 `TerminalSession`이 ConPTY 프로세스를 소유하고 관리해야 브라우저 재연결 시 세션 유실을 막을 수 있습니다.

---

## 3. 세부 가이드라인

### 3.1. 범용 PTY 엔진 유지 (ITerminalProcess)
엔진 코드는 AGY 전용으로 오염시키지 않고 범용 터미널 인터페이스(`ITerminalProcess`)로 유지합니다. 실행 조건(실행 파일, 작업 디렉터리, 환경변수)은 `TerminalLaunchOptions` 객체로 분리하여 주입받습니다. 이를 통해 PowerShell, CMD, Python, AGY 등을 동일한 엔진으로 지원할 수 있습니다.

### 3.2. 터미널 프로필(Profile) 분리
실행 목적에 따라 프로필을 구분합니다.
- **프로필 A (일반 터미널)**: `pwsh.exe -NoLogo -NoProfile`로 실행. 범용적인 쉘 작업 및 수동 AGY 실행에 적합.
- **프로필 B (AGY 전용)**: `agy.exe`를 ConPTY의 루트 자식 프로세스로 직접 실행. 서비스형 UI에 적합.

### 3.3. SignalR 프로토콜 설계
단일 완성형 명령어(`SendCommand`) 대신 터미널 이벤트 기반으로 전환합니다.
- **클라이언트 -> 서버**: `CreateSession`, `AttachSession`, `SendInput(byte[])`, `Resize(cols, rows)`, `CloseSession`
- **서버 -> 클라이언트**: `TerminalOutput(byte[])`, `TerminalExited`, `TerminalError`
- **최적화**: SignalR에서 JSON 배열 대신 MessagePack을 사용하여 바이너리 Raw Input/Output 패킷 오버헤드를 줄입니다.

### 3.4. 세션 생명주기 및 연결(Connection) 분리
브라우저 탭이 새로고침되거나 네트워크가 끊겨도 프로세스가 즉시 죽지 않도록 **연결 유예 시간(Grace Period)**을 둡니다.
- SignalR Disconnect 발생 시 세션을 즉시 종료(Dispose)하지 않고 `Detached` 상태로 전환.
- 일정 시간(예: 60초) 내 재접속 시 기존 세션으로 `Attach`. 유예 시간 초과 시에만 완전 종료.

### 3.5. 입출력 동시성 제어 (Concurrency Control)
다수의 SignalR 요청이 파이프에 무질서하게 쓰이지 않도록 입력 스트림을 직렬화합니다.
- `System.Threading.Channels`를 사용하여 `InputChannel` 큐 생성.
- 단일 `InputPump` Task가 큐에서 데이터를 꺼내어 ConPTY 파이프에 안전하게 기록.

### 3.6. 터미널 크기 조정 (Resize) 지원
프론트엔드(xterm.js)의 창 크기가 변할 때마다 서버로 Resize 이벤트를 보내고, 서버는 `ResizePseudoConsole` Win32 API를 호출해야 합니다. 이를 누락하면 전체 화면 TUI(agy 등)의 UI 레이아웃이 깨지게 됩니다.

### 3.7. 권한 및 환경변수(Environment) 격리
백엔드가 시스템 서비스(IIS, Windows Service) 계정으로 구동될 때, AGY의 인증 정보나 프로필 파일 경로(`USERPROFILE`, `APPDATA`)가 개발 환경과 다를 수 있습니다.
- 백엔드 실행 계정의 프로필을 점검하고, 필요한 경우 전용 계정 혹은 올바른 작업 디렉터리를 명시적으로 바인딩해야 합니다.

### 3.8. 하위 프로세스 완벽 종료 (Windows Job Object)
PowerShell 내에서 띄운 `agy` 등 자식 프로세스가 고아(Orphan) 상태로 남는 것을 방지하기 위해, ConPTY 프로세스를 생성할 때 **Windows Job Object (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`)**로 묶어 관리해야 합니다. 세션 종료 시 Job Handle을 닫으면 프로세스 트리가 깔끔하게 일괄 종료됩니다.

## 4. 권장 구현 우선순위 (Roadmap)

- **P0 (필수 코어)**: `ITerminalProcess` 추상화, Channel 기반 입출력 직렬화, `ResizePseudoConsole` 지원, 안전한 네이티브 Handle 정리(`SafeHandle`)
- **P1 (AGY 통합)**: 일반 PowerShell 및 AGY 전용 프로필 구성, 서비스 계정 환경변수 최적화, 한글(UTF-8) 입출력 및 제어 문자(Ctrl+C) 검증
- **P2 (서비스 안정화)**: SessionManager 유예 시간 도입, 연결 세션 수 제한 제어, 구조화된 로그 도입 (명시적 감사 모드가 아니면 터미널 출력 내용을 로깅하지 않음)
- **P3 (고도화)**: MessagePack 바이너리 프로토콜 도입, xterm.js 화면 버퍼 복원 메커니즘 추가

## 결론
현재 달성된 `ConPtyEngine` 베이스캠프 위에서, 위 가이드라인의 **세션 관리, Resize 연동, 프로세스 트리 종료, 보안 경계**를 보강하여 완전한 프로덕션 레벨의 WebTerminal을 구축합니다.
