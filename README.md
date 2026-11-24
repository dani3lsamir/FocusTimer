# FocusTimer

Temporizador **Pomodoro** personal para Windows: registra tu tiempo de enfoque y muestra si cumples tus metas.
Pequeño, **100 % sin conexión** y con muy poca memoria. Hecho en C# + .NET 10 + WinForms, sin paquetes NuGet en la app.

## Qué hace

- **Pomodoro**: 25 min de enfoque, 5 min de descanso corto y 15 min de descanso largo cada 4 pomodoros (todo configurable).
  Botones: *Iniciar*, *Pausar/Reanudar*, *Detener* y *Saltar descanso*.
- **Un clic para empezar**: *Iniciar* usa la última categoría y la última tarea. La tarea es opcional
  (lista con las 10 más recientes). Enter también inicia.
- **Registro**: cada sesión de enfoque se añade a `sessions.csv`. Una sesión detenida antes de tiempo se guarda con `completed=false`.
  Los descansos no se registran.
- **Avisos**: al terminar un enfoque o un descanso salen una notificación de Windows («Pomodoro terminado – toma 5 min»)
  y el botón de la barra de tareas **parpadea hasta que abras la ventana**.
- **Barra de tareas**: la barra de progreso del botón muestra el tiempo restante.
  Es **verde** en enfoque, **amarilla** en descanso y **roja** en pausa (Windows solo ofrece esos tres colores).
  El título de la ventana muestra los minutos restantes, así que los ves al pasar el ratón.
- **Timers** (pestaña aparte, para cocinar o cualquier cosa): varios a la vez (hasta 10), cada uno con su nombre.
  - Rápidos de **3, 5 y 10 min**.
  - **Timers guardados** con nombre, como «Huevo · 6:30»: un clic los inicia y clic derecho los edita o elimina.
  - La duración se escribe libremente: `10` (min), `6:30`, `6.5`, `1:15:00`, `90s` o `2h`.
  - Se pueden pausar y quitar. Al terminar avisan igual que el Pomodoro: notificación y parpadeo.
  - **No se registran** en `sessions.csv` ni cuentan para metas. Si cierras la app con timers corriendo, te lo pregunta antes.
- **Metas**: meta diaria de pomodoros (por defecto 8) y metas semanales opcionales en minutos por categoría.
- **Panel**: resumen de hoy (minutos, pomodoros frente a la meta, desglose por categoría), gráfico semanal apilado por categoría,
  metas semanales y **racha** de días cumpliendo la meta diaria.
- **Ajustes**: duraciones, categorías (añadir, renombrar y reordenar), metas, *Iniciar con Windows* y *Abrir carpeta de datos*.
- **Tema AMOLED**: fondo negro puro, barra de título oscura y un solo color de acento. La ventana recuerda su posición.

### Reglas de cálculo

- Una sesión cuenta para el **día en que empezó**: un pomodoro de 23:50 a 00:15 es del día anterior.
- La semana va de **lunes a domingo**.
- Los *minutos de enfoque* incluyen las sesiones detenidas. Los *pomodoros* solo cuentan las completas.
- **Racha**: días seguidos cumpliendo la meta diaria. Hoy solo suma cuando ya cumpliste la meta. Si aún no la cumples, la racha no se rompe.
- **Renombrar una categoría no cambia el CSV**: el nombre anterior se guarda como alias y las sesiones antiguas siguen contando para ella.
- El temporizador usa la **hora real del reloj** (no cuenta ticks). Si el PC se suspende o va lento, no se desfasa.
  Al volver, las fases que terminaron se cierran con su hora real de fin.

## Requisitos

- Windows 10 (20H1 o posterior) o Windows 11, x64.
- **.NET 10 Desktop Runtime (x64)**. Se instala una sola vez (~55 MB):
  ```powershell
  winget install Microsoft.DotNet.DesktopRuntime.10
  ```
  O descárgalo de la página oficial de .NET 10 de Microsoft: «.NET Desktop Runtime», x64.

### ¿Por qué *framework-dependent* y no *self-contained*?

|                        | Framework-dependent (elegido) | Self-contained              |
|------------------------|-------------------------------|-----------------------------|
| Tamaño del `.exe`      | **≈ 0,6 MB**                  | ≈ 150 MB (WinForms no se puede recortar) |
| Instalar algo          | .NET 10 Desktop Runtime, una vez | Nada                     |
| RAM                    | Igual (es el mismo runtime)   | Igual o un poco más         |
| Actualizaciones de seguridad del runtime | Por Windows Update / winget | Hay que recompilar |

La RAM la pone el runtime de .NET y WinForms, no la forma de distribuir el programa, así que la diferencia real es el tamaño en disco.

## Compilar

Necesitas el **SDK de .NET 10**: `winget install Microsoft.DotNet.SDK.10`.

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1   # desactiva la telemetría del SDK (no de la app: la app no tiene)
dotnet test
dotnet publish src/FocusTimer.App -c Release -r win-x64 -o publish
```

El resultado es un único archivo: `publish\FocusTimer.exe`.
También compila desde Linux o macOS (`EnableWindowsTargeting`), aunque allí solo corren los tests.

## Instalar en tu PC

1. Crea la carpeta `%LOCALAPPDATA%\Programs\FocusTimer\` y copia ahí `FocusTimer.exe`.
2. Acceso directo en el menú Inicio (PowerShell, sin admin):
   ```powershell
   $exe = "$env:LOCALAPPDATA\Programs\FocusTimer\FocusTimer.exe"
   $lnk = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\FocusTimer.lnk"
   $s = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
   $s.TargetPath = $exe; $s.WorkingDirectory = Split-Path $exe; $s.Save()
   ```
3. Para anclarla a la barra de tareas, ábrela, haz clic derecho en su botón y elige **Anclar a la barra de tareas**.
   Hazlo desde la ventana abierta y no desde el `.exe`, para que el botón anclado y la ventana sean el mismo.
4. Opcional: en *Ajustes* activa **Iniciar con Windows**.

Si ya está abierta y la vuelves a lanzar, se trae al frente la ventana existente. Nunca hay dos copias a la vez.

## Dónde se guardan los datos

Todo vive en **`%LOCALAPPDATA%\FocusTimer\`** (botón *Abrir carpeta de datos* en Ajustes). Nada se guarda en este repositorio.

| Archivo | Contenido |
|---|---|
| `sessions.csv` | Tus sesiones. **Es la exportación**: se abre en Excel o LibreOffice. |
| `settings.json` | Ajustes, categorías, metas, tareas recientes y timers guardados. |
| `current.json` | Pomodoro en curso (se guarda cada 30 s). Sirve para recuperarlo si la app o el PC se cierran de golpe. |
| `sessions.pending.csv` | Solo aparece si `sessions.csv` estaba bloqueado (p. ej. abierto en Excel). Se fusiona solo. |
| `icon.png`, `error.log` | Icono para las notificaciones y registro local de errores inesperados. |

Columnas de `sessions.csv` (UTF-8, separador coma, fechas `yyyy-MM-dd HH:mm:ss` en hora local):

```
id,start_local,end_local,planned_min,actual_min,category,task,completed
```

El archivo **solo crece**: cada sesión se añade al final y se vuelca a disco al instante. Nunca se reescribe.
Si un corte deja una línea a medias, esa fila se ignora y las demás siguen intactas.

## Privacidad

- **Sin conexión**: el código no contiene APIs de red (ni `HttpClient`, ni sockets, ni `System.Net`).
  No hay actualizaciones automáticas, telemetría, informes de errores, ni fuentes o imágenes descargadas.
  Un test (`NoNetworkTests`) falla si alguien añade código de red o un paquete NuGet a la app.
- **Ningún dato sale de tu PC.** Los errores inesperados solo se escriben en `error.log`, en tu carpeta de datos.
- Lo que la app escribe fuera de su carpeta de datos son dos claves del registro, solo de tu usuario (HKCU):
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\FocusTimer`: solo si activas *Iniciar con Windows*.
  - `HKCU\Software\Classes\AppUserModelId\FocusTimer.App`: nombre e icono para que Windows muestre las notificaciones.

### Bloquearla en el Firewall de Windows (certeza extra)

En PowerShell **como administrador**:

```powershell
$exe = "$env:LOCALAPPDATA\Programs\FocusTimer\FocusTimer.exe"
New-NetFirewallRule -DisplayName "FocusTimer - bloquear salida"  -Direction Outbound -Program $exe -Action Block
New-NetFirewallRule -DisplayName "FocusTimer - bloquear entrada" -Direction Inbound  -Program $exe -Action Block
```

Con interfaz gráfica: *Firewall de Windows Defender con seguridad avanzada* → *Reglas de salida* → *Nueva regla* →
*Programa* → elige `FocusTimer.exe` → *Bloquear la conexión*. Repite en *Reglas de entrada*.
Para quitarlas: `Remove-NetFirewallRule -DisplayName "FocusTimer - bloquear*"`.

## Medir la memoria

Con la app abierta:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\medir-ram.ps1 -Etiqueta reposo
```

Muestra el **conjunto de trabajo privado**, que es la columna «Memoria» del Administrador de tareas. Objetivo: menos de 40 MB.
Ajustes que ayudan a mantenerla baja: GC de estación sin hilo concurrente, globalización invariable, ReadyToRun,
cero dependencias y el temporizador de la UI a 2 Hz solo mientras corre (cada 30 s en reposo).

## Desinstalar

1. En *Ajustes*, desactiva *Iniciar con Windows*.
2. Borra `%LOCALAPPDATA%\Programs\FocusTimer\` y el acceso directo del menú Inicio.
3. Borra `%LOCALAPPDATA%\FocusTimer\` (tus datos: guarda antes `sessions.csv` si lo quieres).
4. Opcional: `Remove-Item "HKCU:\Software\Classes\AppUserModelId\FocusTimer.App"`.

## Estructura

```
src/FocusTimer.Core   Lógica sin UI (net10.0): temporizador, CSV, ajustes, metas, racha. Corre en cualquier SO.
src/FocusTimer.App    WinForms (net10.0-windows): ventana, gráfico GDI+, interop con Windows (Native/).
tests/FocusTimer.Tests  xUnit: máquina de estados, CSV, metas/racha, medianoche, recuperación y "sin red".
scripts/medir-ram.ps1   Medición de memoria.
```

El único paquete NuGet del repositorio es `xunit.v3`, y solo en el proyecto de tests: nunca va dentro del `.exe`.
Los tests usan Microsoft.Testing.Platform (configurado en `global.json`).

## Ideas futuras (no incluidas en v1)

Timers que sobrevivan a cerrar la app, atajos de teclado globales, icono en la bandeja, detección de inactividad, editar sesiones a mano, notas por sesión,
relojes mundiales (Bolivia/China), informes de largo plazo y una interfaz para Linux sobre `FocusTimer.Core`.

## Licencia

[MIT](LICENSE)
