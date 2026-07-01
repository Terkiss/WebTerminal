#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')
Push-Location $Root
try {

$Prefix = '[operate-app]'
$Strict = if ($env:HARNESS_STRICT -eq '1') { $true } else { $false }
[int]$script:Warnings = 0
[int]$script:Failures = 0

function Pass  { param([string]$msg) Write-Host "$Prefix PASS: $msg" }
function Warn  { param([string]$msg) $script:Warnings++; Write-Warning "$Prefix WARN: $msg" }
function Fail  { param([string]$msg) $script:Failures++; Write-Error "$Prefix FAIL: $msg" -ErrorAction Continue }

function Require-Doc {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        Fail "missing operations document: $Path"
    } elseif ((Get-Item $Path).Length -eq 0) {
        Warn "$Path is empty; operational context may not be captured yet"
    } else {
        Pass "$Path has content"
    }
}

$docs = @(
    'docs/operations/monitoring.md',
    'docs/operations/incident-playbook.md',
    'docs/operations/rollback.md',
    'docs/handoff/current-state.md',
    'docs/handoff/decisions.md',
    'docs/handoff/open-questions.md',
    'docs/handoff/next-actions.md'
)
foreach ($doc in $docs) { Require-Doc $doc }

if ((Test-Path 'docs/operations/incident-playbook.md') -and (Get-Item 'docs/operations/incident-playbook.md').Length -gt 0) {
    $content = Get-Content 'docs/operations/incident-playbook.md' -Raw
    if ($content -match '(?i)(severity|impact|owner|timeline|rollback|escalation)') {
        Pass 'incident playbook includes incident handling language'
    } else {
        Warn 'incident playbook may not include severity, impact, owner, timeline, rollback, or escalation'
    }
}

if ($script:Failures -gt 0) { exit 1 }

if ($Strict -and $script:Warnings -gt 0) {
    Fail 'strict mode treats warnings as failures'
    exit 1
}

Pass "operate-app verification completed with $($script:Warnings) warning(s)"
exit 0

} finally { Pop-Location }
