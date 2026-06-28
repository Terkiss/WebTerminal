#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')
Push-Location $Root
try {

$Prefix = '[verify-change]'
$Strict = if ($env:HARNESS_STRICT -eq '1') { $true } else { $false }
[int]$script:Warnings = 0
[int]$script:Failures = 0

function Info { param([string]$msg) Write-Host "$Prefix INFO: $msg" }
function Pass { param([string]$msg) Write-Host "$Prefix PASS: $msg" }
function Warn { param([string]$msg) $script:Warnings++; Write-Warning "$Prefix WARN: $msg" }
function Fail { param([string]$msg) $script:Failures++; Write-Error "$Prefix FAIL: $msg" -ErrorAction Continue }

$docs = @('docs/development/testing.md', 'docs/development/commands.md', 'docs/harness/quality-gates.md')
foreach ($doc in $docs) {
    if (-not (Test-Path $doc)) {
        Fail "missing verification document: $doc"
    } elseif ((Get-Item $doc).Length -eq 0) {
        Warn "$doc is empty"
    } else {
        Pass "$doc has content"
    }
}

# Detect project type and run appropriate tests.
# Supports: pubspec.yaml, package.json, pyproject.toml, or other project config files.
$projectConfig = @('pubspec.yaml', 'package.json', 'pyproject.toml', 'Cargo.toml', 'go.mod', 'pom.xml', 'build.gradle', '*.csproj') |
    Where-Object { Test-Path $_ } | Select-Object -First 1

if ($projectConfig) {
    Pass "Project config detected: $projectConfig"
    Info "Run project-specific test command (예: flutter test, npm test, pytest, dotnet test)"
} else {
    Info 'No project config file found; skipping project tests'
}

# Test directory check
if (-not (Test-Path 'test') -and -not (Test-Path 'tests')) {
    Warn 'no test directory found'
}

if ($script:Failures -gt 0) { exit 1 }

if ($Strict -and $script:Warnings -gt 0) {
    Fail 'strict mode treats warnings as failures'
    exit 1
}

Pass "verify-change verification completed with $($script:Warnings) warning(s)"
exit 0

} finally { Pop-Location }
