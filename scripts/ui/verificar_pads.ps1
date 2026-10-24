param(
  [string]$Theme = 'Studio Dark Neon',
  [string]$Tag = 'ui8',
  [string]$Sandbox = (Join-Path $env:TEMP 'qsr_pads_test'),
  [switch]$Keep
)

# UI-8: verificacion en vivo de la botonera SFX.
#
# Cubre la queja del fundador ("el menu contextual de los pads no tiene funcion ni contenido;
# deberia poder regular el volumen de cada pad y su ducking") con medidas leidas desde fuera por
# UIA, no con capturas. Los puntos que se comprueban:
#   P0  los doce pads existen, son botones reales y cada uno lleva su mezcla escrita (HelpText)
#   P1  la botonera viene de cart_pads.json: nombre, ruta, volumen, ducking y modo del disco
#   P2  el menu contextual sale con clic derecho y sus nueve lineas estan HABITADAS (antes
#       salia con dos lineas muertas: el mando enlazaba contra la ventana desde un Popup)
#   P3  cambiar el volumen desde el submenú se ve en la cara del pad, en el menu y en el JSON
#   P4  el ducking es de verdad: con un pad en el aire el VU de salida BAJA el factor pedido
#   P5  el volumen del pad se aplica EN VIVO, sin volver a disparar el clip
#   P6  los dos modos de disparo: inicio/parada corta, disparar y deja el clip sonar hasta el
#       final y la segunda pulsacion lo reinicia
#   P7  la pre-escucha del pad sale por el monitor y NO entra en la salida (el VU se queda en cero)
#   P8  lo ajustado sobrevive al reinicio (cart_pads.json + cart_pads_backup.json, sin .tmp)
#   P9  renombrar/asignar/quitar siguen siendo de nivel: sin login abren la identificacion y, al
#       cancelarla, la botonera no cambia
#   PX  pulsar un pad cuyo archivo ya no esta NO abre ningun dialogo: lo dice en el pie
#   PF  el pie de la consola muestra los avisos de la botonera (efecto lateral: MainViewModel
#       reavisaba por el nombre y el nombre StatusText significa otra cosa en cada modelo)
#
# Se ejecuta SIEMPRE contra una libreria de ensayo en %TEMP%, nunca contra la real. La libreria de
# la emisora vive en bin/Debug/net10.0-windows (musica/, efectos/, horarias/) y no esta en git: un
# error aqui la borraria sin copia. QUASAR_RADIO_DATA_DIR apunta al sandbox para que cart_pads.json
# y users.json de ensayo no toquen los datos del fundador, y el exe arranca con
# -WorkingDirectory $Sandbox porque el escaneo de "efectos" y "musica" es relativo al directorio.
#
# Los archivos de ensayo llevan el prefijo QSRPAD. Jamas HRS ni MIN: con esos nombres se pisaron
# 31 grabaciones reales de horarias/ (ver docs/UI_BASELINE.md).
#
# RUIDO: las unicas ventanas con el master arriba son P4 (el ducking se mide en la salida) y P5 (el
# volumen del pad se mide en la salida). Sin ellas no hay forma de demostrar que el ducking y el
# nivel por pad existen. Todo lo demas corre con el master en cero o por el monitor.

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if (-not ('QsrPad' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class PadWin { public IntPtr Hwnd; public string Title; public bool Visible; public string Rect; public int L; public int T; public int W; public int H; }
public static class QsrPad {
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
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
  public static void Raise(IntPtr h) {
    ShowWindow(h, 5); BringWindowToTop(h);
    keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
    SetForegroundWindow(h);
  }
  public static void Restore(IntPtr h) { ShowWindow(h, 9); BringWindowToTop(h); }
  public static bool Minimizada(IntPtr h) { return IsIconic(h); }
  public static void Close(IntPtr h) { SendMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }
  public static void Click(int x, int y) {
    SetCursorPos(x, y);
    mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
    mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
  }
  // Clic derecho: es LA prueba de UI-8. El menu de la botonera se abre con el raton, y un
  // ContextMenu de WPF vive en una ventana Popup aparte; solo desde ahi dentro se puede ver si los
  // mandos resuelven o llegan nulos.
  public static void RightClick(int x, int y) {
    SetCursorPos(x, y);
    mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero);
    mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero);
  }
  // ESC cierra el menu sin pulsar nada: ningun mando de prueba puede depender de que el clic
  // siguiente caiga sobre la opcion correcta.
  public static void Esc() {
    keybd_event(0x1B, 0, 0, UIntPtr.Zero);
    keybd_event(0x1B, 0, 2, UIntPtr.Zero);
  }
  static int _target; static List<PadWin> _found;
  public static List<PadWin> Top(int pid) {
    _target = pid; _found = new List<PadWin>();
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == _target) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        var wi = new PadWin();
        wi.Hwnd = h; wi.Title = sb.ToString(); wi.Visible = IsWindowVisible(h);
        wi.L = r.Left; wi.T = r.Top; wi.W = r.Right - r.Left; wi.H = r.Bottom - r.Top;
        wi.Rect = r.Left + "," + r.Top + " " + wi.W + 'x' + wi.H;
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
$VP    = [System.Windows.Automation.ValuePattern]
$RVP   = [System.Windows.Automation.RangeValuePattern]
$ECP   = [System.Windows.Automation.ExpandCollapsePattern]
$CHECK = [string][char]0x2713   # el gancho con el que el menu marca la opcion vigente

function CTCond($ct) { New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ct) }
function PidCond($pid_) { New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $pid_) }
function ById($root, $id) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
  return $root.FindFirst($Scope::Descendants, $c)
}
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
function Txt($e) { if ($null -eq $e) { return '(ausente)' } return $e.Current.Name }
function Invoke-El($e) { [void]$e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Set-Range($e, $v) { [void]$e.GetCurrentPattern($RVP::Pattern).SetValue($v) }
function HelpOf($e) { if ($null -eq $e) { return '(ausente)' } return $e.Current.HelpText }

function RectOf($e) {
  $r = $e.Current.BoundingRectangle
  return [pscustomobject]@{ X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
}

function Wait-Until([scriptblock]$Test, [int]$Ms) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $Ms) {
    try { if (& $Test) { return [int]$sw.ElapsedMilliseconds } } catch { }
    Start-Sleep -Milliseconds 200
  }
  return -1
}

# LA MEDIDA. El medidor expone RangeValue (0..100). Se lee post-fader y post-ducking desde el bus,
# asi que es la unica forma de demostrar que el volumen del pad y el ducking existen de verdad.
function Vu-Value($root, $id) {
  $e = ById $root $id
  if ($null -eq $e) { return $null }
  try { return [double]$e.GetCurrentPattern($RVP::Pattern).Current.Value } catch { }
  try { return [double]$e.GetCurrentPattern($VP::Pattern).Current.Value } catch { return $null }
}

function Vu-Serie($root, $id, [int]$n, [int]$everyMs) {
  $vals = @()
  for ($i = 0; $i -lt $n; $i++) {
    $v = Vu-Value $root $id
    if ($null -ne $v) { $vals += $v }
    Start-Sleep -Milliseconds $everyMs
  }
  if ($vals.Count -eq 0) { return [pscustomobject]@{ n = 0; min = -1; max = -1; last = -1 } }
  return [pscustomobject]@{
    n    = $vals.Count
    min  = [Math]::Round(($vals | Measure-Object -Minimum).Minimum, 2)
    max  = [Math]::Round(($vals | Measure-Object -Maximum).Maximum, 2)
    last = [Math]::Round($vals[-1], 2)
  }
}

function Describe-Vu($root, $id, $rotulo, [int]$n = 10, [int]$everyMs = 150) {
  $s = Vu-Serie $root $id $n $everyMs
  L ($rotulo + ' [' + $id + '] lecturas=' + $s.n + ' min=' + $s.min + ' max=' + $s.max + ' ultimo=' + $s.last)
  return $s
}

function Ratio($a, $b) {
  if ($null -eq $a -or $null -eq $b -or $b -le 0) { return -1 }
  return [Math]::Round($a / $b, 3)
}

function InRange($v, $lo, $hi) { return (($v -ge $lo) -and ($v -le $hi)) }

# ---- pads, por el Nombre accesible que llevan en la cara (F1..F12)
function Get-PadEl($root, $name) {
  foreach ($e in $root.FindAll($Scope::Descendants, (CTCond $CT::Button))) {
    if ($e.Current.Name -eq $name) { return $e }
  }
  return $null
}

# ---- clic de raton, nunca InvokePattern, sobre lo que pueda abrir un modal.
# Leccion ya aprendida (verificar_permisos.ps1): Invoke() sobre un mando cuyo manejador abre un
# ShowDialog NO devuelve hasta que el modal se cierra. Los pads vacios y las lineas de nivel
# pueden acabar en una ventana modal, asi que todo lo de UI-8 se pulsa de verdad.
function Raise-Front($hwnd) {
  for ($i = 1; $i -le 3; $i++) {
    [void][QsrPad]::Raise([IntPtr]$hwnd)
    Start-Sleep -Milliseconds 250
    if ([QsrPad]::GetForegroundWindow() -eq [IntPtr]$hwnd) { return $true }
  }
  return $false
}

function Click-Point([int]$x, [int]$y, [string]$que) {
  [QsrPad]::Click($x, $y)
  L ('  CLIC ' + $que + ' en ' + $x + ',' + $y)
  return $true
}

function Click-Pad($root, $win, $name, [string]$que) {
  $e = Get-PadEl $root $name
  if ($null -eq $e) { L ('  PAD ' + $name + ' (' + $que + '): NO ENCONTRADO'); return $false }
  if (-not (Raise-Front $script:frontHwnd)) { L ('  PAD ' + $name + ' (' + $que + '): la consola no esta al frente'); return $false }
  $r = RectOf $e
  if ($r.W -lt 4 -or $r.H -lt 4) { L ('  PAD ' + $name + ' (' + $que + '): rect invalida'); return $false }
  if ($r.X -lt $win.L -or ($r.X + $r.W) -gt ($win.L + $win.W) -or $r.Y -lt $win.T -or ($r.Y + $r.H) -gt ($win.T + $win.H)) {
    L ('  PAD ' + $name + ' (' + $que + '): fuera de la ventana (' + $r.X + ',' + $r.Y + ' vs ' + $win.Rect + ')')
    return $false
  }
  return Click-Point ($r.X + [int]($r.W / 2)) ($r.Y + [int]($r.H / 2)) $que
}

# ---- el menu contextual. Un ContextMenu de WPF NO cuelga de la consola: es una ventana Popup del
# mismo proceso, y por eso se busca desde la raiz de UIA filtrando por ProcessId, no dentro del
# arbol de la ventana principal. Ahi dentro estaba el bug que reporto el fundador.
function Get-MenuItems($pid_) {
  $items = @()
  try {
    foreach ($w in @($AE::RootElement.FindAll($Scope::Children, (PidCond $pid_)))) {
      try {
        foreach ($m in @($w.FindAll($Scope::Descendants, (CTCond $CT::MenuItem)))) { $items += $m }
      } catch { }
    }
  } catch { }
  return ,@($items)
}

function Open-PadMenu($root, $win, $name, [string]$que) {
  $e = Get-PadEl $root $name
  if ($null -eq $e) { L ('  MENU ' + $name + ' (' + $que + '): pad no encontrado'); return $null }
  if (-not (Raise-Front $script:frontHwnd)) { L ('  MENU ' + $name + ' (' + $que + '): la consola no esta al frente'); return $null }
  $r = RectOf $e
  [QsrPad]::RightClick($r.X + [int]($r.W / 2), $r.Y + [int]($r.H / 2))
  $ms = Wait-Until { (Get-MenuItems $script:proc.Id).Count -ge 4 } 6000
  $items = Get-MenuItems $script:proc.Id
  if ($ms -lt 0) {
    L ('  MENU ' + $name + ' (' + $que + '): no abrio; lineas vistas=' + $items.Count)
    return $null
  }
  L ('  MENU ' + $name + ' (' + $que + '): abierto en ' + $ms + ' ms con ' + $items.Count + ' lineas')
  return ,@($items)
}

function Find-MenuItem($items, [string]$pattern) {
  foreach ($m in $items) {
    if ($m.Current.Name -match $pattern) { return $m }
  }
  return $null
}

function Click-MenuItem($item, [string]$que) {
  if ($null -eq $item) { L ('  LINEA ' + $que + ': NO ENCONTRADA'); return $false }
  $r = RectOf $item
  if ($r.W -lt 4 -or $r.H -lt 4) { L ('  LINEA ' + $que + ': rect invalida'); return $false }
  return Click-Point ($r.X + [int]($r.W / 2)) ($r.Y + [int]($r.H / 2)) ('menu ' + $que)
}

# Desplegar un submenú. ExpandCollapse es el patron de UIA; si no lo expone, se despliega con el
# mismo clic de raton con el que lo haria una persona.
function Expand-MenuItem($item, [string]$que) {
  if ($null -eq $item) { L ('  SUBMENU ' + $que + ': padre no encontrado'); return $false }
  try {
    $ec = $item.GetCurrentPattern($ECP::Pattern)
    $ec.Expand()
    Start-Sleep -Milliseconds 400
    L ('  SUBMENU ' + $que + ': desplegado por ExpandCollapse')
    return $true
  } catch {
    [void](Click-MenuItem $item $que)
    Start-Sleep -Milliseconds 500
    L ('  SUBMENU ' + $que + ': desplegado por clic')
    return $true
  }
}

function Close-Menu {
  [QsrPad]::Esc()
  Start-Sleep -Milliseconds 300
  $n = (Get-MenuItems $script:proc.Id).Count
  L ('  MENU CERRADO: lineas restantes=' + $n)
}

# Esperar a que el submenu REALMENTE exista. ExpandCollapse.Expand() vuelve antes de que WPF
# monte el Popup del submenu: las tres lecturas que se hacian justo despues de Expand() podian
# ver solo las 8 lineas del padre (dos de tres submenus se quedaron sin hijos en la regrescion
# del 2026-10-24; en la corrida de UI-8 habian caido del lado afortunado de la carrera).
# Se espera hasta que el conteo supere el del padre, acotado, y entonces se lee.
function Wait-SubmenuItems([int]$padre, [int]$Ms) {
  [void](Wait-Until { (Get-MenuItems $script:proc.Id).Count -gt $padre } $Ms)
  $leidos = Get-MenuItems $script:proc.Id
  # 'return ,@($leidos)' y NO '@(Get-MenuItems)': Get-MenuItems ya devuelve su arbolito con el
  # comma-outer, y volver a envolverlo deja en $items UN elemento que es el array entero — la
  # membresia sobre ese objeto imprime TODAS las lineas pegadas y el RectOf del clic revienta
  # con "System.Object[] -> System.Int32" (medido en la re-corrida de hoy).
  return ,@($leidos)
}

# ---- WAV de ensayo: PCM 16 bits mono. Amplitud 0 = silencio absoluto (el pad con el que se mide
# el ducking: no puede sumar nada a la salida, asi que lo que baja es la musica y solo la musica).
function New-QsrWav([string]$Path, [double]$Seconds, [int]$Hz, [int]$Amp) {
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
    $s = 0
    if ($Amp -gt 0) { $s = [int]([Math]::Sin(2 * [Math]::PI * $Hz * ($i / $rate)) * $Amp) }
    $bw.Write([int16]$s)
  }
  $bw.Flush(); [IO.File]::WriteAllBytes($Path, $ms.ToArray())
  $bw.Close(); $ms.Close()
}

$MUS  = 'QSRPAD Musica - Cuarenta s.wav'   # 40 s, 9000/32768: la musica a la que hay que bajarle el nivel
$PT   = 'QSRPAD Tono - Veinte s.wav'       # 20 s, nivel bajo: el pad con volumen regulable
$PM   = 'QSRPAD Mudo - Veinte s.wav'       # 20 s, silencio: el pad con el que se mide el ducking
$PG   = 'QSRPAD Golpe - Seis s.wav'        # 6 s: el pad de disparar y dejar
$PL   = 'QSRPAD Perdido - No existe.wav'   # NUNCA se crea: el pad que apunta a un archivo borrado

function New-Sandbox {
  if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'musica')     | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'efectos')    | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'jingles')    | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'importados') | Out-Null
  New-QsrWav (Join-Path $Sandbox ('musica\' + $MUS)) 40.0 240 9000
  New-QsrWav (Join-Path $Sandbox ('efectos\' + $PT)) 20.0 523 5000
  New-QsrWav (Join-Path $Sandbox ('efectos\' + $PM)) 20.0 240 0
  New-QsrWav (Join-Path $Sandbox ('efectos\' + $PG))  6.0 700 6000
  L ('FIXTURES: 4 WAV QSRPAD en ' + $Sandbox + ' (y ' + $PL + ' a proposito sin crear)')
}

# cart_pads.json SEMBRADO antes de arrancar: es la otra mitad de UI-8. La botonera ya no se
# reconstruye por orden de aparicion en efectos/, se lee del disco con su nombre, su ruta, su
# volumen, su ducking y su modo. Los titulos a proposito DISTINTOS del nombre del archivo, para
# que se vea en pantalla que lo que manda es el JSON y no el escaneo de la carpeta.
function New-PadsJson {
  $efx = Join-Path $Sandbox 'efectos'
  $pads = @()
  $pads += [pscustomobject]@{ padId = 1; shortcut = 'F1'; title = 'QSRTono';    filePath = (Join-Path $efx $PT); colorHex = '#FF1744'; volumePercent = 70;  duckPercent = 50; mode = 'Toggle' }
  $pads += [pscustomobject]@{ padId = 2; shortcut = 'F2'; title = 'QSRMudo';    filePath = (Join-Path $efx $PM); colorHex = '#00B0FF'; volumePercent = 100; duckPercent = 75; mode = 'Toggle' }
  $pads += [pscustomobject]@{ padId = 3; shortcut = 'F3'; title = 'QSRGolpe';   filePath = (Join-Path $efx $PG); colorHex = '#00E676'; volumePercent = 100; duckPercent = 0;  mode = 'OneShot' }
  $pads += [pscustomobject]@{ padId = 4; shortcut = 'F4'; title = 'QSRPerdido'; filePath = (Join-Path $efx $PL); colorHex = '#651FFF'; volumePercent = 100; duckPercent = 0;  mode = 'Toggle' }
  $file = [pscustomobject]@{ version = 1; pads = $pads }
  $json = $file | ConvertTo-Json -Depth 4
  $path = Join-Path $Sandbox 'cart_pads.json'
  [IO.File]::WriteAllText($path, $json, (New-Object Text.UTF8Encoding($false)))
  L ('SEED: cart_pads.json con 4 pads (F1 70%/D50, F2 100%/D75, F3 OneShot, F4 archivo inexistente)')
}

function Read-PadsJson($path) {
  if (-not (Test-Path $path)) { return $null }
  try { return (Get-Content $path -Raw | ConvertFrom-Json) } catch { L ('  JSON ileible: ' + $_.Exception.Message); return $null }
}

function Json-Pad($data, [int]$id) {
  if ($null -eq $data) { return $null }
  return @($data.pads | Where-Object { [int]$_.padId -eq $id }) | Select-Object -First 1
}

function Find-Window($pid_, $pattern) {
  return ([QsrPad]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like $pattern } | Select-Object -First 1)
}

function Start-Consola([string]$modo) {
  $p = Start-Process -FilePath $exe -WorkingDirectory $Sandbox -PassThru
  $script:proc = $p
  Start-Sleep -Seconds 11
  $c = Find-Window $p.Id 'Quasar Radio Pro*'
  if ($null -eq $c) { throw ('sin consola en ' + $modo) }
  $script:frontHwnd = $c.Hwnd
  if ([QsrPad]::Minimizada([IntPtr]$c.Hwnd)) { [void][QsrPad]::Restore([IntPtr]$c.Hwnd); Start-Sleep -Seconds 2 }
  $el = $AE::FromHandle([IntPtr]$c.Hwnd)
  # Centinela: la frase mas profunda del arbol. Mientras el escaneo tiene ocupado al hilo de UI,
  # FindAll devuelve solo la parte de arriba (H11) y los pads parecerian no existir.
  $ms = Wait-Until { $null -ne (One $el 'abre su propia ventana' $CT::Text) } 60000
  if ($ms -lt 0) { throw ('consola sin centinela en ' + $modo) }
  $ms2 = Wait-Until { $null -ne (Get-PadEl $el 'F12') } 20000
  L ('ARRANQUE[' + $modo + ']: arbol uia completo en ' + $ms + ' ms | pad F12 visible a los ' + $ms2 + ' ms tras los 11 s de cortesia | consola=' + $c.Rect)
  return [pscustomobject]@{ Proc = $p; Con = $c; ConEl = $el }
}

$exe  = 'C:\Users\CY-KILL\Desktop\Programación y Datanálisis\quasar_radio\bin\Debug\net10.0-windows\quasar_radio.exe'
$logdir = Join-Path (Split-Path $exe) 'logs'
$out    = Join-Path $env:TEMP 'qsr_ui'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$np = Join-Path $Sandbox 'nowplaying.txt'

$lines = New-Object Collections.ArrayList
function L($m) { $lines.Add($m) | Out-Null; Write-Host $m }

function CapWindow($win, $t) {
  if ($null -eq $win) { L ('CAP ' + $t + ' : sin ventana'); return }
  $w = [int]$win.W; $h = [int]$win.H
  if ($w -lt 50 -or $h -lt 50) { L ('CAP ' + $t + ' : rect invalida'); return }
  $bmp = New-Object Drawing.Bitmap($w, $h)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrPad]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 350
  $hdc = $g.GetHdc()
  $ok = [QsrPad]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $path = Join-Path $out ($t + '.png')
  $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  L ('CAP ' + $t + ' : PrintWindow=' + $ok + ' ' + $w + 'x' + $h + ' md5=' + (Get-FileHash $path -Algorithm MD5).Hash)
}

function CapWindowCrop($win, $x, $y, $w, $h, $t) {
  if ($w -lt 20 -or $h -lt 20) { L ('CAP ' + $t + ' : recorte invalido ' + $w + 'x' + $h); return }
  $bmp = New-Object Drawing.Bitmap([int]$win.W, [int]$win.H)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrPad]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 300
  $hdc = $g.GetHdc()
  [void][QsrPad]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $cx = [int]($x - $win.L); $cy = [int]($y - $win.T)
  if ($cx -lt 0) { $cx = 0 }; if ($cy -lt 0) { $cy = 0 }
  if ($cx + $w -gt $bmp.Width) { $w = $bmp.Width - $cx }
  if ($cy + $h -gt $bmp.Height) { $h = $bmp.Height - $cy }
  if ($w -lt 20 -or $h -lt 20) { L ('CAP ' + $t + ' : recorte fuera de la ventana'); $bmp.Dispose(); return }
  $crop = New-Object Drawing.Bitmap([int]$w, [int]$h, ([Drawing.Imaging.PixelFormat]::Format32bppArgb))
  $g2 = [Drawing.Graphics]::FromImage($crop)
  $g2.DrawImage($bmp, (New-Object Drawing.Rectangle(0, 0, [int]$w, [int]$h)), $cx, $cy, [int]$w, [int]$h, [Drawing.GraphicsUnit]::Pixel)
  $g2.Dispose()
  $path = Join-Path $out ($t + '.png')
  $crop.Save($path, [Drawing.Imaging.ImageFormat]::Png)
  $crop.Dispose(); $bmp.Dispose()
  L ('CAPC ' + $t + ' : PrintWindow ' + $w + 'x' + $h + ' md5=' + (Get-FileHash $path -Algorithm MD5).Hash)
}

# H36: PrintWindow deja el arbol UIA de la ventana sin contenido para el resto de la sesion del
# cliente. Toda la medicion va ANTES de las capturas, que se hacen aqui, al final del todo.
function Cap-PadStrip($win, $root) {
  $first = Get-PadEl $root 'F1'
  $last  = Get-PadEl $root 'F12'
  if ($null -eq $first -or $null -eq $last) { L 'CAP botonera : sin pads que recortar'; return }
  $a = RectOf $first; $b = RectOf $last
  $x = [Math]::Min($a.X, $b.X) - 8
  $y = [Math]::Min($a.Y, $b.Y) - 34
  $w = ([Math]::Max($a.X + $a.W, $b.X + $b.W) - $x) + 16
  $h = ([Math]::Max($a.Y + $a.H, $b.Y + $b.H) - $y) + 12
  CapWindowCrop $win $x $y $w $h ($Tag + '_botonera')
}

$pre = @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)
if ($pre.Count -gt 0) { throw ('Ya hay ' + $pre.Count + ' instancia(s) abiertas; abortando para no tocar la emision.') }

# Huella de los datos REALES antes de empezar. La prueba escribe su botonera en el sandbox; si algo
# cae en el directorio de datos del fundador o en bin/, este informe lo dice.
$env:QUASAR_RADIO_DATA_DIR = $Sandbox
$realBin  = Split-Path $exe
$realData = Join-Path $env:LOCALAPPDATA 'QuasarRadio'
$realPads = Join-Path $realData 'cart_pads.json'
$metaReal = Join-Path $realBin 'track_metadata.json'
function Hash-Or($path, $ifmissing) { if (Test-Path $path) { return (Get-FileHash $path -Algorithm MD5).Hash } return $ifmissing }
$padsAntes = Hash-Or $realPads '(no existe)'
$metaAntes = Hash-Or $metaReal '(no existe)'
$musicaAntes = @(Get-ChildItem (Join-Path $realBin 'musica') -File -ErrorAction SilentlyContinue).Count
$efectosAntes = @(Get-ChildItem (Join-Path $realBin 'efectos') -File -ErrorAction SilentlyContinue).Count

$layout = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
if (Test-Path $layout) { Remove-Item $layout -Force }
$conf = Join-Path $env:APPDATA 'QuasarRadio\appearance.json'
Set-Content -Path $conf -Value ('{"theme":"' + $Theme + '"}') -Encoding UTF8

$script:proc = $null
$script:frontHwnd = $null

try {
  L ('TEMA=' + $Theme + ' | DATA_DIR(sandbox)=' + $env:QUASAR_RADIO_DATA_DIR)

  # ================================ SESION 1 ================================
  New-Sandbox
  New-PadsJson
  $s1 = Start-Consola 'sesion 1'
  $con = $s1.ConEl
  $win = $s1.Con

  # ---- P0: los doce pads son botones y cada uno lleva su mezcla escrita encima
  $faltan = @()
  $ayud = @{}
  for ($i = 1; $i -le 12; $i++) {
    $e = Get-PadEl $con ('F' + $i)
    if ($null -eq $e) { $faltan += ('F' + $i); continue }
    $r = RectOf $e
    $ayud['F' + $i] = HelpOf $e
    if ($r.W -lt 30 -or $r.H -lt 20) { L ('  P0_pad F' + $i + ' : rect pequena ' + $r.W + 'x' + $r.H) }
    if (-not $e.Current.IsEnabled)  { L ('  P0_pad F' + $i + ' : DESHABILITADO (sin login deberia poder dispararse)') }
  }
  L ('P0_doce_pads_como_botones=' + ($faltan.Count -eq 0) + ' | faltan=[' + ($faltan -join ' ') + ']')
  L ('P0_mezcla_legible_F1=[' + $ayud['F1'] + '] F2=[' + $ayud['F2'] + '] F3=[' + $ayud['F3'] + '] F4=[' + $ayud['F4'] + '] F5=[' + $ayud['F5'] + ']')
  L ('P0_F1_es_70_con_D50=' + ($ayud['F1'] -eq '70% · D50') + ' | P0_F2_es_100_con_D75=' + ($ayud['F2'] -eq '100% · D75') +
     ' (los dos numeros vienen del JSON sembrado, no de un ajuste a mano)')
  $chip = Txt (ById $con 'PadInventoryChip')
  L ('P0_inventario_de_la_botonera=' + ($chip -match '^3 de 12 con clip$') + ' | chip=' + $chip)
  $sinArchivo = One $con 'SIN ARCHIVO' $CT::Text
  L ('P0_la_cara_del_pad_sin_archivo_lo_dice=' + ($null -ne $sinArchivo) + ' | texto=' + (Txt $sinArchivo))

  # ---- P1: manda el JSON, no el orden de la carpeta efectos/
  $caras = @()
  foreach ($t in @('QSRTono', 'QSRMudo', 'QSRGolpe')) {
    $caras += ($t + '=' + (($null -ne (One $con $t $CT::Text))) + ' ')
  }
  L ('P1_los_nombres_del_json_estan_en_pantalla ' + ($caras -join '') +
     '| en_carta=' + ((($null -ne (One $con 'QSRTono' $CT::Text)) -and ($null -ne (One $con 'QSRMudo' $CT::Text)) -and ($null -ne (One $con 'QSRGolpe' $CT::Text)))))
  L ('P1_por_la_ruta_no_por_el_nombre_del_archivo=' + (($null -eq (One $con ('QSRPAD Tono') $CT::Text))))
  $pie0 = Txt (ById $con 'ConsoleStatusText')
  L ('P1_pie_de_consola_al_arrancar=' + $pie0)

  # ---- P2: el menu contextual sale con clic derecho y trae lineas HABITADAS
  $items = Open-PadMenu $con $win 'F1' 'contenido'
  $p2abierto = ($null -ne $items)
  $nombres = @()
  $muertas = @()
  if ($p2abierto) {
    foreach ($m in $items) {
      $n = $m.Current.Name
      $nombres += ('[' + $n + '|' + $(if ($m.Current.IsEnabled) { 'on' } else { 'OFF' }) + ']')
      if (-not $m.Current.IsEnabled) { $muertas += $n }
    }
  }
  L ('P2_menu_abierto=' + $p2abierto + ' | lineas=' + $(if ($p2abierto) { $items.Count } else { 0 }))
  L ('P2_estado_de_cada_linea=' + ($nombres -join ' '))
  $dispara = Find-MenuItem $items 'Cortar|Sacar al aire'
  $prel    = Find-MenuItem $items 'escucha'
  $volum   = Find-MenuItem $items 'Volumen del pad'
  $duck    = Find-MenuItem $items 'Bajar la'
  $modo    = Find-MenuItem $items 'Modo de disparo'
  $renom   = Find-MenuItem $items 'Renombrar'
  $asign   = Find-MenuItem $items 'Asignar audio'
  $quitar  = Find-MenuItem $items 'Quitar el clip'
  L ('P2_ocho_lineas_con_funcion=' + (($null -ne $dispara) -and ($null -ne $prel) -and ($null -ne $volum) -and ($null -ne $duck) -and ($null -ne $modo) -and ($null -ne $renom) -and ($null -ne $asign) -and ($null -ne $quitar)) +
     ' | disparo=[' + (Txt $dispara) + '] | volumen=[' + (Txt $volum) + '] | duck=[' + (Txt $duck) + ']')
  L ('P2_lineas_deshabilitadas=' + $muertas.Count + ' [' + ($muertas -join ' / ') + '] (el menu viejo salia con dos lineas muertas: el mando enlazaba contra la ventana desde el Popup)')
  L ('P2_el_nombre_del_pad_se_lee_en_la_primera_linea=' + ((Txt $dispara) -match 'QSRTono'))
  Close-Menu

  # ---- P7: pre-escucha por el monitor, sin entrar en la salida. Se prueba con todo parado para
  # que el VU de salida en cero sea una medida limpia.
  $vol = One $con 'Volumen master' $CT::Slider
  $volSfx = One $con 'Volumen de la botonera SFX' $CT::Slider
  Set-Range $vol 0
  if ($null -ne $volSfx) { Set-Range $volSfx 1.0 }
  Start-Sleep -Milliseconds 300
  L ('P7_faders_preparados: master=' + [Math]::Round($vol.GetCurrentPattern($RVP::Pattern).Current.Value, 2) +
     ' botonera=' + $(if ($null -ne $volSfx) { [Math]::Round($volSfx.GetCurrentPattern($RVP::Pattern).Current.Value, 2) } else { '(sin slider)' }))
  $items = Open-PadMenu $con $win 'F1' 'pre-escucha'
  [void](Click-MenuItem (Find-MenuItem $items 'Pre-escuchar') 'pre-escucha por el monitor')
  $msPre = Wait-Until { ((Txt (ById $con 'ConsoleStatusText')) -match 'Pre-escucha F1') } 8000
  $preTxt = Txt (ById $con 'ConsoleStatusText')
  L ('P7_pre_escucha_anunciada_ms=' + $msPre + ' | pie=' + $preTxt)
  L ('P7_dice_claramente_que_no_sale_al_aire=' + ($preTxt -match 'no sale al aire'))
  $pv = Describe-Vu $con 'MasterLeftVu' 'P7_mientras_suena_la_pre-escucha' 8 150
  $busPre = Txt (ById $con 'BusSourcesText')
  L ('P7_no_contamina_la_salida=' + (($pv.max -le 1.0) -and ($busPre -match 'SIN')) + ' | nivel=' + $pv.max +
     ' | fuentes_del_bus=' + $busPre + ' (el pad se oye por AUX 3, que no empuja al bus de salida)')
  $parar = One $con 'PARAR SFX' $CT::Button
  if ($null -ne $parar) { Invoke-El $parar }
  $msPreOff = Wait-Until { ((Txt (ById $con 'ConsoleStatusText')) -match 'Terminada la pre-escucha|pre-escucha quitada|Parados los pads') } 8000
  L ('P7_la_pre-escucha_se_suelta_ms=' + $msPreOff + ' | pie=' + (Txt (ById $con 'ConsoleStatusText')))
  Start-Sleep -Milliseconds 600
  $preAyud = HelpOf (Get-PadEl $con 'F1')
  $preNivel = Describe-Vu $con 'MasterLeftVu' 'P7_al_soltarla_la_salida_sigue_en_cero' 5 150
  L ('P7_la_pre-escucha_no_dejo_el_pad_encendido=' + (($preNivel.max -le 1.0) -and ($preAyud -eq '70% · D50')) + ' | F1=[' + $preAyud + ']')

  # ---- P4: EL DUCKING MEDIDO. Deck al aire con el master arriba; un pad MUDO entra en el aire con
  # su ducking anotado: si el VU de salida baja exactamente al factor pedido, la musica se baja de
  # verdad y el numero que pinta la consola es el de esa bajada.
  Set-Range $vol 1.0
  Start-Sleep -Milliseconds 300
  $play = ById $con 'MasterPlayButton'
  if ($null -eq $play -or -not $play.Current.IsEnabled) { throw 'el PLAY de la consola no esta habilitado con la cola cargada' }
  Invoke-El $play
  $msAire = Wait-Until { (Test-Path $np) -and ((Get-Content $np -Raw) -match 'QSRPAD Musica') } 20000
  if ($msAire -lt 0) { throw 'la pista de ensayo nunca salio al aire; sin musica no hay ducking que medir' }
  L ('P4_al_aire_en_ms=' + $msAire + ' | nowplaying=' + (Get-Content $np -Raw).Trim())
  $base = Describe-Vu $con 'MasterLeftVu' 'P4_la_musica_sola' 10 150
  $bus0 = Txt (ById $con 'BusSourcesText')
  L ('P4_fuentes_del_bus_sin_pad=' + $bus0)
  $duck0 = Txt (ById $con 'DuckReadout')
  # Un TextBlock con texto vacio puede no llegar al arbol: lo que se niega es que haya un aviso.
  L ('P4_sin_aviso_de_ducking_al_parar=' + (($duck0 -eq '') -or ($duck0 -eq '(ausente)')) + ' | aviso=[' + $duck0 + ']')

  [void](Click-Pad $con $win 'F2' 'ducking 75')
  $msDuck = Wait-Until { ((Txt (ById $con 'DuckReadout')) -match '75') } 6000
  $duckTxt = Txt (ById $con 'DuckReadout')
  L ('P4_aviso_de_musica_bajada_ms=' + $msDuck + ' | aviso=' + $duckTxt)
  $msPad = Wait-Until { ((Txt (ById $con 'BusSourcesText')) -match 'PAD 2') } 6000
  $bus1 = Txt (ById $con 'BusSourcesText')
  L ('P4_el_pad_en_el_aire_aparece_en_el_bus_ms=' + $msPad + ' | fuentes=' + $bus1 + ' (un pad que suena ES aire: UI-7)')
  Start-Sleep -Milliseconds 400
  $durante = Describe-Vu $con 'MasterLeftVu' 'P4_la_musica_con_el_pad_de_d75_en_el_aire' 10 150
  $rDuck = Ratio $durante.max $base.max
  L ('P4_la_salida_baja_al_factor_pedido=' + (InRange $rDuck 0.15 0.40) + ' | ratio=' + $rDuck + ' (duck 75 % => ~0,25) | ' + $base.max + ' -> ' + $durante.max)
  L ('P4_la_bajada_tambien_se_ve_en_decibelios=' + (InRange (Ratio (Vu-Value $con 'MasterRightVu') $base.max) 0.15 0.45) +
     ' | lectura=' + (Txt (ById $con 'MasterLevelReadout')))

  # P4b: cambiar la profundidad MIENTRAS el pad suena tiene que notarse en ese clip.
  $items = Open-PadMenu $con $win 'F2' 'duck en vivo'
  [void](Expand-MenuItem (Find-MenuItem $items 'Bajar la') 'submenu de ducking')
  $items = Wait-SubmenuItems $items.Count 6000
  $marca = @($items | Where-Object { $_.Current.Name.Contains($CHECK) } | Select-Object -First 1)
  L ('P4b_la_opcion_vigente_va_marcada_dentro_del_submenu=' + ($marca.Count -eq 1) + ' | marcada=[' + $(if ($marca.Count -eq 1) { $marca[0].Current.Name } else { '(ninguna)' }) + ']')
  # Las hojas del submenu empiezan por el numero ("    90 % (casi muda)"); la cabecera del submenu
  # lleva el valor vigente detras del punto ("Bajar la música · 75 %"), asi que el ancla al inicio
  # es lo que distingue una linea de la otra.
  $lineasDuck = @($items | Where-Object { $_.Current.Name -match '^\s*(90 %|90 % \(casi muda\))' })
  L ('P4b_lineas_del_submenu_duck=' + $items.Count + ' | la del 90=[' + $(if ($lineasDuck.Count -gt 0) { $lineasDuck[0].Current.Name } else { '(no esta)' }) + ']')
  [void](Click-MenuItem ($lineasDuck | Select-Object -First 1) 'ducking 90 casi muda')
  $msD90 = Wait-Until { ((Txt (ById $con 'DuckReadout')) -match '90') } 6000
  Start-Sleep -Milliseconds 600
  $d90 = Describe-Vu $con 'MasterLeftVu' 'P4b_con_el_duck_a_90_sonando_el_mismo_clip' 8 150
  $r90 = Ratio $d90.max $base.max
  L ('P4b_la_nueva_profundidad_vale_para_lo_que_queda_de_clip_ms=' + $msD90 + ' | ratio=' + $r90 + ' (duck 90 => ~0,10) | ' + $base.max + ' -> ' + $d90.max)
  L ('P4b_bajada_medible_y_aun_aire=' + (InRange $r90 0.02 0.30) + ' | aviso=' + (Txt (ById $con 'DuckReadout')))

  [void](Click-Pad $con $win 'F2' 'cortar el pad')
  $msOff = Wait-Until { ((Txt (ById $con 'BusSourcesText')) -notmatch 'PAD 2') } 8000
  $cut = Txt (ById $con 'ConsoleStatusText')
  L ('P4_cortar_el_pad_saca_del_bus_ms=' + $msOff + ' | pie=' + $cut + ' | fuentes=' + (Txt (ById $con 'BusSourcesText')))
  Start-Sleep -Milliseconds 800
  $recup = Describe-Vu $con 'MasterLeftVu' 'P4_la_musica_vuelve_a_su_nivel_al_cortar' 8 150
  $rRec = Ratio $recup.max $base.max
  L ('P4_la_recuperacion_es_completa=' + (InRange $rRec 0.85 1.15) + ' | ratio=' + $rRec + ' (suelta en 0,45 s, no de golpe)')
  L ('P4_el_aviso_de_bajada_desaparece=' + ((Txt (ById $con 'DuckReadout')) -notmatch '[0-9]') + ' | aviso=[' + (Txt (ById $con 'DuckReadout')) + ']')

  # ---- P3 y P5: el VOLUMEN DEL PAD, medido en la salida y cambiado desde el submenú sin volver a
  # disparar el clip. El deck se para antes: la medida tiene que ser solo del pad.
  Invoke-El (ById $con 'MasterStopButton')
  Start-Sleep -Milliseconds 900
  $vacio = Describe-Vu $con 'MasterLeftVu' 'P5_la_salida_en_cero_con_el_deck_parado' 6 150
  L ('P5_aire_parado_sin_nivel=' + ($vacio.max -le 1.0))

  [void](Click-Pad $con $win 'F1' 'tono al 70')
  $msP5 = Wait-Until { ((Txt (ById $con 'BusSourcesText')) -match 'PAD 1') } 6000
  $padBase = Describe-Vu $con 'MasterLeftVu' 'P5_el_pad_a_70_de_volumen' 10 150
  L ('P5_pad_al_aire_ms=' + $msP5 + ' | fuentes=' + (Txt (ById $con 'BusSourcesText')) + ' | nivel70=' + $padBase.max)
  $items = Open-PadMenu $con $win 'F1' 'volumen en vivo'
  [void](Expand-MenuItem (Find-MenuItem $items 'Volumen del pad') 'submenu de volumen')
  $items = Wait-SubmenuItems $items.Count 6000
  L ('P3_submenu_de_volumen_completo=' + (@($items | Where-Object { $_.Current.Name -match '^\s*\d+ %$' }).Count -ge 11) +
     ' | lineas_vistas=' + $items.Count)
  $treinta = @($items | Where-Object { ($_.Current.Name -notmatch $CHECK) -and ($_.Current.Name -match '^\s*30 %$') }) | Select-Object -First 1
  [void](Click-MenuItem $treinta 'volumen 30 %')
  $msVol = Wait-Until { ((HelpOf (Get-PadEl $con 'F1')) -eq '30% · D50') } 8000
  L ('P3_la_cara_del_pad_refleja_el_cambio_ms=' + $msVol + ' | ayuda=' + (HelpOf (Get-PadEl $con 'F1')))
  L ('P3_el_pie_confirma_al_operador=' + ((Txt (ById $con 'ConsoleStatusText')) -match '30') + ' | pie=' + (Txt (ById $con 'ConsoleStatusText')))
  Start-Sleep -Milliseconds 500
  $pad30 = Describe-Vu $con 'MasterLeftVu' 'P5_el_mismo_clip_sonando_al_30_sin_re_disparar' 8 150
  $rVol = Ratio $pad30.max $padBase.max
  L ('P5_el_nivel_baja_con_el_volumen_en_vivo=' + (InRange $rVol 0.30 0.58) + ' | ratio=' + $rVol + ' (30/70 = ~0,43) | ' + $padBase.max + ' -> ' + $pad30.max)
  $jsonS1 = Read-PadsJson (Join-Path $Sandbox 'cart_pads.json')
  $p1json = Json-Pad $jsonS1 1
  L ('P3_cart_pads.json_guardado_solo_al_ajustar=' + (($null -ne $p1json) -and ([int]$p1json.volumePercent -eq 30)) +
     ' | pad1: volumen=' + $(if ($null -ne $p1json) { $p1json.volumePercent } else { '(sin json)' }) + ' duck=' + $(if ($null -ne $p1json) { $p1json.duckPercent } else { '' }))

  # ---- P6: los dos modos de disparo
  [void](Click-Pad $con $win 'F1' 'cortar el pad 1')
  Start-Sleep -Milliseconds 600
  $modo1 = Txt (ById $con 'ConsoleStatusText')
  L ('P6_toggle:la_segunda_pulsacion_corta=' + ($modo1 -match 'cortado') + ' | pie=' + $modo1 +
     ' | fuentes=' + (Txt (ById $con 'BusSourcesText')))
  [void](Click-Pad $con $win 'F3' 'golpe one-shot, primera pulsacion')
  $msG = Wait-Until { ((Txt (ById $con 'BusSourcesText')) -match 'PAD 3') } 6000
  $g1 = Describe-Vu $con 'MasterLeftVu' 'P6_el_golpe_sale_al_aire' 6 150
  L ('P6_oneshot_primera_pulsacion_en_el_bus_ms=' + $msG + ' | nivel=' + $g1.max + ' | pie=' + (Txt (ById $con 'ConsoleStatusText')))
  Start-Sleep -Milliseconds 700
  [void](Click-Pad $con $win 'F3' 'golpe one-shot, segunda pulsacion')
  Start-Sleep -Milliseconds 400
  # Serie corta tras el reinicio: aqui es donde se ve si el aviso de parada del clip viejo se lleva
  # puesto al recien nacido (el pad queda mudo y el bus vuelve a SIN SENAL).
  $serieG = @()
  for ($i = 1; $i -le 8; $i++) {
    $serieG += ((Txt (ById $con 'BusSourcesText')) + '/' + (Vu-Value $con 'MasterLeftVu'))
    Start-Sleep -Milliseconds 150
  }
  L ('P6_serie_tras_reiniciar=' + ($serieG -join ' | '))
  $pie2 = Txt (ById $con 'ConsoleStatusText')
  $busG = Txt (ById $con 'BusSourcesText')
  L ('P6_oneshot:la_segunda_pulsacion_reinicia_y_no_deja_el_pad_mudo=' + (($pie2 -match 'F3 al aire') -and ($busG -match 'PAD 3')) + ' | pie=' + $pie2 + ' | fuentes=' + $busG)
  $vivoAntes = ($busG -match 'PAD 3')
  $msFin = Wait-Until { ((Txt (ById $con 'BusSourcesText')) -notmatch 'PAD 3') } 12000
  L ('P6_oneshot:el_clip_se_deja_terminar_solo_ms=' + $msFin + ' (el WAV dura 6 s) | estaba_vivo_antes=' + $vivoAntes +
     ' | fuentes=' + (Txt (ById $con 'BusSourcesText')))

  # ---- PX: pulsar un pad cuyo archivo ya no esta NO abre ningun dialogo
  $ventanasAntes = @([QsrPad]::Top($script:proc.Id) | Where-Object { $_.Visible }).Count
  [void](Click-Pad $con $win 'F4' 'pad con archivo perdido')
  Start-Sleep -Milliseconds 1200
  $ventanasDespues = @([QsrPad]::Top($script:proc.Id) | Where-Object { $_.Visible }).Count
  $pieX = Txt (ById $con 'ConsoleStatusText')
  L ('PX_sin_dialogo_encima_del_aire=' + ($ventanasDespues -le $ventanasAntes) + ' | ventanas ' + $ventanasAntes + ' -> ' + $ventanasDespues)
  L ('PX_lo_dice_en_el_pie=' + (($pieX -match 'ya no esta') -or ($pieX -match 'no tiene clip')) + ' | pie=' + $pieX)
  # PF: el aviso de la botonera tiene que LLEGAR al pie. MainViewModel reavisaba por el nombre de la
  # propiedad, y StatusText se llama igual en los dos modelos pero significa otra cosa: sin el
  # espejo del puente, todo lo de arriba (corte, nivel, guardado, pad sin archivo) se quedaba en el
  # sub-ViewModel y el operador no veia nada.
  L ('PF_el_pie_de_la_consola_muestra_los_avisos_de_la_botonera=' + (($cut -match 'Pad F2 cortado') -and ($pieX -match 'F4')) +
     ' | cortes_leidos=[' + $cut + '] / [' + $pieX + ']')

  # ---- P9: renombrar sigue siendo de nivel: sin login abre la identificacion y al cancelarla no
  # toca nada. Se hace con clic de raton: Invoke sobre una linea cuyo manejador hace ShowDialog
  # no devuelve hasta cerrar el modal.
  $items = Open-PadMenu $con $win 'F1' 'puerta de nivel'
  [void](Click-MenuItem (Find-MenuItem $items 'Renombrar') 'renombrar sin login')
  $msLogin = Wait-Until { $null -ne (Find-Window $script:proc.Id 'Quasar Radio | *') } 8000
  $log = Find-Window $script:proc.Id 'Quasar Radio | *'
  L ('P9_la_linea_de_administrador_pide_identificarse_ms=' + $msLogin + ' | ventana=' + $(if ($null -ne $log) { $log.Title } else { '(ninguna)' }))
  if ($null -ne $log) {
    $logEl = $AE::FromHandle([IntPtr]$log.Hwnd)
    $volver = One $logEl 'VOLVER' $CT::Button
    if ($null -eq $volver) {
      L 'P9_no se encontró el boton VOLVER del dialogo de identificacion'
    } elseif (Raise-Front $log.Hwnd) {
      $rv = RectOf $volver
      [void](Click-Point ($rv.X + [int]($rv.W / 2)) ($rv.Y + [int]($rv.H / 2)) 'Volver de la identificacion')
      $msCierre = Wait-Until { $null -eq (Find-Window $script:proc.Id 'Quasar Radio | *') } 8000
      L ('P9_cancelada_la_identificacion_ms=' + $msCierre + ' | modal_cerrado=' + ($msCierre -ge 0))
    } else {
      L 'P9_el_dialogo de identificacion no quedo al frente; no se pulsa nada sobre otras ventanas'
    }
  }
  Start-Sleep -Milliseconds 800
  $con2 = $AE::FromHandle([IntPtr]$win.Hwnd)
  $tonoSigue = ($null -ne (One $con2 'QSRTono' $CT::Text))
  L ('P9_sin_identificacion_el_nombre_no_cambio=' + $tonoSigue + ' | pie=' + (Txt (ById $con2 'ConsoleStatusText')))

  $s1.Proc.Kill(); Start-Sleep -Seconds 3
  $script:proc = $null

  # ---- P8: persistencia. Lo ajustado en la sesion 1 tiene que estar en el disco y en pantalla al
  # relanzar, porque en cabina la botonera se monta una vez y se usa meses.
  $padsPath    = Join-Path $Sandbox 'cart_pads.json'
  $backupPath  = Join-Path $Sandbox 'cart_pads_backup.json'
  $tmpPath     = $padsPath + '.tmp'
  $json        = Read-PadsJson $padsPath
  $jp1 = Json-Pad $json 1; $jp2 = Json-Pad $json 2; $jp3 = Json-Pad $json 3; $jp4 = Json-Pad $json 4
  L ('P8_cart_pads.json: pads=' + @($json.pads).Count + ' | version=' + $json.version)
  L ('P8_pad1_volumen=' + $jp1.volumePercent + ' (esperado 30, el cambiado desde el menu) duck=' + $jp1.duckPercent + ' (esperado 50) modo=' + $jp1.mode + ' title=' + $jp1.title)
  L ('P8_pad2_duck=' + $jp2.duckPercent + ' (esperado 90, el cambiado con el pad en el aire) modo=' + $jp2.mode)
  L ('P8_pad3_modo=' + $jp3.mode + ' (esperado OneShot, del seed) | pad4_ruta_rota_conservada=' + (($null -ne $jp4) -and ($jp4.filePath -match 'Perdido')))
  L ('P8_escritura_atomica: backup=' + (Test-Path $backupPath) + ' sin_residuo_tmp=' + (-not (Test-Path $tmpPath)))
  L ('P8_el_json_no_perdio_la_ruta_de_los_clips=' + (($jp1.filePath -match 'QSRPAD') -and ($jp2.filePath -match 'QSRPAD') -and ($jp3.filePath -match 'QSRPAD')))

  $s2 = Start-Consola 'sesion 2 (persistencia)'
  $conB = $s2.ConEl
  $winB = $s2.Con
  $ayudB1 = HelpOf (Get-PadEl $conB 'F1')
  $ayudB2 = HelpOf (Get-PadEl $conB 'F2')
  $chipB  = Txt (ById $conB 'PadInventoryChip')
  L ('P8_relanzado_F1=[' + $ayudB1 + '] (esperado 30% · D50) | F2=[' + $ayudB2 + '] (esperado 100% · D90)')
  L ('P8_relanzado_inventario=' + $chipB + ' | chip_ok=' + ($chipB -match '^3 de 12 con clip$'))
  L ('P8_relanzado_nombres_del_json=' + (($null -ne (One $conB 'QSRTono' $CT::Text)) -and ($null -ne (One $conB 'QSRPerdido' $CT::Text))))
  $itemsB = Open-PadMenu $conB $winB 'F3' 'modo del json en el menu'
  # La cabecera del submenu de modo es fija ("Modo de disparo"): lo que cambia es la opcion marcada
  # dentro, asi que hay que desplegarlo para leer el modo que trajo el cart_pads.json.
  [void](Expand-MenuItem (Find-MenuItem $itemsB 'Modo de disparo') 'submenu de modo')
  $itemsB = Wait-SubmenuItems $itemsB.Count 6000
  $marcaModo = @($itemsB | Where-Object { $_.Current.Name.Contains($CHECK) }) | Select-Object -First 1
  L ('P8_relanzado_F3_sigue_en_disparar_y_dejar=' + ((Txt $marcaModo) -match 'disparar y dejar') + ' | marcada=[' + (Txt $marcaModo) + ']')
  Close-Menu

  # ================================ EVIDENCIA VISUAL (H36: al final) ================================
  Cap-PadStrip $winB $conB
  CapWindow $winB ($Tag + '_consola_final')

  $s2.Proc.Kill(); Start-Sleep -Seconds 2
  $script:proc = $null
}
catch {
  L ('ERROR: ' + $_.Exception.Message)
  foreach ($l in (($_.ScriptStackTrace -split "`n") | Select-Object -First 3)) { L ('   AT: ' + $l.Trim()) }
}
finally {
  $logf = Get-ChildItem $logdir -Filter 'quasar_radio-*.log' -ErrorAction SilentlyContinue |
          Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if ($null -ne $logf) {
    L ('LOG=' + $logf.Name)
    foreach ($m in @('Botonera leída', 'Botonera guardada', 'ducking', 'Accion sin permiso', 'pad ', 'Pad ')) {
      $hit = Select-String -Path $logf.FullName -SimpleMatch $m | Select-Object -Last 3
      foreach ($h in $hit) { L ('   ' + $h.Line.Trim()) }
    }
  } else { L 'LOG=no hay archivo de log' }

  if ($null -ne $script:proc -and -not $script:proc.HasExited) {
    $script:proc.Kill(); Start-Sleep -Seconds 2
  }
  foreach ($z in @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)) { $z.Kill() }
  Start-Sleep -Seconds 1
  L ('RESTANTES=' + (@(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue).Count))

  $padsDespues = Hash-Or $realPads '(no existe)'
  $metaDespues = Hash-Or $metaReal '(no existe)'
  $musicaDespues = @(Get-ChildItem (Join-Path $realBin 'musica') -File -ErrorAction SilentlyContinue).Count
  $efectosDespues = @(Get-ChildItem (Join-Path $realBin 'efectos') -File -ErrorAction SilentlyContinue).Count
  $qsrEnBin = @(Get-ChildItem $realBin -Recurse -Filter 'QSRPAD*' -ErrorAction SilentlyContinue).Count
  L ('BIN_track_metadata_antes=' + $metaAntes.Substring(0, [Math]::Min(12, $metaAntes.Length)) +
     ' despues=' + $metaDespues.Substring(0, [Math]::Min(12, $metaDespues.Length)) +
     ' | intacto=' + ($metaAntes -eq $metaDespues))
  L ('BIN_musica=' + $musicaAntes + ' -> ' + $musicaDespues + ' | efectos=' + $efectosAntes + ' -> ' + $efectosDespues +
     ' | sin_fixtures_QSRPAD_en_bin=' + ($qsrEnBin -eq 0))
  L ('DATOS_REALES_cart_pads_antes=' + $padsAntes.Substring(0, [Math]::Min(12, $padsAntes.Length)) +
     ' despues=' + $padsDespues.Substring(0, [Math]::Min(12, $padsDespues.Length)) +
     ' | intacto=' + ($padsAntes -eq $padsDespues) + ' (la prueba escribe en el sandbox por QUASAR_RADIO_DATA_DIR)')

  $layoutAfter = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
  if (Test-Path $layoutAfter) { Remove-Item $layoutAfter -Force }
  Remove-Item Env:QUASAR_RADIO_DATA_DIR -ErrorAction SilentlyContinue
  if (-not $Keep -and (Test-Path $Sandbox)) { Remove-Item $Sandbox -Recurse -Force }
  else { L ('SANDBOX_CONSERVADO=' + $Sandbox) }

  $res = Join-Path $out ('uia_' + $Tag + '_pads.txt')
  [IO.File]::WriteAllLines($res, $lines, (New-Object Text.UTF8Encoding($true)))
  Write-Host ('EVIDENCIA=' + $res)
}
