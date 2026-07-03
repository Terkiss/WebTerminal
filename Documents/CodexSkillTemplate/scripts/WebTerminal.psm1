$global:WebTerminalApiUrl = "http://gpt.dotge.net:5255/api"
$global:WebTerminalToken = $null

function Connect-WebTerminal {
    param (
        [string]$Username,
        [string]$Password,
        [string]$Url = "http://gpt.dotge.net:5255"
    )

    $global:WebTerminalApiUrl = "$Url/api"
    $loginData = @{
        username = $Username
        password = $Password
    } | ConvertTo-Json

    $response = Invoke-RestMethod -Uri "$global:WebTerminalApiUrl/auth/token" `
                                  -Method Post `
                                  -ContentType "application/json" `
                                  -Body $loginData
    
    if ($response.token) {
        $global:WebTerminalToken = $response.token
        Write-Output "Successfully connected to WebTerminal. Token acquired."
    } else {
        Write-Error "Failed to acquire token from WebTerminal."
    }
}

function Get-WebTerminalHeaders {
    if (-not $global:WebTerminalToken) {
        throw "Not connected. Run Connect-WebTerminal first."
    }
    return @{
        "Authorization" = "Bearer $($global:WebTerminalToken)"
    }
}

function Get-WebTerminalSessions {
    $headers = Get-WebTerminalHeaders
    $sessions = Invoke-RestMethod -Uri "$global:WebTerminalApiUrl/sessions/me" `
                                  -Method Get `
                                  -Headers $headers
    return $sessions
}

function Get-WebTerminalOutput {
    param (
        [Parameter(Mandatory=$true)]
        [string]$SessionId
    )
    $headers = Get-WebTerminalHeaders
    $output = Invoke-RestMethod -Uri "$global:WebTerminalApiUrl/sessions/$SessionId/output" `
                                -Method Get `
                                -Headers $headers
    return $output
}

function Send-WebTerminalCommand {
    param (
        [Parameter(Mandatory=$true)]
        [string]$SessionId,
        [Parameter(Mandatory=$true)]
        [string]$Command
    )
    $headers = Get-WebTerminalHeaders
    # 입력 끝에 엔터(\r\n)가 포함되어야 PTY가 실행합니다.
    $body = @{
        input = "$Command`r`n"
    } | ConvertTo-Json

    $result = Invoke-RestMethod -Uri "$global:WebTerminalApiUrl/sessions/$SessionId/input" `
                                -Method Post `
                                -Headers $headers `
                                -ContentType "application/json" `
                                -Body $body
    return $result
}

function Wait-WebTerminalResult {
    param (
        [Parameter(Mandatory=$true)]
        [string]$SessionId,
        [int]$TimeoutSeconds = 300,
        [int]$PollInterval = 5
    )
    $headers = Get-WebTerminalHeaders
    $elapsed = 0

    while ($elapsed -lt $TimeoutSeconds) {
        try {
            $result = Invoke-RestMethod -Uri "$global:WebTerminalApiUrl/sessions/$SessionId/result" `
                                        -Method Get `
                                        -Headers $headers
            
            # 204 No Content 면 아직 결과가 없는 것으로 간주 (백엔드 설계에 따라 다를 수 있음)
            if ($result) {
                return $result
            }
        } catch {
            # 404 (결과 없음) 등은 폴링 계속
            if ($_.Exception.Response.StatusCode.value__ -ne 404) {
                Write-Error "Polling error: $($_.Exception.Message)"
            }
        }
        
        Start-Sleep -Seconds $PollInterval
        $elapsed += $PollInterval
    }

    Write-Warning "Timeout reached waiting for result from session $SessionId."
    return $null
}

Export-ModuleMember -Function Connect-WebTerminal, Get-WebTerminalSessions, Get-WebTerminalOutput, Send-WebTerminalCommand, Wait-WebTerminalResult
