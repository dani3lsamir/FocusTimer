<#
.SYNOPSIS
  Mide la memoria de FocusTimer en ejecución (lo mismo que muestra el Administrador de tareas).

.DESCRIPTION
  Toma una muestra por segundo durante -Segundos y muestra el promedio y el máximo de:
    - Conjunto de trabajo privado  (Working Set - Private; columna "Memoria" del Administrador de tareas)
    - Conjunto de trabajo total    (Working Set)
    - Bytes privados confirmados   (Private Bytes / commit)
  Uso:
    1) Abre FocusTimer y espera ~1 min sin tocarlo  ->  .\scripts\medir-ram.ps1 -Etiqueta reposo
    2) Inicia un pomodoro, abre el Panel y Ajustes  ->  .\scripts\medir-ram.ps1 -Etiqueta sesion
  Si PowerShell bloquea el script:  powershell -ExecutionPolicy Bypass -File .\scripts\medir-ram.ps1
  Solo lee contadores locales; no hace nada de red.
#>
param(
    [int]$Segundos = 15,
    [string]$Etiqueta = "medicion"
)

$p = Get-Process -Name FocusTimer -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Host "FocusTimer no está abierto." -ForegroundColor Yellow; exit 1 }

$priv = @(); $ws = @(); $commit = @()
for ($i = 0; $i -lt $Segundos; $i++) {
    $p.Refresh()
    $ws += $p.WorkingSet64
    $commit += $p.PrivateMemorySize64
    # El contador "Working Set - Private" es el valor del Administrador de tareas.
    $c = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess = $($p.Id)" -ErrorAction SilentlyContinue
    if ($c) { $priv += [int64]$c.WorkingSetPrivate }
    Start-Sleep -Seconds 1
}

function MB($v) { "{0,7:N1} MB" -f ($v / 1MB) }
function Fila($nombre, $valores) {
    if ($valores.Count -eq 0) { return "{0,-34} (no disponible)" -f $nombre }
    $m = ($valores | Measure-Object -Average -Maximum)
    "{0,-34} prom {1}   máx {2}" -f $nombre, (MB $m.Average), (MB $m.Maximum)
}

Write-Host ""
Write-Host "FocusTimer (PID $($p.Id)) – $Etiqueta – $Segundos s" -ForegroundColor Cyan
Write-Host (Fila "Conjunto de trabajo PRIVADO" $priv)
Write-Host (Fila "Conjunto de trabajo total" $ws)
Write-Host (Fila "Bytes privados (commit)" $commit)
Write-Host "Objetivo: conjunto de trabajo privado < 40 MB."
