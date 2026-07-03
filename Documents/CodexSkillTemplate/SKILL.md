---
name: use-webterminal
description: WebTerminal(gpt.dotge.net:5255) 서버에 REST API로 원격 접속하여 세션을 조회하고, PTY 내부의 agy와 통신하며 작업을 지시하는 스킬입니다.
---

# WebTerminal Remote Bridge Skill

이 스킬은 WebTerminal(원격 터미널) 내부에서 동작하는 `agy`와 통신하기 위해 사용합니다.
당신(Codex)은 제공된 PowerShell 모듈을 사용하여 원격 터미널 세션에 명령을 주입하고, 비동기 폴링(Polling) 방식으로 결과를 받아볼 수 있습니다.

## 🛠️ 지원되는 기능 (Helper Scripts)

이 스킬 폴더의 `scripts/WebTerminal.psm1` 모듈에는 다음과 같은 명령어들이 준비되어 있습니다.

### 1. `Connect-WebTerminal`
WebTerminal에 로그인하여 JWT 토큰을 발급받고 환경 변수에 저장합니다.
- **사용법**: `Connect-WebTerminal -Username "dign" -Password "비밀번호"`

### 2. `Get-WebTerminalSessions`
현재 로그인한 계정의 활성 세션 목록을 가져옵니다.
- **사용법**: `$sessions = Get-WebTerminalSessions`

### 3. `Get-WebTerminalOutput`
특정 세션의 현재 터미널 화면(Scrollback) 텍스트를 긁어옵니다. (agy가 켜져 있는지 확인할 때 사용)
- **사용법**: `$output = Get-WebTerminalOutput -SessionId "세션ID"`

### 4. `Send-WebTerminalCommand`
터미널 세션에 텍스트(명령어)를 주입합니다.
- **사용법**: `Send-WebTerminalCommand -SessionId "세션ID" -Command "agy --help"`

### 5. `Wait-WebTerminalResult`
agy가 작업을 마치고 Hook 콜백을 보낼 때까지 주기적으로 폴링하며 대기합니다.
- **사용법**: `$result = Wait-WebTerminalResult -SessionId "세션ID" -TimeoutSeconds 300`

---

## 📝 작업 워크플로우 (표준 지침)

이 스킬을 발동할 때 다음 순서대로 행동하십시오.

1. **로그인**: `Connect-WebTerminal`로 서버에 연결합니다.
2. **세션 탐색**: `Get-WebTerminalSessions`를 호출하여 세션 ID를 파악합니다.
3. **상태 확인**: `Get-WebTerminalOutput`을 사용하여 터미널 화면을 확인하고, `agy` 프롬프트가 보이지 않으면 `Send-WebTerminalCommand`로 `agy` 명령어를 입력해 원격 에이전트를 깨웁니다.
4. **작업 지시**: `Send-WebTerminalCommand`를 통해 원격 `agy`에게 지시할 대화나 명령을 전달합니다.
5. **결과 대기**: 명령 전달 직후 반드시 `Wait-WebTerminalResult`를 실행하여 원격 에이전트가 턴을 마칠 때까지(응답을 반환할 때까지) 안전하게 대기하십시오.
