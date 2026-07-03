param(
    [string]$HomeDir
)
$global:HomeDir = $HomeDir

# Enable UTF-8 encoding
[console]::InputEncoding=[console]::OutputEncoding=[System.Text.Encoding]::UTF8

New-Item -ItemType Directory -Force -Path $global:HomeDir | Out-Null
Set-Location $global:HomeDir

Remove-Item alias:cd -Force -ErrorAction SilentlyContinue
Remove-Item alias:chdir -Force -ErrorAction SilentlyContinue
Remove-Item alias:sl -Force -ErrorAction SilentlyContinue

function Set-Location-Safe {
    [CmdletBinding()]
    param(
        [Parameter(Position=0, ValueFromPipeline=$true, ValueFromPipelineByPropertyName=$true)]
        [string]$Path
    )
    process {
        if (-not $Path) { $Path = $global:HomeDir }
        
        $resolved = $null
        try {
            $resolved = (Resolve-Path -Path $Path -ErrorAction Stop).Path
        } catch {
            $resolved = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine((Get-Location).Path, $Path))
        }

        if ($resolved.StartsWith($global:HomeDir, [System.StringComparison]::InvariantCultureIgnoreCase)) {
            Microsoft.PowerShell.Management\Set-Location -Path $Path
        } else {
            Write-Host "Access Denied: Cannot navigate outside home directory." -ForegroundColor Red
        }
    }
}

Set-Alias -Name cd -Value Set-Location-Safe -Option AllScope -Force
Set-Alias -Name chdir -Value Set-Location-Safe -Option AllScope -Force
Set-Alias -Name sl -Value Set-Location-Safe -Option AllScope -Force
Set-Alias -Name Set-Location -Value Set-Location-Safe -Option AllScope -Force

# Clear screen for cleaner startup
Clear-Host
Write-Host "Welcome to WebTerminal!" -ForegroundColor Cyan
Write-Host "Home Directory: $global:HomeDir" -ForegroundColor Gray

# Custom Linux-style prompt
function prompt {
    $currentPath = (Get-Location).Path
    $displayPath = $currentPath
    
    # Replace home directory path with ~ and fix slashes
    if ($currentPath.StartsWith($global:HomeDir, [System.StringComparison]::InvariantCultureIgnoreCase)) {
        if ($currentPath.Length -eq $global:HomeDir.Length) {
            $displayPath = "~"
        } else {
            $displayPath = "~" + $currentPath.Substring($global:HomeDir.Length).Replace('\', '/')
        }
    }
    
    $username = Split-Path -Leaf $global:HomeDir
    Write-Host "${username}@webterminal" -NoNewline -ForegroundColor Green
    Write-Host ":" -NoNewline
    Write-Host $displayPath -NoNewline -ForegroundColor Blue
    return "$ "
}
