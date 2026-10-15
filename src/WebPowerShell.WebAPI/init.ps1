param(
    [string]$HomeDir,
    [switch]$IsAdmin
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

        if ($IsAdmin -or $resolved.StartsWith($global:HomeDir, [System.StringComparison]::InvariantCultureIgnoreCase)) {
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

$script:Esc = [char]27
$script:PromptUser = if ($IsAdmin) { "root" } else { Split-Path -Leaf $global:HomeDir }
$script:PromptSymbol = if ($IsAdmin) { "# " } else { "$ " }

# Fast ANSI screen clear and welcome banner
[Console]::Write("${script:Esc}[2J${script:Esc}[H${script:Esc}[36mWelcome to WebTerminal!${script:Esc}[0m`r`n${script:Esc}[90mHome Directory: $global:HomeDir${script:Esc}[0m`r`n")

# Custom Linux-style prompt (single string allocation, zero Write-Host overhead)
function prompt {
    $currentPath = $ExecutionContext.SessionState.Path.CurrentLocation.Path
    $displayPath = $currentPath
    
    # Replace home directory path with ~ and fix slashes
    if ($currentPath.StartsWith($global:HomeDir, [System.StringComparison]::InvariantCultureIgnoreCase)) {
        if ($currentPath.Length -eq $global:HomeDir.Length) {
            $displayPath = "~"
        } else {
            $displayPath = "~" + $currentPath.Substring($global:HomeDir.Length).Replace('\', '/')
        }
    }
    
    return "${script:Esc}[32m${script:PromptUser}@webterminal${script:Esc}[0m:${script:Esc}[34m${displayPath}${script:Esc}[0m${script:PromptSymbol}"
}
