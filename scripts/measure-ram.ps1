<#
.SYNOPSIS
  Measures the memory of the running FocusTimer (same value Task Manager shows).

.DESCRIPTION
  Takes one sample per second for -Seconds and prints the average and maximum of:
    - Private working set  (Working Set - Private; the "Memory" column in Task Manager)
    - Total working set    (Working Set)
    - Private bytes        (commit)
  Usage:
    1) Open FocusTimer and leave it alone ~1 min       ->  .\scripts\measure-ram.ps1 -Label idle
    2) Start a pomodoro, open Dashboard and Settings   ->  .\scripts\measure-ram.ps1 -Label session
  If PowerShell blocks the script:  powershell -ExecutionPolicy Bypass -File .\scripts\measure-ram.ps1
  Reads local counters only; no network.
#>
param(
    [int]$Seconds = 15,
    [string]$Label = "measurement"
)

$p = Get-Process -Name FocusTimer -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Host "FocusTimer is not running." -ForegroundColor Yellow; exit 1 }

$priv = @(); $ws = @(); $commit = @()
for ($i = 0; $i -lt $Seconds; $i++) {
    $p.Refresh()
    $ws += $p.WorkingSet64
    $commit += $p.PrivateMemorySize64
    # "Working Set - Private" is the value Task Manager shows.
    $c = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess = $($p.Id)" -ErrorAction SilentlyContinue
    if ($c) { $priv += [int64]$c.WorkingSetPrivate }
    Start-Sleep -Seconds 1
}

function MB($v) { "{0,7:N1} MB" -f ($v / 1MB) }
function Row($name, $values) {
    if ($values.Count -eq 0) { return "{0,-24} (not available)" -f $name }
    $m = ($values | Measure-Object -Average -Maximum)
    "{0,-24} avg {1}   max {2}" -f $name, (MB $m.Average), (MB $m.Maximum)
}

Write-Host ""
Write-Host "FocusTimer (PID $($p.Id)) - $Label - $Seconds s" -ForegroundColor Cyan
Write-Host (Row "PRIVATE working set" $priv)
Write-Host (Row "Total working set" $ws)
Write-Host (Row "Private bytes (commit)" $commit)
Write-Host "Target: private working set < 40 MB."
