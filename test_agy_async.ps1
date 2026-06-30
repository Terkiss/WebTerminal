
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "agy"
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.RedirectStandardInput = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

$process = [System.Diagnostics.Process]::Start($psi)

$buffer = New-Object char[] 1024
$task = $process.StandardOutput.ReadAsync($buffer, 0, 1024)
if ($task.Wait(2000)) {
    $output = new-object string $buffer, 0, $task.Result
    "OUTPUT_RECEIVED: $output"
} else {
    "NO_OUTPUT_TIMEOUT"
}

$process.Kill()

