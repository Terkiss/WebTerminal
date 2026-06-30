
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "agy"
$psi.Arguments = "-p `"hello`""
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.RedirectStandardInput = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$process = [System.Diagnostics.Process]::Start($psi)
Start-Sleep -Seconds 10
if (-not $process.HasExited) {
    "HUNG"
    $process.Kill()
} else {
    "EXITED"
    $process.StandardOutput.ReadToEnd()
}

