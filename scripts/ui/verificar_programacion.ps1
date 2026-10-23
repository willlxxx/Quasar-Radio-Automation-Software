param(
  [string]$Theme = 'Studio Dark Neon',
  [string]$Tag = 'ui4c',
  [string]$Sandbox = (Join-Path $env:TEMP 'qsr_prog_test'),
  [switch]$Keep
)

# UI-4 paso C: verificacion EN VIVO de la rejilla horaria de Programacion.
#
# Lo que se comprueba, en el orden en que lo vive un operador:
#   G0/G1  la ventana abre sin clave, son 168 celdas (7 dias x 24 horas) y la geometria
#          cuadricula de verdad (24 filas por 7 columnas, todas con rect util)
#   G2     vista dia (24 celdas, 1 columna) y vista semana (168, 7 columnas) sobre la misma rejilla
#   G5/G6/G7  una celda programada se CUMPLE a su minuto con el modulo abierto, una sola vez,
#          y una celda pausada no suena (el reloj vive en la consola, no en la ventana)
#   G3/G4  editar y APLICAR, dejar la celda libre con QUITAR, y el marcado (pausada)
#   G8     RELLENAR HASTA (de 06:00 a 12:00 de un plumazo)
#   G9     A TODA LA SEMANA, y el guardia de la referencia vaciabot (un cart sin atajo no se escribe)
#   G10    COPIAR / PEGAR
#   G13a   una tanda se programa en la celda desde su propio boton
#   G11    GUARDAR pide identificacion (nivel administrador) y schedule.json queda en el sandbox
#   G12    reinicio en el MISMO sandbox: la rejilla vuelve del disco
#   G13b   cerrar Programacion no cierra la emisora
#
# AISLAMIENTO: el exe arranca con -WorkingDirectory $Sandbox (la libreria que se escanea y el
# nowplaying.txt son rutas relativas al directorio de trabajo) y QUASAR_RADIO_DATA_DIR apunta a
# $Sandbox\data, que es donde UI-4 paso C escribe schedule.json junto a users.json. Sin eso esta
# prueba programaria la rejilla real del fundador.
#
# Los archivos de ensayo llevan prefijo QSRPROG. Jamas HRS ni MIN: con esos nombres se pisaron
# 31 grabaciones reales de horarias/ (docs/UI_BASELINE.md).
#
# El master de salida se baja a 0 antes de que nada suene al aire: la pauta se comprueba con el
# aire realmente empezado, pero en la cabina del fundador no tiene que oirse nada.

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if (-not ('QsrProg' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class ProgWin { public IntPtr Hwnd; public string Title; public bool Visible; public string Rect; public int L; public int T; public int W; public int H; }
public static class QsrProg {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  public static void Click(int x, int y) {
    SetCursorPos(x, y);
    mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
    mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
  }
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
  public static void Raise(IntPtr h) {
    ShowWindow(h, 5); BringWindowToTop(h);
    keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
    SetForegroundWindow(h);
  }
  public static void Close(IntPtr h) { SendMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); } // WM_CLOSE
  static int _target; static List<ProgWin> _found;
  public static List<ProgWin> Top(int pid) {
    _target = pid; _found = new List<ProgWin>();
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == _target) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        var wi = new ProgWin();
        wi.Hwnd = h; wi.Title = sb.ToString(); wi.Visible = IsWindowVisible(h);
        wi.L = r.Left; wi.T = r.Top; wi.W = r.Right - r.Left; wi.H = r.Bottom - r.Top;
        wi.Rect = r.Left + "," + r.Top + " " + wi.W + "x" + wi.H;
        _found.Add(wi);
      }
      return true;
    }, IntPtr.Zero);
    return _found;
  }
}
'@
}

$AE    = [System.Windows.Automation.AutomationElement]
$CT    = [System.Windows.Automation.ControlType]
$Scope = [System.Windows.Automation.TreeScope]

function CTCond($ct) { New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ct) }
function ByName($root, $pattern, $ct) {
  $hits = @()
  foreach ($e in $root.FindAll($Scope::Descendants, (CTCond $ct))) {
    if ($e.Current.Name -match $pattern) { $hits += $e }
  }
  return $hits
}
function One($root, $pattern, $ct) {
  $h = ByName $root $pattern $ct
  if ($h.Count -eq 0) { return $null }
  return $h[0]
}
# Los textos de estado y cabecera van por AutomationId (x:Name en WPF), nunca por Nombre: en un
# TextBlock el Nombre ES el valor, y buscarlo por una etiqueta fija devolveria la etiqueta.
function ById($root, $id) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
  return $root.FindFirst($Scope::Descendants, $c)
}
function Get-Estado($win) {
  $e = ById $win 'ScheduleStatusText'
  if ($null -eq $e) { $e = ById $win 'StatusText' }
  if ($null -eq $e) { return '(sin texto de estado)' }
  return $e.Current.Name
}
function Invoke-El($e, $que) {
  if ($null -eq $e) { L "  INVOCAR $que : NO ENCONTRADO"; return $false }
  try {
    $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
  } catch {
    # El mensaje solo no dice nada: esta prueba petó una vez con «error » y punto (corrida 5).
    # La excepcion de UIA trae ademas el HRESULT y el tipo, y es eso lo que distingue un peer
    # viejo (ElementNotAvailable) de un boton apagado (InvokeNotSupported / no habilitado).
    $ex = $_.Exception
    $tipo = $ex.GetType().FullName
    $hr = ''
    if ($ex -is [System.Windows.Automation.AutomationException]) {
      $hr = ' hresult=0x' + ('{0:X8}' -f ([int]$ex.HResult))
    }
    L ("  INVOCAR $que : error [" + $tipo + $hr + "] " + $ex.Message)
    return $false
  }
}
# Invocar un boton de WPF despues de un cambio de estado: el peer del anterior queda viejo (H26),
# asi que se vuelve a resolver la ventana, se espera a que el boton este habilitado y se reintenta.
function Invoke-Vivo($hwnd, $id, $pat, $ct, $que, [int]$intentos = 4) {
  # El boton se busca por AutomationId cuando lo tiene, y si no por su Nombre de UIA. La espera de
  # habilitado se hace SOBRE EL ELEMENTO YA ENCONTRADO (leer IsEnabled es barato) y lo que se
  # invoca es una resolucion nueva: un peer viejo no falla con ruido, falla en silencio o escribe
  # en la celda equivocada (H26, y la corrida 4 con «domingo 17» en lugar de la hora en curso).
  # Volver a buscar en cada vuelta de espera costaria un FindAll de 168 botones por vuelta.
  for ($i = 1; $i -le $intentos; $i++) {
    $e = $null
    for ($v = 0; $v -lt 5 -and $null -eq $e; $v++) {
      $win = Win-El $hwnd
      $e = if ($id) { ById $win $id } else { One $win $pat $ct }
      if ($null -eq $e) { Start-Sleep -Milliseconds 400 }
    }
    if ($null -eq $e) {
      L ("  INVOCAR-VIVO $que : intento $i no vio el boton en 2 s")
      continue
    }
    $espera = 0
    while (-not $e.Current.IsEnabled -and $espera -lt 4000) {
      Start-Sleep -Milliseconds 300
      $espera += 300
    }
    if (-not $e.Current.IsEnabled) {
      L ("  INVOCAR-VIVO $que : intento $i sigue apagado tras $espera ms")
      continue
    }
    $win = Win-El $hwnd
    $vivo = if ($id) { ById $win $id } else { One $win $pat $ct }
    if ($null -eq $vivo) { $vivo = $e }
    if (Invoke-El $vivo $que) {
      if ($espera -gt 0) { L ("   $que : habilitado a los $espera ms, invocado al intento $i") }
      return $i
    }
    Start-Sleep -Milliseconds 400
  }
  return 0
}
# Atajo para todo lo que sea un boton de la ventana de Programacion buscado por su Nombre.
function Inv($que, $pat, [int]$intentos = 4) {
  return (Invoke-Vivo $script:proHwnd $null $pat $CT::Button $que $intentos)
}
function Set-Text($e, $valor) {
  # Un null aqui petaba la corrida entera con «You cannot call a method on a null-valued
  # expression» (ErrorActionPreference=Stop) y no dejaba ni la linea de evidencia. Y no devuelve
  # nada: los sitios que lo llaman sin capturar («Set-Text $minBox 47») imprimirian un «True»
  # suelto en el archivo de evidencia (corrida 6, entre G5_activa y G5_antes_de_aplicar).
  if ($null -eq $e) { L "  ESCRIBIR '$valor' : el campo no esta en el arbol"; return }
  try { [void]$e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($valor) }
  catch { L ("  ESCRIBIR '$valor' : " + $_.Exception.GetType().Name + ' ' + $_.Exception.Message) }
}
function Valor($e) {
  if ($null -eq $e) { return '(ausente)' }
  try { return $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
  catch { return '(sin ValuePattern)' }
}
function Txt($e) { if ($null -eq $e) { return '(ausente)' } return $e.Current.Name }
function Get-Rows($g) {
  if ($null -eq $g) { return @() }
  $r = @($g.FindAll($Scope::Children, (CTCond $CT::DataItem)))
  if ($r.Count -eq 0) { $r = @($g.FindAll($Scope::Descendants, (CTCond $CT::DataItem))) }
  return $r
}
function RectOf($e) {
  $r = $e.Current.BoundingRectangle
  return [pscustomobject]@{ X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
}
# CheckBox: se pone al estado que haga falta, no se alterna a ciegas (una prueba que depende de
# como estaba el casillero antes de empezar no es una prueba).
function Set-Check($e, [bool]$on) {
  if ($null -eq $e) { L '  CASILLERO : no esta en el arbol'; return '(ausente)' }
  $t = $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
  $want = if ($on) { [System.Windows.Automation.ToggleState]::On } else { [System.Windows.Automation.ToggleState]::Off }
  if ($t.Current.ToggleState -ne $want) { [void]$t.Toggle() }
  return $t.Current.ToggleState.ToString()
}
# El casillero se resuelve con reintentos y la ventana fresh en cada vuelta. En la corrida 8 (la
# de las 18:18) G7 leyo el «Pauta activa» dos veces seguidas: la primera lo encontro y la segunda
# «No se puede llamar a un metodo en una expresion con valor NULL» y se llevo la corrida entera
# por delante, sin decir nada del estado real. Es H16/H26 sobre un CheckBox: la rejilla se refresca
# con el reloj cada segundo y el peer de un control puede sustituirse entre dos lecturas.
function Casillero($hwnd, $pat, [int]$intentos = 4) {
  for ($i = 1; $i -le $intentos; $i++) {
    $e = One (Win-El $hwnd) $pat $CT::CheckBox
    if ($null -ne $e) { return $e }
    Start-Sleep -Milliseconds 350
  }
  # Y cuando no esta, se vuelca lo que SI hay de ese tipo, para no volver a ciegas (H16).
  $cb = @(ByName (Win-El $hwnd) '.' $CT::CheckBox)
  L ('  CASILLERO ' + $pat + ' : no aparecio en ' + $intentos + ' intentos | checkBoxes=' + $cb.Count +
     ' [' + (($cb | ForEach-Object { $_.Current.Name } | Select-Object -First 5) -join ' | ') + ']')
  return $null
}
function Estado-Casillero($hwnd, $pat) {
  $e = Casillero $hwnd $pat
  if ($null -eq $e) { return '(sin casillero)' }
  try { return $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString() }
  catch { return '(sin TogglePattern)' }
}
function Poner-Casillero($hwnd, $pat, [bool]$on) {
  $e = Casillero $hwnd $pat
  if ($null -eq $e) { L ('  CASILLERO ' + $pat + ' : no se pudo poner a ' + $on); return '(sin casillero)' }
  return (Set-Check $e $on)
}
# Una hora que no se va a cumplir mientras dura la corrida: descarta la hora en curso y la
# siguiente (G5/G7 disparan sobre esas) y la banda 06-13 (la del rango de G8). Se puso aqui porque
# la usan G2c y G9, y en PowerShell una «function» no existe hasta que el script pasa por ella.
function Hora-Libre([int[]]$yaElegidas) {
  $ahora = [int](Get-Date).Hour
  foreach ($c in @(20, 3, 10, 17, 0, 7, 14, 21, 4, 11, 18, 1, 8, 15, 22, 5, 12, 19, 2, 9, 16, 23, 6, 13)) {
    if ($c -eq $ahora -or $c -eq (($ahora + 1) % 24)) { continue }
    if ($c -ge 6 -and $c -le 13) { continue }
    if ($yaElegidas -contains $c) { continue }
    return $c
  }
  return -1
}
function Wait-Until([scriptblock]$Test, [int]$Ms) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $Ms) {
    try { if (& $Test) { return [int]$sw.ElapsedMilliseconds } } catch { }
    Start-Sleep -Milliseconds 250
  }
  return -1
}
function Win-El($hwnd) { return $AE::FromHandle([IntPtr]$hwnd) }
function Raise-Front($hwnd) {
  for ($i = 1; $i -le 3; $i++) {
    [void][QsrProg]::Raise([IntPtr]$hwnd)
    Start-Sleep -Milliseconds 300
    if ([QsrProg]::GetForegroundWindow() -eq [IntPtr]$hwnd) { return $true }
  }
  return $false
}
# CLIC DE RATON para lo que pueda abrir el dialogo de identificacion: InvokePattern sobre un boton
# cuyo manejador abre un modal (ShowDialog) no devuelve hasta que el modal se cierra.
function Click-El($e, $que, $win) {
  if ($null -eq $e) { L "  CLIC $que : NO ENCONTRADO"; return $false }
  if (-not (Raise-Front $script:frontHwnd)) { L "  CLIC $que : abortado, la ventana no esta al frente"; return $false }
  $r = RectOf $e
  if ($r.W -lt 4 -or $r.H -lt 4) { L ("  CLIC $que : rect invalida " + $r.X + ',' + $r.Y + ' ' + $r.W + 'x' + $r.H); return $false }
  $cx = $r.X + [int]($r.W / 2)
  $cy = $r.Y + [int]($r.H / 2)
  if ($null -ne $win) {
    if ($cx -lt $win.L -or $cx -gt ($win.L + $win.W) -or $cy -lt $win.T -or $cy -gt ($win.T + $win.H)) {
      L ("  CLIC $que : fuera de la ventana (" + $cx + ',' + $cy + ' vs ' + $win.Rect + ')')
      return $false
    }
  }
  [QsrProg]::Click($cx, $cy)
  return $true
}
function Get-Login($pid_) {
  return [QsrProg]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like 'Quasar Radio | *' } | Select-Object -First 1
}
function Wait-Login($pid_, [int]$segundos = 12) {
  $fin = (Get-Date).AddSeconds($segundos)
  while ((Get-Date) -lt $fin) {
    $m = Get-Login $pid_
    if ($null -ne $m) { Start-Sleep -Milliseconds 600; return $m }
    Start-Sleep -Milliseconds 300
  }
  return $null
}
# Un PasswordBox de WPF no expone su valor por UIA (a proposito). Foco con clic y teclado.
function Set-Password($root, $automationId, $valor) {
  $pb = ById $root $automationId
  if ($null -eq $pb) { return $false }
  if (-not (Raise-Front $script:frontHwnd)) { return $false }
  Start-Sleep -Milliseconds 200
  $r = RectOf $pb
  [QsrProg]::Click(($r.X + [int]($r.W / 2)), ($r.Y + [int]($r.H / 2)))
  Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait($valor)
  Start-Sleep -Milliseconds 200
  return $true
}
function Maximize($hwnd) {
  try {
    $el = $AE::FromHandle([IntPtr]$hwnd)
    $p = $el.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $p.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
    Start-Sleep -Milliseconds 700
    return $true
  } catch { L ('  MAXIMIZAR : ' + $_.Exception.Message); return $false }
}
function Short-Hash($path) {
  if (-not (Test-Path $path)) { return 'no-hay' }
  return (Get-FileHash $path -Algorithm MD5).Hash.Substring(0, 8)
}
# Salir de la banda 54-59 del reloj. Las pruebas de disparo (G5, G7) apuntan al minuto siguiente
# del reloj real y esperan hasta 130 s: si el minuto en curso ya es 54 o mas, el objetivo cae
# justo cuando cambia la hora y la prueba se queda esperando un aviso que ya no le toca a esa
# celda. Esperar «un minuto» no vale: desde :54 se pasa a :55 y sigue en la banda. Se espera de
# diez en diez hasta el minuto 0-53, avisando cada seis vueltas para no llenar la evidencia.
function Esperar-MinutoTranquilo($que, [int]$capSegundos = 420) {
  $t0 = Get-Date
  $vueltas = 0
  while ((Get-Date).Minute -ge 54) {
    if (((Get-Date) - $t0).TotalSeconds -gt $capSegundos) {
      L ('   ' + $que + '_sigue_apurado tras ' + $capSegundos + ' s (minuto ' + (Get-Date).Minute + ')')
      return $false
    }
    $vueltas++
    if ($vueltas % 6 -eq 1) {
      L ('   ' + $que + '_minuto_apurado (' + (Get-Date).Minute + '), saliendo de la banda 54-59')
    }
    Start-Sleep -Seconds 10
  }
  $s = [int]((Get-Date) - $t0).TotalSeconds
  if ($s -ge 10) { L ('   ' + $que + '_fuera_de_la_banda en ' + $s + ' s (minuto ' + (Get-Date).Minute + ')') }
  return $true
}

$exe    = 'C:\Users\CY-KILL\Desktop\Programación y Datanálisis\quasar_radio\bin\Debug\net10.0-windows\quasar_radio.exe'
$binDir = Split-Path $exe
$logdir = Join-Path $binDir 'logs'
$out    = Join-Path $env:TEMP 'qsr_ui'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$dataDir   = Join-Path $Sandbox 'data'
$scheduleJ = Join-Path $dataDir 'schedule.json'
$backupJ   = Join-Path $dataDir 'schedule_backup.json'
$usersJ    = Join-Path $dataDir 'users.json'
$binMeta   = Join-Path $binDir 'track_metadata.json'

$lines = New-Object Collections.ArrayList
# Con hora en cada linea: esta prueba espera minutos reales de reloj (G5, G6, G7) y sin marca de
# tiempo no se puede saber donde se fue el tiempo de una corrida.
function L($m) {
  $t = '[' + (Get-Date -Format 'HH:mm:ss') + '] ' + $m
  $lines.Add($t) | Out-Null; Write-Host $t
}

function CapWindow($win, $t) {
  if ($null -eq $win) { L "CAP $t : sin ventana"; return }
  $w = [int]$win.W; $h = [int]$win.H
  if ($w -lt 50 -or $h -lt 50) { L "CAP $t : rect inválida"; return }
  $bmp = New-Object Drawing.Bitmap($w, $h)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrProg]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 350
  $hdc = $g.GetHdc()
  $ok = [QsrProg]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $path = Join-Path $out "$t.png"
  $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  L ("CAP $t : PrintWindow=$ok ${w}x${h} md5=" + (Get-FileHash $path -Algorithm MD5).Hash)
}

# ---- WAV de ensayo: PCM 16 bits mono
function New-QsrWav([string]$Path, [double]$Seconds, [int]$Hz) {
  $rate = 44100; $n = [int]($rate * $Seconds); $data = $n * 2
  $ms = New-Object IO.MemoryStream
  $bw = New-Object IO.BinaryWriter($ms)
  $bw.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int](36 + $data))
  $bw.Write([Text.Encoding]::ASCII.GetBytes('WAVE'))
  $bw.Write([Text.Encoding]::ASCII.GetBytes('fmt ')); $bw.Write([int]16)
  $bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([int32]$rate)
  $bw.Write([int32]($rate * 2)); $bw.Write([int16]2); $bw.Write([int16]16)
  $bw.Write([Text.Encoding]::ASCII.GetBytes('data')); $bw.Write([int32]$data)
  for ($i = 0; $i -lt $n; $i++) {
    $s = [int]([Math]::Sin(2 * [Math]::PI * $Hz * ($i / $rate)) * 9000)
    $bw.Write([int16]$s)
  }
  $bw.Flush(); [IO.File]::WriteAllBytes($Path, $ms.ToArray())
  $bw.Close(); $ms.Close()
}

function New-Sandbox {
  if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'musica') | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'jingles') | Out-Null
  New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
  # Tres pistas cortas: la pauta de «lista automatica» tiene que encontrar con que llenar la cola.
  for ($i = 1; $i -le 3; $i++) {
    New-QsrWav (Join-Path $Sandbox ('musica\QSRPROG Artista ' + $i + ' - Pista ' + $i + '.wav')) 2.0 (220 + $i * 60)
  }
  New-QsrWav (Join-Path $Sandbox 'jingles\QSRPROG Sintonia - Corto.wav') 1.0 660
  L ('FIXTURES: 3 wav QSRPROG* en musica/, 1 jingle y datos aislados en ' + $dataDir)
}

function Find-Window($pid_, $pattern) {
  return ([QsrProg]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like $pattern } | Select-Object -First 1)
}

# Arranca el programa con la ventana de Programacion ya abierta.
function Start-QsrProg([string]$modo) {
  $p = Start-Process -FilePath $exe -WorkingDirectory $Sandbox -ArgumentList '--ui-check', 'Schedule' -PassThru
  Start-Sleep -Seconds 12
  $pro = Find-Window $p.Id 'Programaci?n - Quasar Radio*'
  $con = Find-Window $p.Id 'Quasar Radio Pro*'
  if ($null -eq $pro) { throw "sin ventana de Programacion en $modo" }
  if ($null -eq $con) { throw "sin consola en $modo" }
  $proEl = $AE::FromHandle([IntPtr]$pro.Hwnd)
  $conEl = $AE::FromHandle([IntPtr]$con.Hwnd)
  # Centinela: el texto de estado es lo ultimo que se publica. Los FindAll sobre un arbol que
  # todavia se pinta devuelven solo la parte de arriba (H11/H20).
  $ms = Wait-Until { $null -ne (ById $proEl 'ScheduleStatusText') } 60000
  if ($ms -lt 0) { throw "Programacion sin centinela en $modo" }
  $ms2 = Wait-Until { $null -ne (ById $conEl 'ConsoleStatusText') } 60000
  L ("ARRANQUE[$modo]: programacion lista en ${ms} ms, consola en ${ms2} ms")
  return [pscustomobject]@{ Proc = $p; Pro = $pro; Con = $con; ProEl = $proEl; ConEl = $conEl }
}

# Las celdas son botones cuyo Nombre de UIA se describen a si mismas: «celda martes 06 · hora
# hablada (pausada)». Se buscan por ahi y SIEMPRE se vuelve a resolver tras cada edicion, porque
# el peer de UIA de un boton de WPF se sustituye cuando cambia su contenido (H26).
#
# Desde el arreglo de H27 la rejilla son DOS ItemsControls que se alternan por Visibility
# (ScheduleWeekGrid de 7 columnas, ScheduleDayGrid de 1). Todo lo que se cuenta o se pulsa va
# DENTRO de la que esta a la vista, que es la que apunta $script:RejillaId: una busqueda por toda
# la ventana puede devolver la celda gemela de la rejilla oculta, y el Invoke a un boton
# colapsado no selecciona nada — la prueba se quedaria esperando un disparo que nunca se programo.
$script:RejillaId = 'ScheduleWeekGrid'
function Get-Celdas($win) {
  return @($win.FindAll($Scope::Descendants, (CTCond $CT::Button)) |
           Where-Object { $_.Current.Name -match '^celda ' })
}
function Get-Rejilla($win) {
  $g = ById $win $script:RejillaId
  if ($null -ne $g) { return $g }
  return $win
}
function Get-Celdas-En($raiz) {
  if ($null -eq $raiz) { return @() }
  return @($raiz.FindAll($Scope::Descendants, (CTCond $CT::Button)) |
           Where-Object { $_.Current.Name -match '^celda ' })
}
# Cuantas celdas publica UNA rejilla concreta, se vea o no: es la medida de si la rejilla
# oculta esta en el arbol de UIA, que es lo que decide si contar por ventana es seguro.
function Celdas-De($hwnd, $id) {
  $g = ById (Win-El $hwnd) $id
  if ($null -eq $g) { return -1 }
  return @(Get-Celdas-En $g).Count
}
function Wait-Celdas($hwnd, [int]$esperado) {
  $mejor = @()
  for ($i = 0; $i -lt 25; $i++) {
    $c = @(Get-Celdas-En (Get-Rejilla (Win-El $hwnd)))
    if ($c.Count -gt $mejor.Count) { $mejor = $c }
    if ($c.Count -ge $esperado) {
      L ('   Wait-Celdas=' + $c.Count + '/' + $esperado + ' en el intento ' + ($i + 1) +
         " (raiz refrescada, rejilla $script:RejillaId)")
      return ,$c
    }
    Start-Sleep -Milliseconds 400
  }
  L ('   Wait-Celdas=' + $mejor.Count + '/' + $esperado + " tras 10 s ($script:RejillaId), refrescando la raiz en cada vuelta")
  return ,$mejor
}
function Horas-De($celdas) {
  return @($celdas | ForEach-Object { if ($_.Current.Name -match '^celda \S+ (\d\d)') { $Matches[1] } } | Sort-Object -Unique)
}
# El reparto de lo que UIA efectivamente publico, por dia y por hora, con sus conteos. Sirve para
# saber WHERE cae el hueco si faltan celdas: si a un dia le faltan las tres ultimas horas es un
# tramo del final de la coleccion; si a todas las horas les falta lo mismo es otra cosa.
function Reparto-De($celdas) {
  $porDia = @{}; $porHora = @{}
  foreach ($c in $celdas) {
    if ($c.Current.Name -match '^celda (\S+) (\d\d)') {
      # El dia se guarda sin tildes: «miercoles» y «sabado» tienen que caer en la misma clave
      # del universo de abajo, y escribir acentos en una clave de hashtable es una fuente de
      # falsos huecos.
      $d = ($Matches[1] -replace '[^A-Za-z]', '')
      $h = $Matches[2]
      if (-not $porDia.ContainsKey($d)) { $porDia[$d] = 0 }; $porDia[$d]++
      if (-not $porHora.ContainsKey($h)) { $porHora[$h] = 0 }; $porHora[$h]++
    }
  }
  return [pscustomobject]@{ Dia = $porDia; Hora = $porHora }
}
# Clave que NO llego a publicarse, o que se publico a medias. Recibe el universo completo:
# sin el, un hueco del tamano entero no aparece porque la clave simplemente no existe en el mapa.
function Huecos-De($mapa, $universo, [int]$esperado) {
  $r = @()
  foreach ($k in $universo) {
    $n = 0
    if ($mapa.ContainsKey($k)) { $n = [int]$mapa[$k] }
    if ($n -lt $esperado) { $r += ($k + '=' + $n) }
  }
  return $r
}
$HorasUniv = @(0..23 | ForEach-Object { '{0:00}' -f $_ })
# El universo de dias va en la forma que deja la normalizacion de arriba: la 'e' de «miércoles»
# ES la letra acentuada, asi que al quitarle los acentos queda «mircoles» y «sbado», no
# «miercoles» / «sabado». Con la forma acentuada mal escrita, G2 daba «miercoles=0 sabado=0» como
# si faltaran los dos dias enteros: era la prueba mintiendo (corrida del 2026-10-23 06:55).
$DiasUniv = @('lunes', 'martes', 'mircoles', 'jueves', 'viernes', 'sbado', 'domingo')
function Find-Celda($win, $pat) { return (One (Get-Rejilla $win) ('^celda ' + $pat) $CT::Button) }
# Inventario sin filtro de ControlType: cuantos elementos hay y cuantos se describen como celda.
# Si tras un cambio de vista «celdas_en_el_arbol» sigue siendo el numero completo pero Get-Celdas
# da menos, el problema es del filtro (los botones no publican ControlType.Button); si el arbol
# tambien esta corto, WPF no llego a publicar esos contenedores.
function Arbol-De($hwnd) {
  return @((Win-El $hwnd).FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
}
function Resumen-Del-Arbol($arbol) {
  $celdas = @($arbol | Where-Object { $_.Current.Name -match '^celda ' })
  $bots = @($arbol | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' })
  # Los elementos que se describen como celda SIN ser botones: en la corrida 7 el inventario de
  # toda la ventana daba 170 mientras la rejilla publicada daba 168, y hay que saber QUE son los
  # dos de mas (tooltip, contenedor, texto del editor) en vez de suponerlo.
  $noBotones = @($celdas | Where-Object { $_.Current.ControlType.ProgrammaticName -ne 'ControlType.Button' })
  $r = 'elementos=' + $arbol.Count + ' botones=' + $bots.Count + ' celdas_sin_filtro=' + $celdas.Count
  if ($noBotones.Count -gt 0) {
    $r += ' celdas_no_boton=' + (($noBotones | Select-Object -First 4 | ForEach-Object {
      $_.Current.ControlType.ProgrammaticName.replace('ControlType.','') + ':' + $_.Current.Name +
      ':' + $_.Current.AutomationId }) -join ' / ')
  }
  return $r
}
function Nombres-Repetidos($arbol) {
  $nombres = @{}
  foreach ($e in $arbol) {
    $n = $e.Current.Name
    if ([string]::IsNullOrWhiteSpace($n)) { continue }
    if (-not $nombres.ContainsKey($n)) { $nombres[$n] = 0 }
    $nombres[$n]++
  }
  $rep = @($nombres.Keys | Where-Object { $nombres[$_] -gt 1 -and $nombres[$_] -lt 20 } |
           ForEach-Object { '[' + $_ + '] x' + $nombres[$_] })
  if ($rep.Count -eq 0) { return '0 | (ningun nombre duplicado)' }
  return $rep.Count.ToString() + ' | ' + (($rep | Select-Object -First 12) -join ' ; ')
}
function Nombre-Celda($win, $pat) {
  $c = Find-Celda $win $pat
  if ($null -eq $c) { return '(no existe la celda)' }
  return $c.Current.Name
}

# Dias como patron de regex, sin acentos: ByName usa -match y en la rejilla «miércoles» y
# «sábado» llevan tilde. El punto cubre la vocal acentuada sin escribirla.
$DiaRe = @('domingo', 'lunes', 'martes', 'mi.rcoles', 'jueves', 'viernes', 's.bado')

$pre = @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)
if ($pre.Count -gt 0) { throw "Ya hay $($pre.Count) instancia(s) abiertas; abortando para no tocar la emision." }

$binMetaAntes = Short-Hash $binMeta
L ('AUDIT_ANTES: bin/track_metadata.json md5=' + $binMetaAntes + ' (no se toca: el trabajo va en el sandbox)')

$layout = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
if (Test-Path $layout) { Remove-Item $layout -Force }
New-Sandbox
if (-not (Test-Path (Join-Path $env:APPDATA 'QuasarRadio'))) { New-Item -ItemType Directory -Force -Path (Join-Path $env:APPDATA 'QuasarRadio') | Out-Null }
Set-Content -Path (Join-Path $env:APPDATA 'QuasarRadio\appearance.json') -Value ('{"theme":"' + $Theme + '"}') -Encoding UTF8
$env:QUASAR_RADIO_DATA_DIR = $dataDir

$script:proc = $null
try {
  # ==================================================================== SESION 1
  $s1 = Start-QsrProg 'sesion 1'
  $script:proc = $s1.Proc
  $win = $s1.ProEl
  $con = $s1.ConEl
  $script:frontHwnd = $s1.Pro.Hwnd
  $script:proHwnd   = $s1.Pro.Hwnd
  $script:conHwnd = $s1.Con.Hwnd

  # ---- G0: la ventana existe, el contenido XAML se instancio y el estado arranca vacio
  L ('G0_programacion=' + $s1.Pro.Rect + ' | consola=' + $s1.Con.Rect)
  L ('G0_estado=' + (Get-Estado $win))
  L ('G0_cabecera=' + (Txt (ById $win 'ProgrammedText')) + ' | ' + (Txt (ById $win 'NextDueText')))
  L ('G0_vacio_sin_json=' + ((Get-Estado $win) -match 'sin schedule.json') +
     ' | sin schedule.json todavia=' + (-not (Test-Path $scheduleJ)))
  L ('G0_tandas_vivas=' + (@(ByName $win 'Lanzar esta tanda ahora' $CT::Button).Count))

  # ---- G1: 168 celdas y la geometria de rejilla de verdad
  L ('G1_maximizada=' + (Maximize $s1.Pro.Hwnd))
  # La recta de la ventana se vuelve a leer: Click-El comprueba que el boton caiga dentro de lo
  # que se ve, y despues de maximizar la recta guardada al arrancar ya no es la ventana.
  $s1.Pro = Find-Window $script:proc.Id 'Programaci?n - Quasar Radio*'
  $script:frontHwnd = $s1.Pro.Hwnd
  $win = Win-El $s1.Pro.Hwnd
  $celdas = Wait-Celdas $s1.Pro.Hwnd 168
  L ('G1_celdas=' + $celdas.Count + ' (esperadas 168: 7 dias x 24 horas)')
  $repartoG1 = Reparto-De $celdas
  L ('G1_huecos_por_hora=' + ((Huecos-De $repartoG1.Hora $HorasUniv 7) -join ' ') +
     ' | por_dia=' + ((Huecos-De $repartoG1.Dia $DiasUniv 24) -join ' '))
  $filas = @($celdas | ForEach-Object { [int]([Math]::Round((RectOf $_).Y / 5)) } | Sort-Object -Unique)
  $cols  = @($celdas | ForEach-Object { [int]([Math]::Round((RectOf $_).X / 5)) } | Sort-Object -Unique)
  $visibles = @($celdas | Where-Object { $r = RectOf $_; ($r.W -ge 8 -and $r.H -ge 8) }).Count
  L ('G1_filas=' + $filas.Count + ' columnas=' + $cols.Count + ' celdas_con_rect_util=' + $visibles)
  L ('G1_geometria=' + (($filas.Count -eq 24) -and ($cols.Count -eq 7) -and ($visibles -eq 168)))
  $etiquetas = @(ByName $win '^\d\d:00$' $CT::Text)
  L ('G1_etiquetas_de_hora=' + $etiquetas.Count + ' (esperadas 24)')
  $dias = @(ByName $win '^[LMXJVS D]$' $CT::Text)
  L ('G1_cabeceras_de_dia=' + $dias.Count + ' | ' + (($dias | ForEach-Object { $_.Current.Name }) -join ' '))
  $todas = @($celdas | ForEach-Object { $_.Current.Name })
  $libres = @($todas | Where-Object { $_ -match 'libre$' }).Count
  L ('G1_celdas_libres=' + $libres + ' (rejilla nueva: todas libres)')

  # Inventario COMPLETO del arbol, con todos los controles y sin filtro de tipo. Sirve para
  # dos cosas que con FindAll por ControlType no se ven:
  #   1) si las celdas que faltan despues de un cambio de vista NO estan en el arbol (problema
  #      de publicacion de WPF) o estan con otro Nombre (problema de la prueba);
  #   2) si algun elemento del editor esta duplicado, porque dos «Aplicar» en el arbol significan
  #      que «One» puede estar pulsando el que no toca.
  $arbol = @(Arbol-De $s1.Pro.Hwnd)
  L ('G1_arbol=' + (Resumen-Del-Arbol $arbol))
  $nombres = Nombres-Repetidos $arbol
  L ('G1_nombres_repetidos=' + $nombres)

  # ---- G2: vista dia <-> vista semana sobre la misma rejilla
  # Cada vista es SU ItemsControl (ScheduleWeekGrid / ScheduleDayGrid) y la prueba cambia el
  # puntero $script:RejillaId con la vista: todo lo que se cuenta o se pulsa despues de aqui tiene
  # que caer dentro de la rejilla que el operador esta viendo.
  $botonVista = One $win 'Cambiar entre vista' $CT::Button
  L ('G2_boton=' + (Txt $botonVista) + ' | ' + (Invoke-El $botonVista 'CAMBIAR A DIA'))
  Start-Sleep -Seconds 2
  $script:RejillaId = 'ScheduleDayGrid'
  $win = Win-El $s1.Pro.Hwnd
  $celdasDia = Wait-Celdas $s1.Pro.Hwnd 24
  $colsDia = @($celdasDia | ForEach-Object { [int]([Math]::Round((RectOf $_).X / 5)) } | Sort-Object -Unique)
  # Si faltan celdas hay que saber CUALES: la hora se lee del propio nombre de la celda. Con la
  # rejilla encolada a la ventana no debe faltar ninguna; si faltan, son las de abajo del todo.
  $horasDia = Horas-De $celdasDia
  $repartoDia = Reparto-De $celdasDia
  $etiquetasDia = @(ByName $win '^\d\d:00$' $CT::Text).Count
  L ('G2_vista_dia_celdas=' + $celdasDia.Count + ' columnas=' + $colsDia.Count +
     ' | horas_en_la_rejilla=' + ($horasDia -join ' ') + ' | etiquetas=' + $etiquetasDia +
     ' | titulo=' + (Txt (One $win 'las 24 horas' $CT::Text)))
  L ('G2_dia_huecos=' + ((Huecos-De $repartoDia.Hora $HorasUniv 1) -join ' ') +
     ' | dia_leido=' + (($repartoDia.Dia.Keys | Sort-Object) -join ' '))
  # Las DOS rejillas publican sus celdas (la oculta tambien esta en el arbol): por eso se cuenta
  # por rejilla y no por ventana. -1 es la rejilla que no llego a instanciarse.
  L ('   G2_dia_por_rejilla=semana=' + (Celdas-De $s1.Pro.Hwnd 'ScheduleWeekGrid') +
     ' dia=' + (Celdas-De $s1.Pro.Hwnd 'ScheduleDayGrid') +
     ' | arbol=' + (Resumen-Del-Arbol (Arbol-De $s1.Pro.Hwnd)))
  L ('G2_dia_correcta=' + (($celdasDia.Count -eq 24) -and ($colsDia.Count -eq 1) -and ($etiquetasDia -eq 24)))
  CapWindow (Find-Window $script:proc.Id 'Programaci?n - Quasar Radio*') "${Tag}_vista_dia"
  $win = Win-El $s1.Pro.Hwnd
  L ('G2_volver_a_semana=' + (Invoke-El (One $win 'Cambiar entre vista' $CT::Button) 'CAMBIAR A SEMANA'))
  Start-Sleep -Seconds 2
  $script:RejillaId = 'ScheduleWeekGrid'
  $win = Win-El $s1.Pro.Hwnd
  $celdasSem = Wait-Celdas $s1.Pro.Hwnd 168
  $repartoSem = Reparto-De $celdasSem
  L ('G2_semana_de_nuevo=' + ($celdasSem.Count -eq 168) + ' | celdas=' + $celdasSem.Count +
     ' | horas=' + ((Horas-De $celdasSem) -join ' '))
  # Donde caen las que faltan: por hora (cada hora debe estar en los 7 dias) y por dia (cada
  # dia debe tener 24 horas). «23=4» en las horas significa que a esa hora le faltan 3 dias.
  L ('G2_semana_huecos_por_hora=' + ((Huecos-De $repartoSem.Hora $HorasUniv 7) -join ' '))
  L ('G2_semana_huecos_por_dia=' + ((Huecos-De $repartoSem.Dia $DiasUniv 24) -join ' '))
  L ('   G2_semana_por_rejilla=semana=' + (Celdas-De $s1.Pro.Hwnd 'ScheduleWeekGrid') +
     ' dia=' + (Celdas-De $s1.Pro.Hwnd 'ScheduleDayGrid'))
  L ('   G2_semana_arbol=' + (Resumen-Del-Arbol (Arbol-De $s1.Pro.Hwnd)) +
     ' | dias_leidos=' + (($repartoSem.Dia.Keys | Sort-Object) -join ' '))

  # ---- G2c: editar DESDE la vista dia. La aceptacion dice «rejilla horaria editable con vista
  # dia/semana», y con dos rejillas_alternando por Visibility no basta con que la de dia se vea
  # completa: hay que probar que la que el operador ve es la que manda, o sea que APLICAR y QUITAR
  # pulsados sobre una celda de ScheduleDayGrid escriben en la misma celda que luego lee la semana.
  $diaDeVista = (($repartoDia.Dia.Keys | Sort-Object) -join '')
  $horaDelDia = Hora-Libre @()
  $celdaDelDia = ('{0} {1:d2}' -f $diaDeVista, $horaDelDia)
  L ('G2c_editando_en_dia=' + $celdaDelDia + ' (hora libre elegida con Hora-Libre)')
  $win = Win-El $s1.Pro.Hwnd
  L ('   G2c_a_dia=' + (Invoke-El (One $win 'Cambiar entre vista' $CT::Button) 'CAMBIAR A DIA'))
  Start-Sleep -Seconds 2
  $script:RejillaId = 'ScheduleDayGrid'
  $win = Win-El $s1.Pro.Hwnd
  $selDia = Find-Celda $win $celdaDelDia
  if ($null -eq $selDia) { throw "G2c: no esta la celda $celdaDelDia en la rejilla del dia" }
  Invoke-El $selDia ('CELDA ' + $celdaDelDia + ' EN VISTA DIA') | Out-Null
  Start-Sleep -Milliseconds 700
  L ('   G2c_seleccion=' + (Txt (ById (Win-El $s1.Pro.Hwnd) 'SelectedCellTitle')))
  L ('   G2c_ficha_hora=' + (Inv 'FICHA HORA EN DIA' 'tipo de pauta: hora hablada'))
  Set-Text (One (Win-El $s1.Pro.Hwnd) 'Minuto de la celda' $CT::Edit) '20'
  L ('   G2c_aplicar=' + (Inv 'APLICAR DESDE LA VISTA DIA' 'Aplicar la pauta a la celda elegida'))
  Start-Sleep -Seconds 1
  $win = Win-El $s1.Pro.Hwnd
  L ('G2c_celda_en_dia=' + (Nombre-Celda $win $celdaDelDia))
  # Y la misma celda, leida desde la rejilla de la semana: las dos vistas apuntan a los mismos
  # objetos del ViewModel, asi que lo escrito en una se tiene que ver en la otra.
  $win = Win-El $s1.Pro.Hwnd
  L ('   G2c_a_semana=' + (Invoke-El (One $win 'Cambiar entre vista' $CT::Button) 'CAMBIAR A SEMANA'))
  Start-Sleep -Seconds 2
  $script:RejillaId = 'ScheduleWeekGrid'
  $win = Win-El $s1.Pro.Hwnd
  $nombreSem = (Nombre-Celda $win $celdaDelDia)
  L ('G2c_semana_refleja=' + $nombreSem + ' | mismo_objeto=' + ($nombreSem -match 'hora hablada') +
     ' | cabecera=' + (Txt (ById $win 'ProgrammedText')))
  # Se deshace lo escrito para que G3/G8/G9 empiecen con la rejilla limpia, y se comprueba el
  # deshecho desde la propia vista dia (QUITAR pulsado con la rejilla de dia en pantalla).
  $win = Win-El $s1.Pro.Hwnd
  L ('   G2c_a_dia_de_nuevo=' + (Invoke-El (One $win 'Cambiar entre vista' $CT::Button) 'CAMBIAR A DIA'))
  Start-Sleep -Seconds 2
  $script:RejillaId = 'ScheduleDayGrid'
  L ('   G2c_quitar=' + (Inv 'QUITAR DESDE LA VISTA DIA' 'Quitar la pauta de la celda elegida'))
  Start-Sleep -Seconds 1
  $win = Win-El $s1.Pro.Hwnd
  L ('G2c_dia_vuelve_a_libre=' + (Nombre-Celda $win $celdaDelDia))
  $win = Win-El $s1.Pro.Hwnd
  L ('   G2c_fin_a_semana=' + (Invoke-El (One $win 'Cambiar entre vista' $CT::Button) 'CAMBIAR A SEMANA'))
  Start-Sleep -Seconds 2
  $script:RejillaId = 'ScheduleWeekGrid'
  $win = Win-El $s1.Pro.Hwnd
  L ('G2c_semana_libre=' + (Nombre-Celda $win $celdaDelDia) + ' | cabecera=' +
     (Txt (ById $win 'ProgrammedText')))

  # ---- El master a 0 antes de cualquier cosa que pueda salir al aire
  $vol = One $con 'Volumen master' $CT::Slider
  if ($null -eq $vol) { throw 'G5: no se encuentra el slider de master para silenciar' }
  [void]$vol.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(0)
  Start-Sleep -Milliseconds 400
  L ('G5_master_silenciado=' + [Math]::Round($vol.GetCurrentPattern(
    [System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value, 2))

  # Serilog tiene el log abierto en escritura y NO comparte lectura con File.ReadAllText/ReadAllLines
  # («está siendo utilizado en otro proceso»). Se abre a mano con FileShare.ReadWrite, que es lo
  # que usa Get-Content y por eso el resto del script sí puede leerlo.
  function Read-Lines($path) {
    $fs = New-Object IO.FileStream($path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
                                   [IO.FileShare]::ReadWrite)
    try {
      $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
      try { return @($sr.ReadToEnd() -split "`n") } finally { $sr.Dispose() }
    } finally { $fs.Dispose() }
  }

  $logf = Get-ChildItem $logdir -Filter 'quasar_radio-*.log' -ErrorAction SilentlyContinue |
          Sort-Object LastWriteTime -Descending | Select-Object -First 1
  # El criterio de las pruebas de reloj es contar lineas NUEVAS de «Pauta horaria». Se guarda la
  # ruta y el numero de lineas del momento del disparo; si Serilog abre archivo nuevo en mitad de
  # la prueba, se cuentan todas las del nuevo en vez de rebanar el anterior (que estaria mal).
  $script:logPath = if ($null -ne $logf) { $logf.FullName } else { $null }
  $script:logCorte = if ($null -ne $logf) { @(Read-Lines $logf.FullName).Count } else { 0 }
  function Log-File {
    return (Get-ChildItem $logdir -Filter 'quasar_radio-*.log' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1)
  }
  function Snapshot-Log {
    $f = Log-File
    if ($null -eq $f) { $script:logPath = $null; $script:logCorte = 0; return }
    $script:logPath = $f.FullName
    $script:logCorte = @(Read-Lines $f.FullName).Count
  }
  function Nuevas-Pautas {
    $f = Log-File
    if ($null -eq $f) { return -1 }
    $all = @(Read-Lines $f.FullName)
    if ($f.FullName -ne $script:logPath) { $n = $all }
    elseif ($script:logCorte -ge $all.Count) { return 0 }
    else { $n = $all[$script:logCorte..($all.Count - 1)] }
    return @($n | Where-Object { $_ -match 'Pauta horaria' }).Count
  }
  function Lineas-Pauta {
    $f = Log-File
    if ($null -eq $f) { return @() }
    return @(Get-Content $f.FullName -Encoding UTF8 | Where-Object { $_ -match 'Pauta horaria' } | Select-Object -Last 3)
  }

  # ---- G5: una celda programada se CUMPLE (el reloj vive en la consola, no en la ventana)
  # Se programa la celda de la hora EN LA QUE ESTAMOS, al minuto siguiente. Es la unica forma de
  # ver un disparo real sin esperar a una hora fija ni mentir con el reloj del sistema.
  #
  # Todo el bloque (dia, hora, minuto, seleccion y APLICAR) se vuelve a calcular en cada intento:
  # la hora en curso se mira DESPUES de saber a que minuto se apunta, porque en la corrida del
  # 2026-10-23 las tres primeras pruebas se comieron el cambio de hora y la celda quedo escrita
  # en «viernes 04 a los :12» cuando ya eran las 05:11. Esa pauta no se dispara nunca, y la
  # espera de 130 s de abajo la iba a contar siempre como fallo.
  $aplicado = $false
  $blanco = -1
  $celdaViva = ''
  for ($intento = 1; $intento -le 6 -and -not $aplicado; $intento++) {
    $ahora = Get-Date
    $m = [int]$ahora.Minute
    if ($m -ge 54) {
      # Esperar al minuto siguiente NO es un intento fallido, y ademas un «Sleep 55» desde :54
      # solo sube un minuto (:55, :56, :57...) y se queda dentro de la banda: en la corrida 5 los
      # seis intentos se los comieron tres esperas, y en la 7 el contador de «apurados» abortaba
      # G7 sin haberlo intentado. Se sale de la banda de verdad y el intento no se gasta.
      if (-not (Esperar-MinutoTranquilo 'G5')) { throw 'G5: no se pudo salir de la banda 54-59 en 7 min' }
      $intento--
      continue
    }
    $blanco = $m + 1
    $hoy = [int]$ahora.DayOfWeek
    $hh = [int]$ahora.Hour
    # OJO con el nombre: en PowerShell las variables NO distinguen mayusculas de minusculas, y
    # «$diaRe = $DiaRe[$hoy]» se comia el array de patrones. G7 leia luego $DiaRe[5] y sacaba la
    # letra «e» de «viernes»: «G7: no esta la celda e 07 en la rejilla» (corrida 6).
    $diaReCurso = $DiaRe[$hoy]
    $celdaViva = ('{0} {1:00}' -f $diaReCurso, $hh)
    # Las celdas fijas de las pruebas de edicion (miercoles 06, martes 06-12 y 20-22, jueves 09 y la
    # semana a las 20) no pueden ser la celda de la hora en curso: G5-G7 la necesitan intacta para
    # demostrar el disparo y la pausa, y un APLICAR/QUITAR encima dejaria la prueba mintiendo.
    $choque = ($hh -eq 20) -or ($hoy -eq 3 -and $hh -eq 6) -or ($hoy -eq 4 -and $hh -eq 9) -or
              ($hoy -eq 2 -and (($hh -ge 6 -and $hh -le 12) -or ($hh -ge 20 -and $hh -le 22)))
    if ($choque) { throw ("la hora en curso ($diaReCurso $hh) coincide con una celda fija de la prueba; " +
                          'lanzarla en otra hora o cambiar los numeros fijos') }
    L ('G5_celda_de_la_hora_actual=' + $celdaViva + ' | minuto=' + $blanco + ' (intento ' + $intento + ')')
    # La raiz se resuelve AQUI, justo antes de buscar la celda. En la corrida del 2026-10-23 la
    # busqueda usaba el elemento guardado antes de los cambios de vista de G2, y el boton que
    # devolvia era un contenedor ya desconectado del ItemsControl: el Invoke llegaba a un DataContext
    # reciclado y el APLICAR escribia «domingo 17 -> hora hablada» (esta en el log) en vez de la
    # celda de la hora en curso. Un clic sobre un contenedor viejo no falla, escribe en otro sitio.
    $win = Win-El $s1.Pro.Hwnd
    $sel = Find-Celda $win $celdaViva
    if ($null -eq $sel) { throw "G5: no esta la celda $celdaViva en la rejilla" }
    L ('   G5_celda_antes_del_clic=' + $sel.Current.Name)
    Invoke-El $sel 'CELDA DE LA HORA' | Out-Null
    Start-Sleep -Milliseconds 700
    $win = Win-El $s1.Pro.Hwnd
    L ('G5_elegida=' + (Txt (ById $win 'SelectedCellTitle')))
    $chip = One $win 'tipo de pauta: lista automatica' $CT::Button
    Invoke-El $chip 'FICHA AUTO' | Out-Null
    Start-Sleep -Milliseconds 400
    $win = Win-El $s1.Pro.Hwnd
    L ('G5_activa=' + (Poner-Casillero $s1.Pro.Hwnd 'Pauta activa' $true))
    $minBox = One $win 'Minuto de la celda' $CT::Edit
    Set-Text $minBox ([string]$blanco)
    Start-Sleep -Milliseconds 300

    # CONTROL DE DERIVA: lo que se va a aplicar, leido del propio ViewModel a traves de la ventana,
    # no de lo que la prueba cree que pulsó. Si la celda elegida ya no es la de la hora en curso,
    # o el minuto del campo no es el que se escribio, o la ficha AUTO no se registro, APLICAR
    # escribiria en una celda que no se dispara nunca: se avisita todo antes del clic.
    $win = Win-El $s1.Pro.Hwnd
    $titulo = (Txt (ById $win 'SelectedCellTitle'))
    $minutoCampo = (Valor (One $win 'Minuto de la celda' $CT::Edit))
    $estadoEditor = (Get-Estado $win)
    L ('   G5_antes_de_aplicar: seleccion=' + $titulo + ' | minuto_en_el_campo=' + $minutoCampo +
       ' | estado=' + $estadoEditor)
    $horaEsperada = ('{0:00}:00' -f $hh)
    $deriva = (($titulo -notmatch $diaReCurso) -or ($titulo -notmatch [regex]::Escape($horaEsperada)) -or
               ($minutoCampo -ne [string]$blanco) -or ($estadoEditor -notmatch 'lista autom.tica'))
    if ($deriva) {
      L ('   G5_DERIVA (intento ' + $intento + '): se esperaba ' + $diaReCurso + ' ' + $horaEsperada +
         ' minuto ' + $blanco + ' con la ficha AUTO; volviendo a elegir la celda')
      Start-Sleep -Seconds 1
      continue
    }
    Snapshot-Log
    $t0 = Get-Date
    # APLICAR se invoca con el boton vivo (re-resuelto, esperando a que este habilitado y
    # reintentando): en la corrida 5 el Invoke petó con un mensaje vacio y la celda quedo libre,
    # porque el peer del boton se sustituye cuando el editor cambia de estado (H26).
    $nAplicar = Invoke-Vivo $s1.Pro.Hwnd $null 'Aplicar la pauta a la celda elegida' $CT::Button 'APLICAR AUTO'
    L ('G5_aplicar=' + ($nAplicar -gt 0) + ' (intento ' + $nAplicar + ')')
    # Si entre el ultimo vistazo y el clic se paso el minuto objetivo, la pauta de esta hora ya
    # no se cumple nunca: se apunta otra vez, con la hora y el minuto de ahora.
    if ((Get-Date).Minute -lt $blanco) { $aplicado = $true }
    else { L '   G5_el_minuto_objetivo_ya_paso: reintentando' }
  }
  if (-not $aplicado) { throw 'G5: no se pudo apuntar un minuto por delante del reloj' }
  Start-Sleep -Seconds 1
  $win = Win-El $s1.Pro.Hwnd
  L ('G5_celda_escrita=' + (Nombre-Celda $win $celdaViva))
  L ('G5_estado=' + (Get-Estado $win) + ' | proxima=' + (Txt (ById $win 'NextDueText')))

  # El disparo se comprueba en ConsolePautaText, el aviso FIJO del pie de la consola, y no en
  # ConsoleStatusText: ese lo pisa en cuanto arranca la emision que la propia pauta provoca
  # (medido 2026-10-23: «Pauta horaria: viernes 05:31» en el log y el estado cambiado a los
  # 78 ms, con la prueba esperando 130 s un texto que ya no estaba). El aviso fijo es lo que ve
  # el operador, y por eso es lo que hay que comprobar.
  $marca = '{0:00}:{1:00}' -f $hh, $blanco
  $msFuego = Wait-Until {
    $p = ById (Win-El $script:conHwnd) 'ConsolePautaText'
    (($null -ne $p) -and ($p.Current.Name -match [regex]::Escape($marca)))
  } 130000
  if ($msFuego -lt 0) { throw "G5: la pauta $marca no dejo aviso en el pie de la consola" }
  $con = Win-El $s1.Con.Hwnd
  $avisoPauta = (Txt (ById $con 'ConsolePautaText'))
  $estadoConsola = (Txt (ById $con 'ConsoleStatusText'))
  L ('G5_disparo_en_ms=' + $msFuego + ' (desde el clic ' + [int]((Get-Date) - $t0).TotalSeconds + ' s), minuto objetivo ' + $blanco)
  L ('G5_aviso_fijo=' + $avisoPauta)
  L ('G5_consola=' + $estadoConsola)
  L ('G5_disparo_correcto=' + (($avisoPauta -match 'PAUTA') -and ($avisoPauta -match 'lista autom.tica')))
  $cg = ById $con 'ConsoleQueueGrid'
  $filasCola = @(Get-Rows $cg).Count
  L ('G5_cola_de_emision_filas=' + $filasCola + ' (la pauta AUTO regenera la lista: 4 fixtures)')
  L ('G5_log_lineas_nuevas=' + (Nuevas-Pautas))
  foreach ($l in Lineas-Pauta) { L ('   ' + $l.Trim()) }
  $stop = $null
  if ((Wait-Until { $s = ById (Win-El $script:conHwnd) 'MasterStopButton'; (($null -ne $s) -and $s.Current.IsEnabled) } 8000) -ge 0) {
    $stop = ById (Win-El $script:conHwnd) 'MasterStopButton'
    Invoke-El $stop 'STOP tras la pauta' | Out-Null
    Start-Sleep -Seconds 2
    L 'G5_aire_detenido=True (habia emision en curso)'
  } else {
    L ('G5_aire_detenido=no_habia_aire_que_parar (los 4 fixtures de 2 s ya se emitieron solos; ' +
       'estado: ' + (Txt (ById (Win-El $script:conHwnd) 'ConsoleStatusText')) + ')')
  }

  # ---- G6: no se dispara dos veces por la misma ocurrencia
  # El reloj de la rejilla es de un segundo; si el criterio fuese «el tick que cae en este minuto»
  # sin sello de ocurrencia, aqui saldrian 60 disparos.
  Start-Sleep -Seconds 65
  $con = Win-El $s1.Con.Hwnd
  L ('G6_disparos_de_la_misma_ocurrencia=' + (Nuevas-Pautas) + ' (esperado 1)')
  L ('G6_estado_consola_sigue=' + (Txt (ById $con 'ConsoleStatusText')))
  L ('G6_aviso_fijo_sigue=' + (Txt (ById $con 'ConsolePautaText')) + ' | el de G5 era=' + $avisoPauta)

  # ---- G7: una celda programada y PAUSADA no suena cuando llega su minuto
  # Se vuelve a mirar la hora en curso, como en G5: si G5 y G6 cruzaron el cambio de hora, la
  # celda de antes ya paso y «no sonó» no significaria nada. Se programa AUTO en la hora actual
  # y se pausa, y contra esa celda se mide la espera.
  $win = Win-El $s1.Pro.Hwnd
  $pausada = $false
  for ($intento = 1; $intento -le 6 -and -not $pausada; $intento++) {
    $ahora = Get-Date
    $m = [int]$ahora.Minute
    if ($m -ge 54) {
      if (-not (Esperar-MinutoTranquilo 'G7')) { throw 'G7: no se pudo salir de la banda 54-59 en 7 min' }
      $intento--
      continue
    }
    $blanco2 = $m + 1
    $hoy7 = [int]$ahora.DayOfWeek
    $hh7 = [int]$ahora.Hour
    $diaRe7 = $DiaRe[$hoy7]
    $celda7 = ('{0} {1:00}' -f $diaRe7, $hh7)
    $choque7 = ($hh7 -eq 20) -or ($hoy7 -eq 3 -and $hh7 -eq 6) -or ($hoy7 -eq 4 -and $hh7 -eq 9) -or
               ($hoy7 -eq 2 -and (($hh7 -ge 6 -and $hh7 -le 12) -or ($hh7 -ge 20 -and $hh7 -le 22)))
    if ($choque7) { throw "G7: la hora en curso ($celda7) coincide con una celda fija de la prueba" }
    # Igual que en G5: raiz nueva antes de buscar la celda, y control de deriva antes de APLICAR.
    $win = Win-El $s1.Pro.Hwnd
    $sel = Find-Celda $win $celda7
    if ($null -eq $sel) { throw "G7: no esta la celda $celda7 en la rejilla" }
    L ('   G7_celda_antes_del_clic=' + $sel.Current.Name)
    Invoke-El $sel 'CELDA PARA PAUSAR' | Out-Null
    Start-Sleep -Milliseconds 700
    $win = Win-El $s1.Pro.Hwnd
    L ('   G7_ficha_auto_invocada_en_el_intento=' + (Inv 'FICHA AUTO PARA PAUSAR' 'tipo de pauta: lista automatica'))
    Start-Sleep -Milliseconds 300
    L ('   G7_pausa_puesta=' + (Poner-Casillero $s1.Pro.Hwnd 'Pauta activa' $false))
    Set-Text (One (Win-El $s1.Pro.Hwnd) 'Minuto de la celda' $CT::Edit) ([string]$blanco2)
    Start-Sleep -Milliseconds 300
    $win = Win-El $s1.Pro.Hwnd
    $titulo7 = (Txt (ById $win 'SelectedCellTitle'))
    $minuto7 = (Valor (One $win 'Minuto de la celda' $CT::Edit))
    $activa7 = (Estado-Casillero $s1.Pro.Hwnd 'Pauta activa')
    $estado7 = (Get-Estado $win)
    L ('   G7_antes_de_aplicar: seleccion=' + $titulo7 + ' | minuto_en_el_campo=' + $minuto7 +
       ' | activa=' + $activa7 + ' | estado=' + $estado7)
    $horaEsperada7 = ('{0:00}:00' -f $hh7)
    $deriva7 = (($titulo7 -notmatch $diaRe7) -or ($titulo7 -notmatch [regex]::Escape($horaEsperada7)) -or
                ($minuto7 -ne [string]$blanco2) -or ($activa7 -ne 'Off') -or ($estado7 -notmatch 'lista autom.tica'))
    if ($deriva7) {
      L ('   G7_DERIVA (intento ' + $intento + '): se esperaba ' + $celda7 + ' ' + $horaEsperada7 +
         ' minuto ' + $blanco2 + ' en Off con la ficha AUTO; volviendo a elegir la celda')
      Start-Sleep -Seconds 1
      continue
    }
    Snapshot-Log
    $nApl7 = Invoke-Vivo $s1.Pro.Hwnd $null 'Aplicar la pauta a la celda elegida' $CT::Button 'APLICAR PAUSADA'
    L ('G7_aplicar_pausada=' + ($nApl7 -gt 0) + ' (intento ' + $nApl7 +
       ' | celda=' + $celda7 + ' minuto=' + $blanco2 + ' | intento grande ' + $intento + ')')
    if ((Get-Date).Minute -lt $blanco2) { $pausada = $true }
    else { L '   G7_el_minuto_objetivo_ya_paso: reintentando' }
  }
  if (-not $pausada) { throw 'G7: no se pudo apuntar un minuto por delante del reloj' }
  $celdaViva = $celda7
  $hh = $hh7
  $hoy = $hoy7
  Start-Sleep -Seconds 1
  $win = Win-El $s1.Pro.Hwnd
  L ('G7_celda=' + (Nombre-Celda $win $celdaViva) + ' (minuto ' + $blanco2 + ', marcada pausada)')
  L ('G7_cabecera=' + (Txt (ById $win 'ProgrammedText')) + ' | proxima=' + (Txt (ById $win 'NextDueText')))
  L ('G7_pausada_no_sale_como_proxima=' + (-not ((Txt (ById $win 'NextDueText')) -match 'lista autom.tica')))
  $avisoAntes7 = (Txt (ById (Win-El $s1.Con.Hwnd) 'ConsolePautaText'))
  $espera = Wait-Until { (((Get-Date).Hour -ne $hh) -or ((Get-Date).Minute -ge $blanco2)) } 120000
  Start-Sleep -Seconds 8
  L ('G7_minuto_alcanzado_en_ms=' + $espera)
  $con = Win-El $s1.Con.Hwnd
  L ('G7_disparos_con_celda_pausada=' + (Nuevas-Pautas) + ' (esperado 0)')
  # El pie fijo, no la linea de estado: «ConsoleStatusText» dura lo que tarda la pauta en arrancar
  # la emision (medido en G5: 78 ms), asi que decir 8 segundos despues que «no pone PAUTA» no
  # probaria nada. El aviso de pauta se queda pegado, asi que si la celda pausada hubiese sonado
  # aqui habria cambiado. Se compara con el aviso que habia antes de esperar y con la hora objetivo.
  $marca7 = ('{0:00}:{1:00}' -f $hh7, $blanco2)
  $avisoG7 = (Txt (ById $con 'ConsolePautaText'))
  L ('G7_aviso_fijo_antes=' + $avisoAntes7)
  L ('G7_aviso_fijo_despues=' + $avisoG7 + ' | objetivo=' + $marca7)
  L ('G7_consola_no_mando_nada=' + ((-not ($avisoG7 -match [regex]::Escape($marca7))) -and ($avisoG7 -eq $avisoAntes7)))

  # ---- G3: editar y APLICAR desde el editor de la celda
  $win = Win-El $s1.Pro.Hwnd
  $celdaEdicion = 'mi.rcoles 06'
  $sel = Find-Celda $win $celdaEdicion
  if ($null -eq $sel) { throw 'G3: no esta la celda miercoles 06' }
  Invoke-El $sel 'CELDA MIERCOLES 06' | Out-Null
  Start-Sleep -Milliseconds 700
  $win = Win-El $s1.Pro.Hwnd
  L ('G3_elegida=' + (Txt (ById $win 'SelectedCellTitle')) + ' | ' + (Nombre-Celda $win $celdaEdicion))
  $progAntes = (Txt (ById $win 'ProgrammedText'))
  Inv 'FICHA HORA' 'tipo de pauta: hora hablada' | Out-Null
  Start-Sleep -Milliseconds 300
  Set-Text (One $win 'Minuto de la celda' $CT::Edit) '15'
  Start-Sleep -Milliseconds 300
  Inv 'APLICAR HORA' 'Aplicar la pauta a la celda elegida' | Out-Null
  Start-Sleep -Seconds 1
  $win = Win-El $s1.Pro.Hwnd
  # El testigo se mide DESPUES del APLICAR: contarlo antes solo diria 0, que no es una prueba de
  # nada (la celda estaba libre de serie).
  $fichaActiva = @((Get-Celdas-En (Get-Rejilla $win)) | Where-Object { $_.Current.Name -match '^celda mi.rcoles 06 .*hora hablada' }).Count
  L ('G3_ficha_marcada_en_rejilla=' + $fichaActiva + ' (el testigo de la celda se pinta al aplicar)')
  L ('G3_celda=' + (Nombre-Celda $win $celdaEdicion))
  L ('G3_aplicar_escribe=' + ((Nombre-Celda $win $celdaEdicion) -match 'hora hablada'))
  L ('G3_cabecera_antes=' + $progAntes + ' -> ' + (Txt (ById $win 'ProgrammedText')) + ' | estado=' + (Get-Estado $win))

  # ---- G4: la celda pausada se lee como pausada y QUITAR la deja libre
  $win = Win-El $s1.Pro.Hwnd
  Invoke-El (Find-Celda $win $celdaEdicion) 'RE-SELECCIONAR' | Out-Null
  Start-Sleep -Milliseconds 600
  $win = Win-El $s1.Pro.Hwnd
  L ('   G4_pausa_puesta=' + (Poner-Casillero $s1.Pro.Hwnd 'Pauta activa' $false))
  Inv 'APLICAR EN PAUSA' 'Aplicar la pauta a la celda elegida' | Out-Null
  Start-Sleep -Seconds 1
  $win = Win-El $s1.Pro.Hwnd
  L ('G4_celda_pausada=' + (Nombre-Celda $win $celdaEdicion))
  L ('G4_marcar_pausada=' + ((Nombre-Celda $win $celdaEdicion) -match '\(pausada\)'))
  Inv 'QUITAR' 'Quitar la pauta de la celda elegida' | Out-Null
  Start-Sleep -Seconds 1
  $win = Win-El $s1.Pro.Hwnd
  L ('G4_quitar_deja_libre=' + (Nombre-Celda $win $celdaEdicion) + ' -> ' +
     ((Nombre-Celda $win $celdaEdicion) -match 'libre$'))

  # ---- G8: RELLENAR HASTA
  $win = Win-El $s1.Pro.Hwnd
  $celdaRango = 'martes 06'
  Invoke-El (Find-Celda $win $celdaRango) 'CELDA MARTES 06' | Out-Null
  Start-Sleep -Milliseconds 600
  $win = Win-El $s1.Pro.Hwnd
  Inv 'FICHA HORA' 'tipo de pauta: hora hablada' | Out-Null
  Set-Text (One $win 'Minuto de la celda' $CT::Edit) '0'
  Set-Text (One $win 'Hora hasta la que rellena el rango' $CT::Edit) '12'
  Start-Sleep -Milliseconds 300
  $progAntes = (Txt (ById $win 'ProgrammedText'))
  L ('   G8_rellenar_invocado_en_el_intento=' + (Inv 'RELLENAR HASTA' 'Rellenar el rango de horas del dia'))
  Start-Sleep -Seconds 2
  $win = Win-El $s1.Pro.Hwnd
  # El separador del nombre de la celda es « · » (punto medio con espacio y espacio), o sea DOS
  # caracteres entre la hora y la ficha. En la corrida 9 este patrón llevaba «.hora», un solo
  # carácter, y G8 decía 0 celdas rellenas teniendo 8 en la cabecera y «Martes: 7 hora(s) de 06:00
  # a 12:00» en el estado: mentía la comprobación, no la aplicación. Se cuenta con el mismo
  # comodín de resto de línea que usan G3 y G9, y se escribe el booleano para que no haya que
  # traducir el número a mano.
  $enRango = @((Get-Celdas-En (Get-Rejilla $win)) | Where-Object { $_.Current.Name -match '^celda martes (0[6-9]|1[0-2]) .*hora hablada' })
  L ('G8_celdas_del_rango=' + $enRango.Count + ' (esperadas 7: de 06:00 a 12:00) | relleno_correcto=' +
     ($enRango.Count -eq 7))
  L ('G8_cabecera=' + $progAntes + ' -> ' + (Txt (ById $win 'ProgrammedText')) + ' | estado=' + (Get-Estado $win))
  L ('G8_fuera_del_rango_libre=' + ((Nombre-Celda $win 'martes 13') -match 'libre$'))

  # ---- G9: A TODA LA SEMANA, y el guardia de la referencia vacia
  # Las horas de los rellenos se eligen mirando el reloj EN ESTE MOMENTO y evitando la banda
  # 06-13 (la del rango de G8): si la prueba cruza un cambio de hora, una pauta «de toda la
  # semana» podria caer encima de la hora en curso y disparar de mas. (Hora-Libre se definio con
  # los demas helpers, arriba, porque G2c tambien la necesita y en PowerShell una funcion no
  # existe hasta que el script pasa por su definicion.)
  $horaSemana = Hora-Libre @()
  $horaPegado = Hora-Libre @($horaSemana)
  $horaTanda  = Hora-Libre @($horaSemana, $horaPegado)
  L ('G9_horas_elegidas: semana=' + $horaSemana + ' pegado=' + $horaPegado + ' tanda=' + $horaTanda +
     ' (en curso=' + (Get-Date).Hour + ')')
  $celdaSemana = 'martes {0:d2}' -f $horaSemana
  $celdaPegado = 'martes {0:d2}' -f $horaPegado
  $celdaTanda  = 'jueves {0:d2}' -f $horaTanda

  $win = Win-El $s1.Pro.Hwnd
  Invoke-El (Find-Celda $win $celdaSemana) ('CELDA ' + $celdaSemana) | Out-Null
  Start-Sleep -Milliseconds 600
  $win = Win-El $s1.Pro.Hwnd
  Inv 'FICHA CART' 'tipo de pauta: cart de la botonera' | Out-Null
  Set-Text (One $win 'Referencia de la pauta' $CT::Edit) ' '
  Set-Text (One $win 'Minuto de la celda' $CT::Edit) '5'
  Start-Sleep -Milliseconds 300
  L ('G9_pista_de_la_referencia=' + (Txt (ById $win 'RefHintText')))
  Inv 'APLICAR CART SIN REFERENCIA' 'Aplicar la pauta a la celda elegida' | Out-Null
  Start-Sleep -Seconds 1
  $win = Win-El $s1.Pro.Hwnd
  L ('G9_cart_sin_referencia_no_se_escribe=' + ((Nombre-Celda $win $celdaSemana) -match 'libre$') +
     ' | estado=' + (Get-Estado $win))
  Inv 'FICHA HORA' 'tipo de pauta: hora hablada' | Out-Null
  Set-Text (One $win 'Minuto de la celda' $CT::Edit) '0'
  Inv 'A TODA LA SEMANA' 'Aplicar la pauta a la misma hora los siete dias' | Out-Null
  Start-Sleep -Seconds 2
  $win = Win-El $s1.Pro.Hwnd
  $patSemana = '^celda \S+ {0:d2} .*hora hablada' -f $horaSemana
  $siete = @((Get-Celdas-En (Get-Rejilla $win)) | Where-Object { $_.Current.Name -match $patSemana })
  L ('G9_semana_completa=' + $siete.Count + ' (esperadas 7)')
  L ('   ' + (($siete | ForEach-Object { $_.Current.Name }) -join ' / '))
  L ('G9_estado=' + (Get-Estado $win))

  # ---- G10: COPIAR / PEGAR
  $win = Win-El $s1.Pro.Hwnd
  Invoke-El (Find-Celda $win $celdaSemana) ('COPIAR ' + $celdaSemana) | Out-Null
  Start-Sleep -Milliseconds 600
  $win = Win-El $s1.Pro.Hwnd
  L ('   G10_copiar_invocado_en_el_intento=' + (Inv 'COPIAR' 'Copiar la celda elegida'))
  Start-Sleep -Milliseconds 600
  $win = Win-El $s1.Pro.Hwnd
  L ('G10_portapapeles=' + (Txt (ById $win 'ClipboardText')))
  Invoke-El (Find-Celda $win $celdaPegado) ('PEGAR EN ' + $celdaPegado) | Out-Null
  Start-Sleep -Milliseconds 600
  $win = Win-El $s1.Pro.Hwnd
  L ('G10_destino_antes=' + (Nombre-Celda $win $celdaPegado))
  L ('   G10_pegar_invocado_en_el_intento=' + (Inv 'PEGAR' 'Pegar la celda copiada en la elegida'))
  Start-Sleep -Seconds 1
  $win = Win-El $s1.Pro.Hwnd
  L ('G10_destino_despues=' + (Nombre-Celda $win $celdaPegado))
  L ('G10_pegar_copia_la_pauta=' + ((Nombre-Celda $win $celdaPegado) -match 'hora hablada'))
  CapWindow (Find-Window $script:proc.Id 'Programaci?n - Quasar Radio*') "${Tag}_semana_editada"

  # ---- G13a: una tanda se programa desde su propio boton
  $win = Win-El $s1.Pro.Hwnd
  Invoke-El (Find-Celda $win $celdaTanda) ('CELDA ' + $celdaTanda) | Out-Null
  Start-Sleep -Milliseconds 600
  $win = Win-El $s1.Pro.Hwnd
  $poner = @(ByName $win 'Programar esta tanda en la celda elegida' $CT::Button)
  L ('G13a_boton_por_tanda=' + $poner.Count)
  Invoke-El $poner[0] 'PONER TANDA EN LA CELDA' | Out-Null
  Start-Sleep -Seconds 1
  $win = Win-El $s1.Pro.Hwnd
  L ('G13a_celda=' + (Nombre-Celda $win $celdaTanda))
  L ('G13a_tanda_programada=' + ((Nombre-Celda $win $celdaTanda) -match 'tanda '))
  L ('G13a_estado=' + (Get-Estado $win))

  # ---- G11: GUARDAR es de administrador; primero hay que crear la cuenta del sandbox
  $con = Win-El $s1.Con.Hwnd
  L ('G11_abrir_usuarios=' + (Invoke-El (One $con 'Abrir la ventana de usuarios' $CT::Button) 'USUARIOS'))
  Start-Sleep -Seconds 3
  $usu = Find-Window $script:proc.Id '*Usuarios y niveles*'
  if ($null -eq $usu) { throw 'G11: no abrio la ventana de usuarios' }
  $usuEl = Win-El $usu.Hwnd
  $script:frontHwnd = $usu.Hwnd
  Set-Text (ById $usuEl 'UsernameBox') 'jefa'
  [void](Set-Password $usuEl 'PasswordBox' 'QuasarPrueba2026')
  L ('G11_rol_por_defecto=' + (Txt (ById $usuEl 'RoleBox')) + ' (la primera cuenta es Admin: aun no hay cuentas)')
  Invoke-El (One $usuEl 'Crear la cuenta' $CT::Button) 'CREAR CUENTA' | Out-Null
  Start-Sleep -Seconds 2
  $usuEl = Win-El $usu.Hwnd
  L ('G11_cuenta_creada=' + ((Get-Estado $usuEl) -match 'Cuenta creada') + ' | ' + (Get-Estado $usuEl) +
     ' | users.json=' + (Test-Path $usersJ))
  [void][QsrProg]::Close([IntPtr]$usu.Hwnd)
  Start-Sleep -Seconds 2

  $win = Win-El $s1.Pro.Hwnd
  $script:frontHwnd = $s1.Pro.Hwnd
  # La ventana se vuelve a buscar por titulo antes del clic de raton: G1 la maximizo, y el rect
  # guardado al arrancar (1180x780 en 210,40) dejaria GUARDAR «fuera de la ventana» para Click-El.
  $s1.Pro = Find-Window $script:proc.Id 'Programaci?n - Quasar Radio*'
  $script:frontHwnd = $s1.Pro.Hwnd
  $progAntes = (Txt (ById $win 'ProgrammedText'))
  $guardar = One $win 'Guardar programacion' $CT::Button
  if ($null -eq $guardar) { throw 'G11: no esta el boton GUARDAR' }
  L ('G11_clic_guardar=' + (Click-El $guardar 'GUARDAR' $s1.Pro))
  $login = Wait-Login $script:proc.Id
  L ('G11_guardar_pide_identificacion=' + ($null -ne $login))
  if ($null -ne $login) {
    CapWindow $login "G11_${Tag}_pide_clave"
    $lgEl = Win-El $login.Hwnd
    $script:frontHwnd = $login.Hwnd
    Set-Text (ById $lgEl 'UsernameBox') 'jefa'
    [void](Set-Password $lgEl 'PasswordBox' 'QuasarPrueba2026')
    Click-El (One $lgEl 'Identificarse y continuar' $CT::Button) 'CONTINUAR' $login | Out-Null
    $msCierre = Wait-Until { $null -eq (Get-Login $script:proc.Id) } 20000
    L ('G11_modal_cerrado_en_ms=' + $msCierre)
  }
  Start-Sleep -Seconds 3
  $win = Win-El $s1.Pro.Hwnd
  $jsonTxt = ''
  if (Test-Path $scheduleJ) { $jsonTxt = Get-Content $scheduleJ -Raw }
  $entradas = ([regex]::Matches($jsonTxt, '"day"')).Count
  L ('G11_estado=' + (Get-Estado $win) + ' (cabecera antes: ' + $progAntes + ')')
  L ('G11_guardado_en_el_sandbox=' + (Test-Path $scheduleJ) + ' | entradas_en_el_json=' + $entradas)
  L ('G11_json_camelCase=' + ($jsonTxt -match '"slots"') + ' | sin_tmp_sueltos=' +
     (-not (Test-Path ($scheduleJ + '.tmp'))))
  CapWindow (Find-Window $script:proc.Id 'Programaci?n - Quasar Radio*') "${Tag}_guardada"
  L ('G11_celdas_en_pantalla=' + (@(Get-Celdas-En (Get-Rejilla $win) | Where-Object { $_.Current.Name -notmatch 'libre$' }).Count))

  # ==================================================================== SESION 2
  $s1.Proc.Kill(); Start-Sleep -Seconds 3
  $script:proc = $null

  $s2 = Start-QsrProg 'sesion 2'
  $script:proc = $s2.Proc
  $win2 = $s2.ProEl
  $con2 = $s2.ConEl
  $script:proHwnd = $s2.Pro.Hwnd
  $script:conHwnd = $s2.Con.Hwnd
  $script:frontHwnd = $s2.Pro.Hwnd

  # ---- G12: la rejilla vuelve del disco, con las pausas incluidas
  L ('G12_maximizada=' + (Maximize $s2.Pro.Hwnd))
  $win2 = Win-El $s2.Pro.Hwnd
  $e2 = Get-Estado $win2
  L ('G12_estado=' + $e2)
  L ('G12_recuperada=' + ($e2 -match 'Recuperada schedule.json'))
  L ('G12_cabecera=' + (Txt (ById $win2 'ProgrammedText')) + ' | proxima=' + (Txt (ById $win2 'NextDueText')))
  L ('G12_rango_martes=' + (Nombre-Celda $win2 'martes 09') + ' | semana=' +
     (Nombre-Celda $win2 ('viernes {0:d2}' -f $horaSemana)) + ' | pegado=' + (Nombre-Celda $win2 $celdaPegado))
  L ('G12_pausa_sobrevive=' + ((Nombre-Celda $win2 $celda7) -match '\(pausada\)') +
     ' (la celda pausada de G7 era ' + $celda7 + ')')
  L ('G12_pega_sobrevive=' + ((Nombre-Celda $win2 $celdaPegado) -match 'hora hablada'))
  L ('G12_tanda_sobrevive=' + ((Nombre-Celda $win2 $celdaTanda) -match 'tanda '))
  $recuperadas = @(@(Get-Celdas-En (Get-Rejilla $win2)) | Where-Object { $_.Current.Name -notmatch 'libre$' }).Count
  L ('G12_celdas_renovadas=' + $recuperadas + ' | backup=' + (Test-Path $backupJ))
  CapWindow (Find-Window $script:proc.Id 'Programaci?n - Quasar Radio*') "${Tag}_tras_reinicio"

  # ---- G13b: cerrar Programacion no cierra la emisora, y el modulo se puede volver a abrir
  $con2 = Win-El $s2.Con.Hwnd
  [void][QsrProg]::Close([IntPtr]$s2.Pro.Hwnd)
  $msCierre = Wait-Until {
    (@([QsrProg]::Top($script:proc.Id) | Where-Object { $_.Visible -and $_.Title -like 'Programaci?n - Quasar Radio*' }).Count -eq 0)
  } 15000
  L ('G13_ventana_cerrada_en_ms=' + $msCierre)
  L ('G13_consola_viva=' + (@([QsrProg]::Top($script:proc.Id) |
     Where-Object { $_.Visible -and $_.Title -like 'Quasar Radio Pro*' }).Count -gt 0))
  $con2 = Win-El $s2.Con.Hwnd
  L ('G13_consola_sigue_operando=' + (Invoke-El (ById $con2 'ClearQueueButton') 'VACIAR COLA'))
  Start-Sleep -Seconds 2
  $con2 = Win-El $s2.Con.Hwnd
  L ('G13_estado_consola=' + (Txt (ById $con2 'ConsoleStatusText')))
  $btnReabrir = One $con2 'Abrir Programaci.n' $CT::Button
  L ('G13_reabrir_desde_la_consola=' + (Invoke-El $btnReabrir 'ABRIR PROGRAMACION'))
  Start-Sleep -Seconds 3
  $pro3 = Find-Window $script:proc.Id 'Programaci?n - Quasar Radio*'
  L ('G13_reabrio=' + ($null -ne $pro3))
  if ($null -ne $pro3) {
    $w3 = Win-El $pro3.Hwnd
    L ('G13_reabierta_con_lo_mismo=' + (Txt (ById $w3 'ProgrammedText')) + ' | ' + (Get-Estado $w3))
  }
}
catch {
  L ('ERROR: ' + $_.Exception.Message)
  foreach ($l in (($_.ScriptStackTrace -split "`n") | Select-Object -First 3)) { L ('   AT: ' + $l.Trim()) }
}
finally {
  # El log se resuelve aqui mismo, no con el archivo que se cogio en G5: si la prueba ha reventado
  # antes, el vuelco de evidencias tiene que decir algo.
  $logNow = Get-ChildItem $logdir -Filter 'quasar_radio-*.log' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if ($null -ne $logNow) {
    L ('LOG=' + $logNow.Name)
    foreach ($m in @('Rejilla horaria', 'Pauta horaria', 'Programaci.n guardada', 'Cuenta creada', 'autenticado')) {
      $hit = Select-String -Path $logNow.FullName -Pattern $m | Select-Object -Last 3
      foreach ($h in $hit) { L ('   ' + $h.Line.Trim()) }
    }
    $q = Select-String -Path $logNow.FullName -Pattern 'schedule.json' | Select-Object -Last 4
    foreach ($h in $q) { L ('   ' + $h.Line.Trim()) }
  } else { L 'LOG=no hay archivo de log' }

  if ($null -ne $script:proc -and -not $script:proc.HasExited) {
    $script:proc.Kill(); Start-Sleep -Seconds 2
  }
  foreach ($z in @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)) { $z.Kill() }
  Start-Sleep -Seconds 1
  L ('RESTANTES=' + (@(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue).Count))
  L ('AUDIT_DESPUES: bin/track_metadata.json md5=' + (Short-Hash $binMeta) +
     ' (antes=' + $binMetaAntes + ') -> sin cambios=' + ((Short-Hash $binMeta) -eq $binMetaAntes))
  L ('AUDIT_DESPUES: schedule.json real del fundador=' +
     (Test-Path (Join-Path $env:LOCALAPPDATA 'QuasarRadio\schedule.json')) + ' (esperado False)')
  $layoutAfter = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
  if (Test-Path $layoutAfter) { Remove-Item $layoutAfter -Force }
  if (-not $Keep -and (Test-Path $Sandbox)) { Remove-Item $Sandbox -Recurse -Force }
  else { L ('SANDBOX_CONSERVADO=' + $Sandbox) }
  $res = Join-Path $out "uia_${Tag}_programacion.txt"
  [IO.File]::WriteAllLines($res, $lines, (New-Object Text.UTF8Encoding($true)))
  Write-Host ('EVIDENCIA=' + $res)
}
