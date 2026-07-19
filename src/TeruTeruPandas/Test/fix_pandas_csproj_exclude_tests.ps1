$ErrorActionPreference = "Stop"

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    $dir = [System.IO.Path]::GetDirectoryName($Path)
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        [System.IO.Directory]::CreateDirectory($dir) | Out-Null
    }
    $enc = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
}

$csprojPath = Join-Path $PSScriptRoot "..\TeruTeruPandas.csproj"
if (-not (Test-Path -LiteralPath $csprojPath)) {
    throw "TeruTeruPandas.csproj not found: $csprojPath"
}

$content = Get-Content -LiteralPath $csprojPath -Raw

# 라이브러리 컴파일에서 테스트 소스(xUnit 프로젝트)를 제외합니다. 제약 조건에 따라 폴더는 디스크에 유지됩니다.
if ($content -notmatch '<Compile\s+Remove=\"Test\\\\\*\*\\\\\*\.cs\"') {
    $insertion = @'

  <ItemGroup>
    <Compile Remove="Test\**\*.cs" />
  </ItemGroup>
'@

    if ($content -match '</Project>\s*$') {
        $content = $content -replace '</Project>\s*$', ($insertion + "`r`n</Project>`r`n")
    }
    else {
        # 폴백(Fallback): 파일 끝에 추가.
        $content = $content + $insertion
    }

    Write-Utf8NoBom -Path $csprojPath -Content $content
    Write-Host ("Updated: " + $csprojPath)
}
else {
    Write-Host ("No change needed: " + $csprojPath)
}

