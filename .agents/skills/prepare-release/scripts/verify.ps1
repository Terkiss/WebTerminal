#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')
Push-Location $Root
try {

$Prefix = '[prepare-release]'
$Strict = if ($env:HARNESS_STRICT -eq '1') { $true } else { $false }
[int]$script:Warnings = 0
[int]$script:Failures = 0

function Info { param([string]$msg) Write-Host "$Prefix INFO: $msg" }
function Pass { param([string]$msg) Write-Host "$Prefix PASS: $msg" }
function Warn { param([string]$msg) $script:Warnings++; Write-Warning "$Prefix WARN: $msg" }
function Fail { param([string]$msg) $script:Failures++; Write-Error "$Prefix FAIL: $msg" -ErrorAction Continue }

function Require-Doc {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        Fail "missing release document: $Path"
    } elseif ((Get-Item $Path).Length -eq 0) {
        Warn "$Path is empty; fill it before real release work"
    } else {
        Pass "$Path has content"
    }
}

$docs = @(
    'docs/operations/release-checklist.md',
    'docs/operations/rollback.md',
    'docs/operations/monitoring.md',
    'docs/handoff/decisions.md',
    'docs/handoff/open-questions.md',
    'docs/handoff/next-actions.md'
)
foreach ($doc in $docs) { Require-Doc $doc }

$releaseConfigs = @('firebase.json', '.firebaserc', 'fastlane', 'codemagic.yaml', '.github/workflows', '.gitlab-ci.yml', '.circleci')
foreach ($path in $releaseConfigs) {
    if (Test-Path $path) {
        Warn "release-affecting config exists: $path; confirm rollback and approval before deploy"
    }
}

$fastlaneDirs = Get-ChildItem -Path . -Directory -Recurse -Filter 'fastlane' -Depth 3 -ErrorAction SilentlyContinue
if ($fastlaneDirs) {
    Warn 'release-affecting Fastlane tooling exists; confirm rollback and approval before deploy'
}

Info 'Release readiness checklist: target, build, signing, smoke test, rollback trigger, monitoring, owner approval.'
Info 'This verification script never runs deployment commands.'

if ($script:Failures -gt 0) { exit 1 }

if ($Strict -and $script:Warnings -gt 0) {
    Fail 'strict mode treats warnings as failures'
    exit 1
}

Pass "prepare-release verification completed with $($script:Warnings) warning(s)"
exit 0

} finally { Pop-Location }
