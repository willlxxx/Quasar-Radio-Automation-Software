param(
  [string]$Theme = 'Studio Dark Neon',
  [string]$Tag = 'auth1',
  [string]$Sandbox = (Join-Path $env:TEMP 'qsr_perm_test'),
  [switch]$Keep
)

# AUTH-1: verificacion EN VIVO del nuevo modelo de permisos.
#
# Regla que se comprueba, en el orden en que la vive un operador:
#   1. el programa arranca SIN pedir login                      (P1)
#   2. las ventanas de modulo se abren SIN clave               (P2 Biblioteca, P3 DSP)
#   3. la ventana de usuarios se abre SIN clave, y ahi se crean las cuentas
#      de nivel; la primera se puede crear estando fuera       (P4, P5)
#   4. crear una cuenta cuando ya existen SIEGA pidiendo identificacion,
#      y con la cuenta correcta se crea                         (P6)
#   5. con sesion de administrador abierta, una operacion de administrador se
#      hace SIN volver a preguntar                              (P7)
#   6. sin sesion, esa misma operacion pide identificacion y al VOLVER no toca nada
#                                                              (P8)
#   7. lo de nivel operador sigue funcionando siendo invitado   (P9)
#
# AISLAMIENTO: users.json se redirige a $Sandbox\data con QUASAR_RADIO_DATA_DIR (véase
# AuthService.DataDirectory). Sin eso esta prueba escribiri cuentas en el perfil real del
# fundador. La libreria tambien es de ensayo: el exe arranca con -WorkingDirectory $Sandbox
# y los fixtures llevan prefijo QSRPERM (jamas HRS ni MIN: con esos nombres se pisaron 31
# grabaciones reales; docs/UI_BASELINE.md).

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

if (-not ('QsrPerm' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class PermWin { public IntPtr Hwnd; public string Title; public bool Visible; public string Rect; public int L; public int T; public int W; public int H; }
public static class QsrPerm {
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
  // El unico sitio donde hace falta un raton de verdad: un PasswordBox de WPF no expone su
  // valor por UIA (a proposito, para que un lector de pantalla no saque la contrasena), y en
  // este cliente no hay FocusPattern accesible desde PowerShell. Poner el foco con un clic y
  // escribir por teclado es la ruta corta, y la clave no queda en ningun archivo.
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
  public static void Close(IntPtr h) { SendMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }
  static int _target; static List<PermWin> _found;
  public static List<PermWin> Top(int pid) {
    _target = pid; _found = new List<PermWin>();
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == _target) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        var wi = new PermWin();
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
function ById($root, $id) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
  return $root.FindFirst($Scope::Descendants, $c)
}
function Get-Estado($win) {
  $e = ById $win 'StatusText'
  if ($null -eq $e) { return '(sin texto de estado)' }
  return $e.Current.Name
}
# Un boton de WPF se pulsa por su InvokePattern: no depende del puntero ni del Z-order, que
# es como se evitan los clics perdidos mientras el fundador usa la maquina.
function Invoke-El($e, $que) {
  if ($null -eq $e) { L "  INVOCAR $que : NO ENCONTRADO"; return $false }
  try {
    $p = $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $p.Invoke()
    return $true
  } catch { L "  INVOCAR $que : error " + $_.Exception.Message; return $false }
}
function Set-Text($e, $valor) {
  $p = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
  $p.SetValue($valor)
}

# CLIC DE RATON, no InvokePattern, para todo lo que pueda abrir el dialogo de identificacion.
# Leccion ya aprendida en este proyecto: InvokePattern.Invoke() sobre un boton cuyo manejador
# abre un modal (ShowDialog) NO devuelve hasta que el modal se cierra, porque WPF ejecuta el
# clic en su hilo de UI y UIA espera. El intento de esquivarlo con Start-Job salio peor: el
# hilo del job seguia bloqueado igual, y mientras tanto el arbol cachedo del hilo principal ya
# no servia (P6/P7/P8 de la prueba "auth2" perdian el boton por ese camino). Un clic real
# devuelve enseguida y el script queda libre para atender el modal.
function Win-El($hwnd) { return $AE::FromHandle([IntPtr]$hwnd) }

# Traer la ventana al frente y COMPROBARLO. Un clic de raton va a las coordenadas que sea, sin
# saber que hay detras: si el Raise no logra poner la ventana delante (bloqueo de foreground de
# Windows), el clic cae sobre la ventana que si este delante. Por eso Raise-Front consulta
# GetForegroundWindow y Click-El se niega a pulsar si el frente no es el esperado.
function Raise-Front($hwnd) {
  for ($i = 1; $i -le 3; $i++) {
    [void][QsrPerm]::Raise([IntPtr]$hwnd)
    Start-Sleep -Milliseconds 300
    if ([QsrPerm]::GetForegroundWindow() -eq [IntPtr]$hwnd) { return $true }
  }
  L ('  FRENTE : la ventana ' + $hwnd + ' no quedo al frente (frente=' + [QsrPerm]::GetForegroundWindow() + ')')
  return $false
}

function Click-El($e, $que, $win) {
  if ($null -eq $e) { L "  CLIC $que : NO ENCONTRADO"; return $false }
  if (-not (Raise-Front $script:frontHwnd)) { L "  CLIC $que : abortado, la ventana no esta al frente"; return $false }
  $r = RectOf $e
  if ($r.W -lt 4 -or $r.H -lt 4) { L ("  CLIC $que : rect invalida " + $r.X + ',' + $r.Y + ' ' + $r.W + 'x' + $r.H); return $false }
  $cx = $r.X + [int]($r.W / 2)
  $cy = $r.Y + [int]($r.H / 2)
  # Un clic solo cae sobre el boton si el boton esta dentro de lo que se ve. Si la ventana esta
  # mas pequena que su contenido, el punto existe pero pertenece a otro sitio: mejor decirlo.
  if ($null -ne $win) {
    if ($cx -lt $win.L -or $cx -gt ($win.L + $win.W) -or $cy -lt $win.T -or $cy -gt ($win.T + $win.H)) {
      L ("  CLIC $que : fuera de la ventana (" + $cx + ',' + $cy + ' vs ' + $win.Rect + ')')
      return $false
    }
  }
  [QsrPerm]::Click($cx, $cy)
  return $true
}

# Cuando un boton "no existe" casi nunca es que no esté: es el arbol parcial de H11 o un Nombre
# que no coincide. Volcar los nombres de los botones presentes es lo unico que permite
# distinguirlo sin adivinar.
function Dump-Botones($win, $donde) {
  $names = @()
  try {
    foreach ($e in $win.FindAll($Scope::Descendants, (CTCond $CT::Button))) { $names += $e.Current.Name }
  } catch { }
  L ('  BOTONES ' + $donde + ': ' + ($names -join ' · '))
}

# El modal puede tardar: el hilo de UI esta ocupado. Se espera en vez de asumir.
function Wait-Login($pid_, [int]$segundos = 12) {
  $fin = (Get-Date).AddSeconds($segundos)
  while ((Get-Date) -lt $fin) {
    $m = Get-Login $pid_
    if ($null -ne $m) { Start-Sleep -Milliseconds 600; return $m }
    Start-Sleep -Milliseconds 300
  }
  return $null
}
# PasswordBox de WPF no expone ValuePattern (es asi a proposito: la contrasena no se lee por
# UIA). Lo que si funciona es ponerle el foco y escribir de teclado.
function Set-Password($root, $automationId, $valor) {
  $pb = ById $root $automationId
  if ($null -eq $pb) { return $false }
  if (-not (Raise-Front $script:frontHwnd)) { return $false }
  Start-Sleep -Milliseconds 200
  $r = RectOf $pb
  [QsrPerm]::Click(($r.X + [int]($r.W / 2)), ($r.Y + [int]($r.H / 2)))
  Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait($valor)
  Start-Sleep -Milliseconds 200
  return $true
}
function RectOf($e) {
  $r = $e.Current.BoundingRectangle
  return [pscustomobject]@{ X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
}

# Maximizar por WindowPattern funciona en las ventanas de modulo y evita que un boton del pie
# quede fuera del area visible (un clic de raton no alcanza lo que no se pinta).
function Maximize($hwnd) {
  try {
    $el = $AE::FromHandle([IntPtr]$hwnd)
    $p = $el.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $p.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
    Start-Sleep -Milliseconds 600
    return $true
  } catch { L ('  MAXIMIZAR : ' + $_.Exception.Message); return $false }
}

# md5 corto, y sin reventar cuando todavia no hay archivo.
function Short-Hash($path) {
  if (-not (Test-Path $path)) { return 'no-hay' }
  $h = (Get-FileHash $path -Algorithm MD5).Hash
  return $h.Substring(0, 8)
}

$exe = 'C:\Users\CY-KILL\Desktop\Programación y Datanálisis\quasar_radio\bin\Debug\net10.0-windows\quasar_radio.exe'
$out = Join-Path $env:TEMP 'qsr_ui'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$dataDir = Join-Path $Sandbox 'data'
$json    = Join-Path $Sandbox 'track_metadata.json'
$users   = Join-Path $dataDir 'users.json'

$lines = New-Object Collections.ArrayList
function L($m) { $lines.Add($m) | Out-Null; Write-Host $m }

function CapWindow($win, $t) {
  if ($null -eq $win) { L "CAP $t : sin ventana"; return }
  $w = [int]$win.W; $h = [int]$win.H
  if ($w -lt 50 -or $h -lt 50) { L "CAP $t : rect invalida"; return }
  $bmp = New-Object Drawing.Bitmap($w, $h)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrPerm]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 350
  $hdc = $g.GetHdc()
  $ok = [QsrPerm]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $path = Join-Path $out "$t.png"
  $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  L ("CAP $t : PrintWindow=$ok ${w}x${h} md5=" + (Get-FileHash $path -Algorithm MD5).Hash)
}

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
    $s = [int]([Math]::Sin(2 * [Math]::PI * $Hz * ($i / $rate)) * 11000)
    $bw.Write([int16]$s)
  }
  $bw.Flush(); [IO.File]::WriteAllBytes($Path, $ms.ToArray())
  $bw.Close(); $ms.Close()
}

function New-Sandbox {
  if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'musica') | Out-Null
  New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
  for ($i = 1; $i -le 3; $i++) {
    New-QsrWav (Join-Path $Sandbox ('musica\QSRPERM Artista ' + $i + ' - Pista ' + $i + '.wav')) 2.0 (220 + $i * 50)
  }
  L ('FIXTURES: 3 wav QSRPERM* en musica/ y datos aislados en ' + $dataDir)
}

# Ventana modal de identificacion: es hija del escritorio con Owner puesto, y por el root de
# UIA no asoma (H10), asi que se busca por EnumWindows y su titulo.
function Get-Login($pid_) {
  return [QsrPerm]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like 'Quasar Radio | *' } | Select-Object -First 1
}

$pre = @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)
if ($pre.Count -gt 0) { throw "Ya hay $($pre.Count) instancia(s) abiertas; abortando para no tocar la emision." }

New-Sandbox

# La geometria y el tema de esta prueba no heredan ni dejan estado de una sesion anterior.
$layout = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
if (Test-Path $layout) { Remove-Item $layout -Force }
Set-Content -Path (Join-Path $env:APPDATA 'QuasarRadio\appearance.json') -Value ('{"theme":"' + $Theme + '"}') -Encoding UTF8

# users.json hacia el sandbox. Es lo que permite crear cuentas de verdad sin tocar las del
# fundador: AuthService lee QUASAR_RADIO_DATA_DIR antes de mirar en LocalAppData.
$env:QUASAR_RADIO_DATA_DIR = $dataDir

$proc = $null
try {
  # ---------- P1: arranque normal, SIN login previo y SIN --ui-check
  $proc = Start-Process -FilePath $exe -WorkingDirectory $Sandbox -PassThru
  $pid_ = $proc.Id
  Start-Sleep -Seconds 12

  $con = [QsrPerm]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like 'Quasar Radio Pro*' } | Select-Object -First 1
  if ($null -eq $con) { throw 'sin consola al arrancar' }
  $script:frontHwnd = $con.Hwnd
  $conEl = $AE::FromHandle([IntPtr]$con.Hwnd)
  L ('P1_consola=' + $con.Rect)

  # Centinela profundo: el arbol de UIA sale parcial mientras el escaneo de la libreria tiene
  # ocupado el hilo de UI (H11), y un boton que existe parece no existir.
  $esp = [Diagnostics.Stopwatch]::StartNew()
  while ($esp.ElapsedMilliseconds -lt 60000) {
    if ($null -ne (One $conEl 'abre su propia ventana' $CT::Text)) { break }
    Start-Sleep -Milliseconds 500
  }
  L ('P1_arbol_listo_en_ms=' + [int]$esp.ElapsedMilliseconds)

  $pie = One $conEl 'Invitado' $CT::Text
  L ('P1_pie_invitado=' + ($null -ne $pie) + ' | ' + (Get-Estado $conEl))
  $btnPie = ByName $conEl 'IDENTIFICARSE|USUARIOS|CERRAR SESI' $CT::Button
  L ('P1_botones_pie=' + (($btnPie | ForEach-Object { $_.Current.Name }) -join ' · '))
  L ('P1_sin_modal_al_arrancar=' + ((Get-Login $pid_) -eq $null))

  # ---------- P2: Biblioteca se abre SIN clave (antes ModuleNavigator la tenia gated)
  Invoke-El (One $conEl 'Abrir Biblioteca' $CT::Button) 'Abrir Biblioteca' | Out-Null
  Start-Sleep -Seconds 3
  $login = Get-Login $pid_
  $bib = [QsrPerm]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like '*Biblioteca*' } | Select-Object -First 1
  L ('P2_biblioteca_abre_sin_clave=' + ($null -ne $bib) + ' | modal_de_login=' + ($null -ne $login) + ' | ' + $bib.Rect)
  if ($null -eq $bib) { throw 'Biblioteca sigue detras de una clave' }
  $bibEl = $AE::FromHandle([IntPtr]$bib.Hwnd)
  $esp = [Diagnostics.Stopwatch]::StartNew()
  while ($esp.ElapsedMilliseconds -lt 60000) { if ($null -ne (ById $bibEl 'StatusText')) { break }; Start-Sleep -Milliseconds 500 }
  L ('P2_biblioteca_lista_en_ms=' + [int]$esp.ElapsedMilliseconds)
  # Se maximiza una vez y para siempre en esta prueba: la Biblioteca tiene dos hileras de chips
  # en el pie, y un clic de raton (P8) necesita que el boton este dentro de lo visible.
  L ('P2_biblioteca_maximizada=' + (Maximize $bib.Hwnd))

  # ---------- P3: DSP, que era el otro gated de la franja
  Invoke-El (One $conEl 'Abrir DSP' $CT::Button) 'Abrir DSP' | Out-Null
  Start-Sleep -Seconds 3
  $dsp = [QsrPerm]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like '*DSP*' } | Select-Object -First 1
  L ('P3_dsp_abre_sin_clave=' + ($null -ne $dsp) + ' | modal=' + ((Get-Login $pid_) -ne $null) + ' | ' + $dsp.Rect)

  # ---------- P4: la ventana de USUARIOS se abre sin clave (ahi se crean los niveles)
  Invoke-El (One $conEl 'Abrir la ventana de usuarios' $CT::Button) 'USUARIOS' | Out-Null
  Start-Sleep -Seconds 2
  $usu = [QsrPerm]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like '*Usuarios y niveles*' } | Select-Object -First 1
  if ($null -eq $usu) { throw 'la ventana de usuarios no abrio sin clave' }
  $usuEl = $AE::FromHandle([IntPtr]$usu.Hwnd)
  $script:frontHwnd = $usu.Hwnd
  $ses = ById $usuEl 'SessionText'
  L ('P4_usuarios_sin_clave=True | ' + $usu.Rect + ' | sesion: ' + $ses.Current.Name)

  # ---------- P5: primera cuenta, creada Estando fuera (bootstrap)
  Set-Text (ById $usuEl 'UsernameBox') 'jefa'
  [void](Set-Password $usuEl 'PasswordBox' 'QuasarPrueba2026')
  $role = ById $usuEl 'RoleBox'
  L ('P5_rol_por_defecto=' + $role.Current.Name)
  Invoke-El (One $usuEl 'Crear la cuenta' $CT::Button) 'CREAR CUENTA' | Out-Null
  Start-Sleep -Seconds 2
  $grid = One $usuEl 'Cuentas creadas' $CT::DataGrid
  $rows = @($grid.FindAll($Scope::Children, (CTCond $CT::DataItem)))
  if ($rows.Count -eq 0) { $rows = @($grid.FindAll($Scope::Descendants, (CTCond $CT::DataItem))) }
  L ('P5_primera_cuenta_sin_login=' + ((Get-Estado $usuEl) -match 'Cuenta creada') + ' | ' + (Get-Estado $usuEl))
  L ('P5_filas_en_la_tabla=' + $rows.Count + ' | users.json en el sandbox=' + (Test-Path $users))
  CapWindow $usu "P5_${Tag}_usuarios"

  # ---------- P6: la segunda cuenta SI exige identificacion, y con credenciales sale
  # Cada paso vuelve a resolver la ventana desde su HWND: el arbol cachedo de $usuEl ya no vale
  # despues de que otras ventanas se hayan traido al frente (fallo real de la prueba "auth2").
  $usu = [QsrPerm]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like '*Usuarios y niveles*' } | Select-Object -First 1
  $usuEl = Win-El $usu.Hwnd
  $script:frontHwnd = $usu.Hwnd
  Set-Text (ById $usuEl 'UsernameBox') 'operador1'
  [void](Set-Password $usuEl 'PasswordBox' 'QuasarPrueba2026')
  $crear = One $usuEl 'Crear la cuenta' $CT::Button
  if ($null -eq $crear) { Dump-Botones $usuEl 'P6 usuarios' }
  L ('P6_clic_en_crear=' + (Click-El $crear 'CREAR CUENTA 2' $usu))
  $login = Wait-Login $pid_
  L ('P6_segunda_cuenta_pide_identificacion=' + ($null -ne $login) + ' | ' + $(if ($login) { $login.Title } else { '-' }))
  if ($null -ne $login) {
    CapWindow $login "P6_${Tag}_pide_clave"
    $lgEl = Win-El $login.Hwnd
    $script:frontHwnd = $login.Hwnd
    Set-Text (ById $lgEl 'UsernameBox') 'jefa'
    [void](Set-Password $lgEl 'PasswordBox' 'QuasarPrueba2026')
    Click-El (One $lgEl 'Identificarse y continuar' $CT::Button) 'CONTINUAR' $login | Out-Null
    Start-Sleep -Seconds 3
    $login2 = Get-Login $pid_
    $usuEl = Win-El $usu.Hwnd
    $grid = One $usuEl 'Cuentas creadas' $CT::DataGrid
    $rows = @($grid.FindAll($Scope::Descendants, (CTCond $CT::DataItem)))
    L ('P6_tras_identificarse_modal_cerrado=' + ($null -eq $login2) + ' | filas=' + $rows.Count + ' | ' + (Get-Estado $usuEl))
  }

  # ---------- P7: con sesion de administrador, GUARDAR se hace sin volver a preguntar
  # Con sesion abierta no se espera modal, asi que InvokePattern es la via limpia: no depende
  # del Z-order ni del puntero.
  $bib = [QsrPerm]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like '*Biblioteca*' } | Select-Object -First 1
  $bibEl = Win-El $bib.Hwnd
  $script:frontHwnd = $bib.Hwnd
  $guardar = One $bibEl 'GUARDAR' $CT::Button
  if ($null -eq $guardar) { Dump-Botones $bibEl 'P7 biblioteca' }
  L ('P7_guardar_habilitado=' + $(if ($null -ne $guardar) { $guardar.Current.IsEnabled } else { 'no-hay boton' }))
  Invoke-El $guardar 'GUARDAR con sesion' | Out-Null
  Start-Sleep -Seconds 3
  $e7 = Get-Estado $bibEl
  L ('P7_sin_modal_y_guardado=' + ((Get-Login $pid_) -eq $null) + ' | ' + $e7)
  L ('P7_operacion_de_administrador_hecha=' + ($e7 -match 'guardado') + ' | json=' + (Test-Path $json) + ' | md5=' + (Short-Hash $json))

  # ---------- P8: cerrar sesion y volver a pulsar GUARDAR -> pide identificacion y VOLVER no escribe
  $antes = Short-Hash $json
  $con = [QsrPerm]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like 'Quasar Radio Pro*' } | Select-Object -First 1
  $conEl = Win-El $con.Hwnd
  $cerrar = One $conEl 'Cerrar la sesion actual' $CT::Button
  if ($null -eq $cerrar) { Dump-Botones $conEl 'P8 consola' }
  Invoke-El $cerrar 'CERRAR SESION' | Out-Null
  Start-Sleep -Seconds 2
  $conEl = Win-El $con.Hwnd
  $pie2 = One $conEl 'Invitado' $CT::Text
  $sesion2 = ById $conEl 'CurrentUserText'
  L ('P8_tras_cerrar_sesion_pie=' + ($null -ne $pie2) + ' | pie: ' + $(if ($sesion2) { $sesion2.Current.Name } else { '-' }))

  # Aqui SI hace falta raton: GUARDAR sin sesion abre el dialogo de identificacion, y
  # InvokePattern no devuelve hasta que ese dialogo se cierra.
  $bib = [QsrPerm]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like '*Biblioteca*' } | Select-Object -First 1
  $bibEl = Win-El $bib.Hwnd
  $script:frontHwnd = $bib.Hwnd
  $guardar = One $bibEl 'GUARDAR' $CT::Button
  # IsEnabled (no Enabled, que en este cliente UIA sale vacio). Siendo invitado el boton tiene
  # que estar ENCENDIDO: la clave dejo de ser una puerta de acceso y es un sello en el clic.
  L ('P8_guardar_habilitado_siendo_invitado=' + $(if ($null -ne $guardar) { $guardar.Current.IsEnabled } else { 'no-hay boton' }))
  L ('P8_clic_en_guardar=' + (Click-El $guardar 'GUARDAR sin sesion' $bib))
  $login = Wait-Login $pid_
  L ('P8_operacion_de_administrador_pide_clave=' + ($null -ne $login))
  if ($null -ne $login) {
    CapWindow $login "P8_${Tag}_pide_clave"
    $lgEl = Win-El $login.Hwnd
    $script:frontHwnd = $login.Hwnd
    Click-El (One $lgEl 'Volver sin realizar' $CT::Button) 'VOLVER' $login | Out-Null
    Start-Sleep -Seconds 2
    $despues = Short-Hash $json
    L ('P8_al_volver_no_se_escribe=' + ($antes -eq $despues) + ' | md5 antes=' + $antes + ' despues=' + $despues)
    L ('P8_modal_cerrado=' + ((Get-Login $pid_) -eq $null))
  }

  # ---------- P9: lo de operador sigue siendo de operador (invitado puede)
  $bib = [QsrPerm]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like '*Biblioteca*' } | Select-Object -First 1
  $bibEl = Win-El $bib.Hwnd
  $auto = One $bibEl 'AUTO PLAYLIST' $CT::Button
  if ($null -eq $auto) { Dump-Botones $bibEl 'P9 biblioteca' }
  L ('P9_auto_habilitado_siendo_invitado=' + $(if ($null -ne $auto) { $auto.Current.IsEnabled } else { 'no-hay boton' }))
  Invoke-El $auto 'AUTO PLAYLIST' | Out-Null
  Start-Sleep -Seconds 3
  $e9 = Get-Estado $bibEl
  L ('P9_auto_funciona_siendo_invitado=' + (($e9 -match 'autom') -and ((Get-Login $pid_) -eq $null)) + ' | ' + $e9)

  CapWindow $con "P9_${Tag}_consola"
  CapWindow $bib "P9_${Tag}_biblioteca"

  # ---------- log: la prueba deja rastro de quien hizo que
  $lg = (Get-ChildItem (Join-Path (Split-Path $exe) 'logs') -Filter 'quasar_radio-*.log' -ErrorAction SilentlyContinue |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1)
  if ($null -ne $lg) {
    L ('LOG=' + (Split-Path $lg -Leaf))
    Get-Content $lg.FullName -Tail 200 |
      Select-String -Pattern 'Base de usuarios|autenticado|Accion atribuida|Accion sin permiso|Cuenta creada|Sesion cerrada|volvio sin identificarse' |
      Select-Object -Last 14 | ForEach-Object { L ('   ' + $_.Line) }
  }
}
finally {
  if ($null -ne $proc) {
    try { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null } catch { }
  }
  Start-Sleep -Seconds 1
  $rest = @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)
  L ('RESTANTES=' + $rest.Count)
  if (Test-Path $layout) { Remove-Item $layout -Force }
  if ($Keep) { L ('SANDBOX CONSERVADO=' + $Sandbox) }
  else { try { Remove-Item $Sandbox -Recurse -Force } catch { L 'no se pudo borrar el sandbox' } }
  $evid = Join-Path $out "uia_${Tag}_permisos.txt"
  $lines | Set-Content -Path $evid -Encoding UTF8
  L ("EVIDENCIA=$evid")
}
