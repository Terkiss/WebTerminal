#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')
Push-Location $Root
try {

$Prefix = '[design-ui]'
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
        Fail "missing required document: $Path"
    } elseif ((Get-Item $Path).Length -eq 0) {
        Warn "$Path is empty; UI decision context may be missing"
    } else {
        Pass "$Path has content"
    }
}

$docs = @(
    'docs/design/ux-principles.md',
    'docs/design/ui-principles.md',
    'docs/design/design-system.md',
    'docs/design/screen-flows.md',
    'docs/design/states.md',
    'docs/project/constraints.md'
)
foreach ($doc in $docs) { Require-Doc $doc }

if ((Test-Path 'docs/design/states.md') -and (Get-Item 'docs/design/states.md').Length -gt 0) {
    $content = Get-Content 'docs/design/states.md' -Raw
    foreach ($state in @('loading', 'empty', 'error', 'success', 'disabled')) {
        if ($content -match "(?i)$state") {
            Pass "state coverage mentions $state"
        } else {
            Warn "states doc does not mention $state"
        }
    }
}

Info 'Manual visual checklist: layout, overflow, tap target, loading, empty, error, disabled, accessibility, contrast, navigation feedback.'

# Detect project type and run appropriate UI/component tests.
# Supports: pubspec.yaml, package.json, pyproject.toml, or other project config files.
$projectConfig = @('pubspec.yaml', 'package.json', 'pyproject.toml', 'Cargo.toml', 'go.mod', 'pom.xml', 'build.gradle', '*.csproj') |
    Where-Object { Test-Path $_ } | Select-Object -First 1

if ($projectConfig) {
    Pass "Project config detected: $projectConfig"
    Info "Run project-specific UI/component tests (예: flutter test, npm test, pytest)"
} else {
    Info 'No project config file found; skipping UI tests'
}

# Source directory check
if (-not (Test-Path 'lib') -and -not (Test-Path 'src') -and -not (Test-Path 'app')) {
    Warn 'Common source directory (lib, src, app) was not found'
}

# Test directory check
if (-not (Test-Path 'test') -and -not (Test-Path 'tests')) {
    Warn 'no test directory found; use manual visual verification'
}

if ($script:Failures -gt 0) { exit 1 }

if ($Strict -and $script:Warnings -gt 0) {
    Fail 'strict mode treats warnings as failures'
    exit 1
}

Pass "design-ui verification completed with $($script:Warnings) warning(s)"
exit 0

} finally { Pop-Location }
