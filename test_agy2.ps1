$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = 'agy'
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.RedirectStandardInput = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.EnvironmentVariables['PYTHONUNBUFFERED'] = '1'

$process = [System.Diagnostics.Process]::Start($psi)
Start-Sleep -Milliseconds 500

$process.StandardInput.WriteLine('help')
$process.StandardInput.Flush()
Start-Sleep -Milliseconds 1500

$process.StandardInput.WriteLine('exit')
$process.StandardInput.Flush()
Start-Sleep -Milliseconds 500

$process.StandardInput.Close()

if (-not $process.HasExited) {
    'HUNG'
    $process.Kill()
} else {
    'EXITED'
    $process.StandardOutput.ReadToEnd()
}
