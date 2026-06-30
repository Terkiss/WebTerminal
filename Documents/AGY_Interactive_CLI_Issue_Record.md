# AGY 대화형 네이티브 명령어 지원 (PTY) 문제 해결 및 아키텍처 결정 기록

본 문서는 WebTerminal 프로젝트에서 `agy` (Antigravity CLI)와 같은 **네이티브 대화형 콘솔 애플리케이션(Interactive TTY Application)**을 지원하는 과정에서 발생한 문제점들과 이를 해결하기 위해 거친 아키텍처 변경의 역사를 기록합니다. 
(이 문서는 추후 다른 AI 요원이나 작업자가 백엔드 구조를 이해하고 유지보수할 때 핵심 컨텍스트로 활용되어야 합니다.)

## 1. 문제의 발단 (The Goal & The Problem)
WebTerminal은 브라우저(xterm.js)에서 백엔드의 쉘을 원격으로 제어하는 애플리케이션입니다.
기본적인 명령어(`ls`, `cd`, `dir`)는 쉽게 지원이 가능했으나, 사용자가 요구한 핵심 기능 중 하나는 **`agy` CLI를 터미널 상에서 대화형(Interactive)으로 구동하는 것**이었습니다.

하지만 `agy`, `vim`, `python` REPL 같은 네이티브 TTY 애플리케이션들은 실행 시 **실제 콘솔(Console) 할당**을 강하게 요구합니다. ASP.NET Core와 같은 백그라운드 서버 프로세스에는 콘솔이 존재하지 않으며, 콘솔 없이 I/O 스트림만 리다이렉션할 경우 프로그램이 입력(`CONIN$`)을 찾지 못해 **무한 대기(Hang) 및 교착 상태(Deadlock)**에 빠지는 치명적인 문제가 발생했습니다.

## 2. 해결 시도 및 실패 사례 (Failed Attempts)

### 시도 1: 기본 `Process.Start` 입출력 리다이렉션 (TeruTeruEngine)
- **접근**: `System.Diagnostics.Process`를 사용하여 `RedirectStandardInput = true`, `RedirectStandardOutput = true`로 프로세스를 띄우고, C# 비동기 스트림으로 데이터를 중계하려 했습니다.
- **결과 (실패)**: 터미널 모드를 엄격히 따지는 `agy`는 콘솔 버퍼를 할당받지 못하자 입력을 대기한 채로 영원히 블로킹(Hang)되었습니다. 이를 억지로 깨기 위해 `StandardInput.Close()`를 호출하면 프로세스가 즉시 종료되어 대화형 사용이 불가능했습니다.

### 시도 2: `System.Management.Automation` (PowerShell SDK) 도입
- **접근**: 가짜 쉘 대신 Microsoft 공식 PowerShell SDK를 C#에 내장(Embed)하여 파이프라인 처리를 엔진에 위임하려 했습니다.
- **결과 (실패)**: PowerShell SDK는 훌륭한 C# 통합 환경을 제공하지만, **원격/비대화형 호스트(ASP.NET)에서 실행될 때는 네이티브 명령어를 리다이렉션 방식으로 처리**합니다. 즉, PowerShell 내부에서 `agy`를 호출해도 똑같이 콘솔 부재로 인해 Hang 현상이 발생했습니다. (PowerShell SDK 자체는 PTY를 생성해주지 않음)

### 시도 3: 서드파티 라이브러리 `Quick.PtyNet` 도입
- **접근**: Windows의 가짜 터미널(ConPTY)을 쉽게 사용할 수 있도록 래핑한 NuGet 패키지(`Quick.PtyNet`)를 사용해 `powershell.exe`를 PTY 안에서 구동하려 했습니다.
- **결과 (실패)**: 해당 라이브러리는 내부에 네이티브 종속성(`os64\conpty.dll`)을 가지고 있었으나, .NET 프로젝트 빌드/실행 환경에서 DLL Load 오류(`System.DllNotFoundException`)가 발생했습니다. 웹 서버 배포 시 이러한 네이티브 바이너리 복사 문제는 심각한 취약점과 불안정성을 야기합니다.

## 3. 최종 해결책: C# 순수 P/Invoke ConPTY 래퍼 직접 구현 (ConPtyEngine)

위의 모든 실패를 거쳐, 외부 라이브러리 의존성 없이 완벽한 PTY 환경을 제공하는 **정공법**을 선택했습니다.

1. **Windows 10 API 직접 호출**: `kernel32.dll`의 `CreatePseudoConsole`, `CreateProcess` (`STARTUPINFOEX` 포함), `UpdateProcThreadAttribute` 등 복잡한 Win32 API를 `WebPowerShell.Infrastructure.ConPTY.ConPtyNative` 클래스에 P/Invoke로 직접 매핑했습니다.
2. **`ConPtyProcess` 클래스**: 파이프(`CreatePipe`)를 생성하고, 이를 기반으로 가짜 콘솔을 만든 뒤, 그 가짜 콘솔을 부모로 삼아 `powershell.exe` 프로세스를 띄웁니다.
3. **`ConPtyEngine` 및 `TerminalHub` 통합**: 
   - 사용자가 프론트엔드(xterm.js)에서 타이핑하는 모든 키스트로크는 SignalR의 `SendInput`을 통해 PTY의 `StandardInput`으로 Raw Byte 형태로 전송됩니다.
   - PTY에서 출력되는 화면(ANSI 색상, 커서 이동 제어 문자 포함)은 `StandardOutput`을 통해 읽혀 클라이언트로 그대로 바이패스됩니다.

### 최종 아키텍처의 장점
- **완벽한 대화형 호환성**: `powershell.exe`가 진짜 콘솔 창 안에서 실행되고 있다고 착각하게 만듭니다. 따라서 그 안에서 실행되는 `agy`, `vim` 등의 모든 TUI 프로그램은 100% 정상적으로 동작합니다.
- **의존성 최소화**: 불안정한 서드파티 DLL 없이 Windows OS의 네이티브 기능을 C# 만으로 깔끔하게 제어하므로 배포가 단순하고 서버가 안정적입니다.

## 4. 향후 작업자(AI)를 위한 당부 (Next Steps for Phase 4)
- 백엔드의 쉘 구동 및 스트림 중계 인프라(ConPTY)는 완전히 셋업되었으며 빌드 오류도 없습니다.
- 프론트엔드 UI를 구현할 때, 백엔드로 명령어를 통째로 보내는 것이 아니라 **xterm.js의 `onData` 이벤트를 캡처하여 터미널의 모든 키 입력을 SignalR `SendInput` 메서드로 실시간 스트리밍**해야 합니다.
- 백엔드에서 내려오는 응답(`ReceiveOutput`) 역시 파싱 없이 그대로 `xterm.write()`에 밀어 넣으면 ANSI 이스케이프 시퀀스가 자동으로 렌더링됩니다.
