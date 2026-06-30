 = New-Object System.Diagnostics.ProcessStartInfo
.FileName = 'agy'
.RedirectStandardOutput = $true
.RedirectStandardError = $true
.RedirectStandardInput = $true
.UseShellExecute = $false
.CreateNoWindow = $true
 = [System.Diagnostics.Process]::Start()

Start-Sleep -Seconds 3

if (-not .HasExited) {
    'HUNG'
    .Kill()
} else {
    'EXITED'
}
