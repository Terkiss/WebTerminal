
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "agy"
$psi.Arguments = "-p `"help`""
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.RedirectStandardInput = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

$process = [System.Diagnostics.Process]::Start($psi)

$process.WaitForExit(60000)
if (-not $process.HasExited) {
    "HUNG"
    $process.Kill()
} else {
    "EXITED"
    $process.StandardOutput.ReadToEnd()
}

