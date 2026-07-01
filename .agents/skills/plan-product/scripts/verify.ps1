#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')
Push-Location $Root
try {

$Prefix = '[plan-product]'
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
        Warn "$Path is empty; product decisions may not be captured yet"
    } else {
        Pass "$Path has content"
    }
}

$docs = @(
    'docs/product/problem.md',
    'docs/product/target-users.md',
    'docs/product/mvp-scope.md',
    'docs/product/success-metrics.md',
    'docs/product/roadmap.md',
    'docs/handoff/decisions.md',
    'docs/handoff/open-questions.md',
    'docs/handoff/next-actions.md'
)
foreach ($doc in $docs) { Require-Doc $doc }

if ((Test-Path 'docs/product/mvp-scope.md') -and (Get-Item 'docs/product/mvp-scope.md').Length -gt 0) {
    $content = Get-Content 'docs/product/mvp-scope.md' -Raw
    if ($content -match '(?i)(must|should|could|out-of-scope|out of scope)') {
        Pass 'MVP scope includes prioritization language'
    } else {
        Warn 'MVP scope does not mention must/should/could/out-of-scope'
    }
}

if ((Test-Path 'docs/product/success-metrics.md') -and (Get-Item 'docs/product/success-metrics.md').Length -gt 0) {
    $content = Get-Content 'docs/product/success-metrics.md' -Raw
    if ($content -match '(?i)(metric|measure|success|target|baseline|conversion|retention|activation)') {
        Pass 'success metrics include measurable language'
    } else {
        Warn 'success metrics may not be measurable yet'
    }
}

if ($script:Failures -gt 0) { exit 1 }

if ($Strict -and $script:Warnings -gt 0) {
    Fail 'strict mode treats warnings as failures'
    exit 1
}

Pass "plan-product verification completed with $($script:Warnings) warning(s)"
exit 0

} finally { Pop-Location }
