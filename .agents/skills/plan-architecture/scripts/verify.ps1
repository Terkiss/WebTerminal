#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')
Push-Location $Root
try {

$Prefix = '[plan-architecture]'
$Strict = if ($env:HARNESS_STRICT -eq '1') { $true } else { $false }
[int]$script:Warnings = 0
[int]$script:Failures = 0

function Pass  { param([string]$msg) Write-Host "$Prefix PASS: $msg" }
function Warn  { param([string]$msg) $script:Warnings++; Write-Warning "$Prefix WARN: $msg" }
function Fail  { param([string]$msg) $script:Failures++; Write-Error "$Prefix FAIL: $msg" -ErrorAction Continue }

function Require-Doc {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        Fail "missing required document: $Path"
    } elseif ((Get-Item $Path).Length -eq 0) {
        Warn "$Path is empty; architecture decision context may be missing"
    } else {
        Pass "$Path has content"
    }
}

$docs = @(
    'docs/architecture/overview.md',
    'docs/architecture/api.md',
    'docs/architecture/data-model.md',
    'docs/architecture/auth-permissions.md',
    'docs/development/testing.md',
    'docs/project/constraints.md'
)
foreach ($doc in $docs) { Require-Doc $doc }

if (Test-Path 'docs/architecture/adr') {
    Pass 'ADR directory exists'
    $adrFiles = Get-ChildItem -Path 'docs/architecture/adr' -Filter '*.md' -Recurse -ErrorAction SilentlyContinue
    if ($adrFiles) {
        Pass 'ADR directory contains markdown files'
    } else {
        Warn 'ADR directory has no markdown files'
    }
} else {
    Fail 'missing ADR directory: docs/architecture/adr'
}

if ((Test-Path 'docs/architecture/auth-permissions.md') -and (Get-Item 'docs/architecture/auth-permissions.md').Length -gt 0) {
    $content = Get-Content 'docs/architecture/auth-permissions.md' -Raw
    if ($content -match '(?i)(auth|permission|role|privacy|token|credential)') {
        Pass 'auth-permissions doc includes security language'
    } else {
        Warn 'auth-permissions doc may not describe auth, permissions, or privacy'
    }
}

if ($script:Failures -gt 0) { exit 1 }

if ($Strict -and $script:Warnings -gt 0) {
    Fail 'strict mode treats warnings as failures'
    exit 1
}

Pass "plan-architecture verification completed with $($script:Warnings) warning(s)"
exit 0

} finally { Pop-Location }
