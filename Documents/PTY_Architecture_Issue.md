# PTY 아키텍처 및 외부 CLI 도구 버퍼링 이슈 (ConPTY 마이그레이션 제안서)

## 1. 문제 개요 (Issue Summary)

웹 터미널 프론트엔드(`app.js`)에서 `agy`(Antigravity CLI), `python`, `vim`과 같은 대화형/양방향(Interactive) 외부 콘솔 프로그램을 실행할 때, 터미널 화면이 멈추고 단순히 줄바꿈만 일어난 뒤 출력이 전혀 나타나지 않는 현상(먹통 버그)이 발생했습니다. 
기존 `dir`, `cd` 등 PowerShell 내장 명령어는 정상 작동하나, 외부 실행 파일을 구동할 때 치명적인 렌더링 불능 상태에 빠집니다.

## 2. 기술적 원인 분석 (Root Cause)

문제의 근본 원인은 백엔드인 `WebPowerShell.Infrastructure.PowerShell.PtyProcessWrapper`의 아키텍처적 한계에 있습니다.

### 2.1. 가짜 PTY (Redirected I/O) 모델의 한계
현재 시스템은 이름만 `PtyProcessWrapper`일 뿐, 실제로는 `System.Diagnostics.Process` 클래스의 `RedirectStandardInput` 및 `RedirectStandardOutput` 파이프를 이용한 **단순 입출력 리다이렉션** 구조입니다. 
이 방식은 진정한 의미의 윈도우 가상 콘솔(Pseudo Console)이 아닙니다.

### 2.2. 외부 콘솔 앱의 출력 버퍼링 (Output Full Buffering)
대부분의 C/C++ 런타임 기반 콘솔 프로그램(예: agy)은 구동 시 자신이 연결된 `stdout`이 모니터 화면(TTY)인지 파이프(Pipe)인지 감지합니다.
*   **TTY 연결 시:** 한 글자가 쓰일 때마다 즉시 화면에 `flush()` 합니다. (Line Buffering / No Buffering)
*   **Pipe 연결 시 (현재 WebTerminal 구조):** 성능을 위해 **풀 버퍼링(Full Buffering)** 모드로 자동 전환하여, 버퍼에 4KB 정도가 꽉 차거나 프로세스가 완전히 종료되기 전까지는 단 한 글자도 밖으로 내보내지 않습니다.

이로 인해 `agy`가 켜져서 입력을 기다리거나 로고를 출력하고 있음에도, 파이프 너머의 프론트엔드 터미널에는 단 1바이트도 도달하지 않아 영원히 멈춰있는 것처럼 보이는 것입니다.

### 2.3. 프론트엔드 땜질(Workaround)의 역효과
백엔드가 진짜 PTY가 아니기 때문에, 프론트엔드(`app.js`)에서 타이핑하는 글자를 실시간(1바이트 스트림)으로 쏠 경우 백엔드에서 글자를 화면에 되비춰주는 **메아리(Echo)**가 동작하지 않습니다. (사용자는 자신이 치는 글자를 볼 수 없는 블라인드 현상).
이를 막고자 프론트엔드에 `로컬 버퍼링(Local Buffering)`과 `에코 필터(Echo Filter)`를 달아 강제로 조합해 쏘는 방식을 사용했으나, 이로 인해 이중 개행(`\r\n\r\n`), 로고 텍스트 파괴, 외부 명령 실행 시 교착 상태(Deadlock) 등 수많은 부작용을 낳았습니다.

## 3. 해결 방안: ConPTY 기반 아키텍처 도입 (Solution)

파이프 기반 우회(Redirect) 방식으로는 이 문제를 영구히 해결할 수 없습니다. 
모든 CLI 프로그램, 인터랙티브 텍스트 에디터(Vim, Nano), TUI(Terminal UI) 기반 툴을 웹 터미널에서 완벽히 렌더링하려면 **Windows Native Pseudo Console (ConPTY) API**를 반드시 이식해야 합니다.

*   **구현 방향:** `CreatePseudoConsole` Win32 API를 P/Invoke로 호출하여, 프로세스 생성 시 진짜 백그라운드 콘솔(Conhost) 버퍼를 할당합니다.
*   **효과:** 
    1. 외부 도구(agy 등)가 WebTerminal을 "진짜 모니터"로 착각하게 되어 즉각적인 `flush`가 일어납니다. (버퍼링 및 멈춤 버그 해결)
    2. 화살표 키, 백스페이스, Tab 자동완성, ANSI 컬러 코드 등 윈도우 콘솔의 네이티브 리드라인(Readline) 기능이 서버 측에서 처리되어 완벽한 싱크가 맞습니다.
    3. 프론트엔드(`app.js`)에 달아둔 거추장스러운 "로컬 버퍼링 및 에코 필터" 코드를 완전히 제거하고, 순수하게 데이터만 넘기고 받는 "Pure PTY Stream" 모드로 동작할 수 있습니다.

## 4. 향후 Action Items

1. **[Backend]** `PtyProcessWrapper.cs` 폐기 및 `ConPtyProcessWrapper.cs` 개발 (P/Invoke 활용).
2. **[Backend]** ConPTY 엔진 적용 후 `TerminalHub.cs` 통신 규격 정비 (UTF-8 인코딩 스트림 방식).
3. **[Frontend]** 백엔드 공사 완료 시점에 맞춰 `app.js`에서 로컬 에코를 끄고 Xterm.js 순수 스트림 모드로 복구.
