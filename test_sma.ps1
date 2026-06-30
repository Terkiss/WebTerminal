
using namespace System.Management.Automation
$ps = [PowerShell]::Create()
$ps.AddScript("agy -p `"help`"")
$results = $ps.Invoke()
if ($ps.HadErrors) {
    "ERRORS:"
    $ps.Streams.Error | ForEach-Object { $_.ToString() }
}
"RESULTS:"
$results | ForEach-Object { $_.ToString() }

