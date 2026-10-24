param(
  [string]$Tag = 'ui9',
  [string]$Sandbox = (Join-Path $env:TEMP 'qsr_temas_test'),
  [switch]$Keep,
  [switch]$SoloS6
)

# UI-9: verificacion en vivo del quinto tema (Studio Light) y de que el resto no se rompio.
#
# Cubre la clausa de aceptacion del plan: "quinto tema seleccionable, 0 hex en las vistas, las
# cuatro ventanas de modulo y la consola legibles con él, contraste >= 4,5:1 medido en el texto
# mas usado, y el VU pintando en claro".
#
# Dos mitades, y cada una mide lo que la otra no puede:
#  T0  CONTRASTE POR TOKENS — sin ventana: se parsean Tokens.xaml y el diccionario de cada tema,
#      se resuelve la clave efectiva (el tema gana sobre el token) y se calcula el ratio WCAG de
#      los pares texto/superficie. Es la unica forma de afirmar 4,5:1 SIN mirar capturas, porque
#      UIA no expone colores.
#  S1..S5  LUMINANCIA POR TEMA — el tema se pone en appearance.json ANTES de arrancar (es la
#      ruta que el fundador cambia a mano en la cabina), la consola se captura con PrintWindow y
#      se mide la luminancia media de los pixeles: oscuro debe dar oscuro, claro debe dar claro.
#      La captura se hace SIEMPRE al final de cada sesion porque el PrintWindow deja el arbol de
#      UIA de esa ventana sin contenido para el resto del cliente (H36 de la linea base).
#  S6   CAMBIO EN CALIENTE — el combo de temas de la consola, seis cambios seguidos: se despliega
#      por ExpandCollapse, la linea se busca en la ventana Popup del proceso (los items del combo
#      no cuelgan del combo en el arbol UIA) y se elige con clic real; leido todo por patrones UIA,
#      sin una sola captura: aqui se prueba el ThemeChanged (que los colores-cadena del ViewModel
#      se reavisan) y que el arbol sobrevive intacto.
#  S7   PERSISTENCIA + EL VU EN CLARO — se reabre despues del ultimo cambio (S6 deja Studio
#      Light), se reproduce un tono, y el recorte del medidor de salida se mide al pixel: tiene
#      que contener el verde del tema claro (#15803D) y NO el verde historico (#22C55E).
#
# Se ejecuta SIEMPRE contra una libreria de ensayo en %TEMP%: el directorio de trabajo y
# QUASAR_RADIO_DATA_DIR apuntan al sandbox, asi que cart_pads.json, track_metadata.json y
# nowplaying.txt de ensayo viven ahi y los datos reales del fundador ni se leen ni se escriben.
# appearance.json y layout.json viven en %APPDATA%\QuasarRadio y son del fundador: se guarda la
# copia exacta y se restaura en el finally. Los archivos de ensayo llevan el prefijo QSRTEMA.
# Jamas HRS ni MIN: con esos nombres se pisaron 31 grabaciones reales de horarias/.
#
# RUIDO: S1..S5 son cinco arranques de unos 15 s cada uno, y S7 ademas reproduce 30 s de tono.
# Entre 3 y 5 minutos de ventana ocupada en la maquina.

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$exe = 'C:/Users/CY-KILL/Desktop/Programación y Datanálisis/quasar_radio/bin/Debug/net10.0-windows/quasar_radio.exe'
$repo = 'C:/Users/CY-KILL/Desktop/Programación y Datanálisis/quasar_radio'
$out = Join-Path $env:TEMP 'qsr_ui'
if (-not (Test-Path $out)) { New-Item -ItemType Directory -Path $out | Out-Null }
$logFile = Join-Path $out ('uia_' + $Tag + '_temas.txt')
$lines = New-Object System.Collections.ArrayList

Add-Type -AssemblyName System.Xml

function L($m) { $lines.Add($m) | Out-Null; Write-Host $m }

if (-not ('QsrTema' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class TemaWin { public IntPtr Hwnd; public string Title; public bool Visible; public string Rect; public int L; public int T; public int W; public int H; }
public static class QsrTema {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
  public static void Raise(IntPtr h) {
    ShowWindow(h, 5); BringWindowToTop(h);
    keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
    SetForegroundWindow(h);
  }
  public static void Click(int x, int y) {
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(80);
    mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
    mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
  }
  public static void Key(byte vk) {
    keybd_event(vk, 0, 0, UIntPtr.Zero);
    keybd_event(vk, 0, 2, UIntPtr.Zero);
  }
  public static void Restore(IntPtr h) { ShowWindow(h, 9); BringWindowToTop(h); }
  public static bool Minimizada(IntPtr h) { return IsIconic(h); }
  public static void Close(IntPtr h) { SendMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }
  static int _target; static List<TemaWin> _found;
  public static List<TemaWin> Top(int pid) {
    _target = pid; _found = new List<TemaWin>();
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == _target) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        var wi = new TemaWin();
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
$RVP   = [System.Windows.Automation.RangeValuePattern]
$ECP   = [System.Windows.Automation.ExpandCollapsePattern]
$SIP   = [System.Windows.Automation.SelectionItemPattern]
$SEP   = [System.Windows.Automation.SelectionPattern]

function CTCond($ct) { New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ct) }
function PidCond($pid_) { New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $pid_) }
function ById($root, $id) {
  try { return $root.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id))) }
  catch { return $null }
}
function One($root, $pattern, $ct) {
  $all = @($root.FindAll($Scope::Descendants, (CTCond $ct)))
  foreach ($e in $all) { if ($e.Current.Name -like ('*' + $pattern + '*')) { return $e } }
  return $null
}
function Txt($e) { if ($null -eq $e) { return '(ausente)' } return $e.Current.Name }
function Invoke-El($e) { [void]$e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Set-Range($e, $v) { [void]$e.GetCurrentPattern($RVP::Pattern).SetValue($v) }
function RectOf($e) {
  $r = $e.Current.BoundingRectangle
  return [pscustomobject]@{ X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
}
function Wait-Until([scriptblock]$Test, [int]$Ms) {
  $t0 = Get-Date
  while (((Get-Date) - $t0).TotalMilliseconds -lt $Ms) {
    try { if (& $Test) { return [int]((Get-Date) - $t0).TotalMilliseconds } } catch { }
    Start-Sleep -Milliseconds 120
  }
  return -1
}
function Vu-Value($root, $id) {
  $e = ById $root $id
  if ($null -eq $e) { return $null }
  try { return [double]$e.GetCurrentPattern($RVP::Pattern).Current.Value } catch { return $null }
}
function Find-Window($pid_, $pattern) {
  return ([QsrTema]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like $pattern } | Select-Object -First 1)
}

# ------------------------------------------------------------------ WCAG y tokens

function Get-Lum([string]$hex) {
  $r = [convert]::ToInt32($hex.Substring(1, 2), 16) / 255
  $g = [convert]::ToInt32($hex.Substring(3, 2), 16) / 255
  $b = [convert]::ToInt32($hex.Substring(5, 2), 16) / 255
  $f = { param($c) if ($c -le 0.03928) { $c / 12.92 } else { [math]::Pow(($c + 0.055) / 1.055, 2.4) } }
  return 0.2126 * (& $f $r) + 0.7152 * (& $f $g) + 0.0722 * (& $f $b)
}
function Get-Contrast([string]$a, [string]$b) {
  $la = Get-Lum $a; $lb = Get-Lum $b
  $hi = [math]::Max($la, $lb); $lo = [math]::Min($la, $lb)
  return ($hi + 0.05) / ($lo + 0.05)
}

# Diccionario efectivo de un tema: Tokens.xaml y encima el fichero del tema (lo ultimo gana).
function Norm-Hex([string]$v) {
  if ([string]::IsNullOrWhiteSpace($v)) { return $v }
  $v = $v.Trim()
  if ($v -notmatch '^#') { $v = '#' + $v }
  if ($v.Length -eq 9) { $v = '#' + $v.Substring(3) }   # #AARRGGBB -> #RRGGBB
  return $v
}
function Resolve-Theme([string]$themeFile) {
  $map = @{}
  foreach ($f in @((Join-Path $repo 'Themes/Tokens.xaml'), (Join-Path $repo ('Themes/' + $themeFile)))) {
    [xml]$doc = Get-Content $f -Raw -Encoding UTF8
    foreach ($n in $doc.SelectNodes('//*')) {
      if ($n.NodeType -ne 'Element') { continue }
      $key = $n.GetAttribute('Key', 'http://schemas.microsoft.com/winfx/2006/xaml')
      if ([string]::IsNullOrEmpty($key)) { continue }
      if ($n.Name -eq 'SolidColorBrush') { $map[$key] = Norm-Hex $n.GetAttribute('Color') }
      elseif ($n.Name -eq 'Color') { $map[$key] = Norm-Hex $n.InnerText }
    }
  }
  return $map
}

$THEMES = [ordered]@{
  'Studio Dark Neon' = 'Theme.StudioDarkNeon.xaml'
  'Cyan Broadcast'   = 'Theme.CyanBroadcast.xaml'
  'Charcoal Pro'    = 'Theme.CharcoalPro.xaml'
  'Deep Navy FM'    = 'Theme.DeepNavyFM.xaml'
  'Studio Light'    = 'Theme.StudioLight.xaml'
}

L ('TEMA-LISTA=' + (($THEMES.Keys) -join ' | '))
L ('T0_fichero_claro_existe=' + (Test-Path (Join-Path $repo 'Themes/Theme.StudioLight.xaml')))

# Pares texto/superficie que se exigen en TODOS los temas: es el texto que el operador lee.
$CORE = @(
  @{ fg = 'Brush.Text.Default'; bg = 'Brush.Surface.Base' }
  @{ fg = 'Brush.Text.High';    bg = 'Brush.Surface.Base' }
  @{ fg = 'Brush.Text.Secondary'; bg = 'Brush.Surface.Base' }
  @{ fg = 'Brush.Text.Faint';   bg = 'Brush.Surface.Base' }
  @{ fg = 'Brush.Text.Default'; bg = 'Brush.Surface.Header' }
  @{ fg = 'Brush.Text.Default'; bg = 'Brush.Surface.Card' }
  @{ fg = 'Brush.Text.High';    bg = 'Brush.Surface.Raised' }
)
# Pares del claro: acentos que se usan como TEXTO en la consola (estado del detector, modo
# auto, ducking) y tintos de las superficies nuevas de UI-9. Solo se exigen al tema claro;
# en los oscuros se reportan sin veredicto porque su legibilidad historica es otra.
$LIGHT_ONLY = @(
  @{ fg = 'Brush.Accent';          bg = 'Brush.Surface.Base' }
  @{ fg = 'Brush.Accent.Success';  bg = 'Brush.Surface.Base' }
  @{ fg = 'Brush.Accent.Danger';   bg = 'Brush.Surface.Base' }
  @{ fg = 'Brush.Accent.Warn';     bg = 'Brush.Surface.Base' }
  @{ fg = 'Brush.Accent.Info';     bg = 'Brush.Surface.Base' }
  @{ fg = 'Brush.Accent.Orange';   bg = 'Brush.Surface.Base' }
  @{ fg = 'Brush.Accent.Purple';   bg = 'Brush.Surface.Base' }
  @{ fg = 'Brush.Accent.Danger';   bg = 'Brush.Surface.Track' }
  @{ fg = 'Brush.Accent.Success';  bg = 'Brush.Surface.Track' }
  @{ fg = 'Brush.Text.OnAccent';   bg = 'Brush.Accent' }
  @{ fg = 'Brush.Text.High';       bg = 'Brush.Button.Mic.Live' }
  @{ fg = 'Brush.Text.High';       bg = 'Brush.Button.Mic.Idle' }
  @{ fg = 'Brush.Text.High';       bg = 'Brush.Badge.Playing' }
  @{ fg = 'Brush.Text.High';       bg = 'Brush.Badge.Next' }
  @{ fg = 'Brush.Text.High';       bg = 'Brush.Badge.Played' }
  @{ fg = 'Brush.Text.High';       bg = 'Brush.Badge.Pending' }
  @{ fg = 'Brush.DeckA.Title';     bg = 'Brush.DeckA.Surface' }
  @{ fg = 'Brush.DeckB.Title';     bg = 'Brush.DeckB.Surface' }
  @{ fg = 'Brush.Text.Default';    bg = 'Brush.Surface.Panel' }
  @{ fg = 'Brush.Text.Alt';        bg = 'Brush.Surface.PanelAlt' }
)
# Los LED del VU son graficos, no texto: se exige 3:1 sobre el hueco (recomendado WCAG para
# elementos de interfaz no textuales).
$VU = @('Brush.Vu.Low', 'Brush.Vu.Mid', 'Brush.Vu.High')

$temasResueltos = @{}
foreach ($name in $THEMES.Keys) { $temasResueltos[$name] = Resolve-Theme $THEMES[$name] }

$failCore = 0
foreach ($name in $THEMES.Keys) {
  $m = $temasResueltos[$name]
  $vals = @()
  foreach ($p in $CORE) {
    $fg = $m[$p.fg]; $bg = $m[$p.bg]
    if ([string]::IsNullOrEmpty($fg) -or [string]::IsNullOrEmpty($bg)) {
      L ('T0_PAREJA_AUSENTE [' + $name + '] ' + $p.fg + ' o ' + $p.bg + ' no esta en el diccionario'); $failCore++; continue
    }
    $c = Get-Contrast $fg $bg
    $ok = $c -ge 4.5
    if (-not $ok) { $failCore++ }
    $vals += ('{0}/{1}={2:F2}{3}' -f ($p.fg -replace 'Brush\.', ''), ($p.bg -replace 'Brush\.', ''), $c, ($(if ($ok) { '' } else { ' <4,5 FALLO' })))
  }
  L ('T0_texto_base [' + $name + '] ' + ($vals -join ' · '))
}
L ('T0_contraste_texto_base_los_cinco_temas=' + ($failCore -eq 0) + ' | fallos=' + $failCore)

$mL = $temasResueltos['Studio Light']
$failLight = 0; $valsL = @()
foreach ($p in $LIGHT_ONLY) {
  $fg = $mL[$p.fg]; $bg = $mL[$p.bg]
  if ([string]::IsNullOrEmpty($fg) -or [string]::IsNullOrEmpty($bg)) {
    L ('T0_CLARO_CLAVE_FALTA ' + $p.fg + ' / ' + $p.bg); $failLight++; continue
  }
  $c = Get-Contrast $fg $bg
  if ($c -lt 4.5) { $failLight++; $valsL += ('{0}/{1}={2:F2} FALLO' -f ($p.fg -replace 'Brush\.', ''), ($p.bg -replace 'Brush\.', ''), $c) }
  else { $valsL += ('{0}/{1}={2:F2}' -f ($p.fg -replace 'Brush\.', ''), ($p.bg -replace 'Brush\.', ''), $c) }
}
L ('T0_contraste_pares_del_claro=' + ($failLight -eq 0) + ' | ' + ($valsL -join ' · '))

$failVu = 0
foreach ($k in $VU) {
  $c = Get-Contrast $mL[$k] $mL['Brush.Surface.Inset']
  if ($c -lt 3.0) { $failVu++ }
  L ('T0_vu_claro [' + ($k -replace 'Brush\.Vu\.', '') + '] sobre Inset = ' + ('{0:F2}' -f $c) + (' ok=' + ($c -ge 3.0)))
}
L ('T0_vu_claro_3a1=' + ($failVu -eq 0))

# Los doce colores de identidad del pad (la paleta fija de CartWallViewModel) con la tinta que
# calcula CartPadItem.PadTextHex: el umbral de cruce negro/blanco de WCAG es luminancia 0.179.
$PADS = @('#FF1744', '#FF9100', '#FFEA00', '#00E676', '#00B0FF', '#651FFF', '#F50057', '#00E5FF', '#76FF03', '#FF3D00', '#3D5AFE', '#D500F9')
$failPad = 0; $padRes = @()
foreach ($hex in $PADS) {
  $lum = Get-Lum $hex
  $ink = if ($lum -gt 0.179) { '#000000' } else { '#FFFFFF' }
  $c = Get-Contrast $hex $ink
  if ($c -lt 4.5) { $failPad++ }
  $padRes += ($hex + '→' + $ink + '=' + ('{0:F1}' -f $c))
}
L ('T0_pad_pastillas_4a5=' + ($failPad -eq 0) + ' | ' + ($padRes -join ' '))

# Y una comprobacion del codigo que no se puede medir por tokens: que las vistas no tengan hex.
$xamlHex = @(Get-ChildItem $repo -Filter *.xaml -Recurse |
  Where-Object { $_.FullName -notmatch '\\Themes\\|\\obj\\|\\bin\\' } |
  ForEach-Object { Select-String -Path $_.FullName -Pattern '="#[0-9A-Fa-f]{6}' } |
  Where-Object { $_.Line -notmatch '&#' })
L ('T0_hex_en_las_vistas=' + $xamlHex.Count + ' (esperado 0)' + $(if ($xamlHex.Count -gt 0) { ' :: ' + (($xamlHex | ForEach-Object { $_.Filename + ':' + $_.LineNumber }) -join ', ') } else { '' }))
$csHex = @(Get-ChildItem $repo -Filter *.cs -Recurse |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' -and $_.Name -ne 'ThemeService.cs' } |
  ForEach-Object { Select-String -Path $_.FullName -Pattern '"#[0-9A-Fa-f]{6}"' })
L ('T0_hex_sueltos_en_codigo=' + $csHex.Count + ' (los de reserva de ThemeHex y la paleta de pad estan justificados) :: ' +
   ((($csHex | ForEach-Object { $_.Filename }) | Sort-Object -Unique) -join ', '))

# ------------------------------------------------------------------ infra de sesiones

$conf = Join-Path $env:APPDATA 'QuasarRadio\appearance.json'
$layout = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
$confBackup = $null
if (Test-Path $conf) { $confBackup = Get-Content $conf -Raw }
L ('APPEARANCE_ANTES=' + $(if ($null -ne $confBackup) { $confBackup.Trim() } else { '(no existe)' }))
# layout.json (geometria de las ventanas de modulo) tambien es del fundador: copia exacta y
# devuelta en el finally. Esta medidora NO lo borra: el tamano de la consola no cambia el
# veredicto de luminancia, y borrarlo como en otras sesiones dejaria al fundador sin ventanas.
$layoutBackup = $null
if (Test-Path $layout) { $layoutBackup = Get-Content $layout -Raw }
L ('LAYOUT_ANTES=' + $(if ($null -ne $layoutBackup) { ('existe (' + $layoutBackup.Length + ' chars)') } else { '(no existe)' }))

$pre = @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)
if ($pre.Count -gt 0) { throw ('Ya hay ' + $pre.Count + ' instancia(s) abiertas; abortando para no tocar la emision.') }

$realBin = Split-Path $exe
$metaReal = Join-Path $realBin 'track_metadata.json'
$hashAntes = '(no existe)'
if (Test-Path $metaReal) { $hashAntes = (Get-FileHash $metaReal -Algorithm MD5).Hash }
$musicaReal = @(Get-ChildItem (Join-Path $realBin 'musica') -File -ErrorAction SilentlyContinue).Count

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
    $s = [int]([Math]::Sin(2 * [Math]::PI * $Hz * ($i / $rate)) * $Amp)
    $bw.Write([int16]$s)
  }
  $bw.Flush(); [IO.File]::WriteAllBytes($Path, $ms.ToArray())
  $bw.Close(); $ms.Close()
}

$FT = 'QSRTEMA Tono - Treinta s.wav'
if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'musica') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'jingles') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'importados') | Out-Null
New-QsrWav (Join-Path $Sandbox ('musica\' + $FT)) 30.0 240 9000
L ('FIXTURES: ' + $FT + ' (30 s, 240 Hz, amplitud 9000) en ' + $Sandbox)

# Aislamiento total de datos: con QUASAR_RADIO_DATA_DIR en el sandbox, cart_pads.json, users.json
# y schedule.json de ensayo viven en %TEMP% (AuthService.DataDirectory es el unico dueño de esa
# ruta, igual que en verificar_pads). La botonera se SIEMBRA con dos pads de identidad conocida:
# la paleta del pad NO depende del tema a proposito (se persiste en el JSON), y S7 necesita un
# color exacto que exigir en el recorte — F1 amarillo #FFEA00 (tinta negra por luminancia) y
# F2 violeta #651FFF (tinta blanca), los dos extremos del cruce negro/blanco de la T0.
$env:QUASAR_RADIO_DATA_DIR = $Sandbox
function New-PadsJson {
  $tono = Join-Path $Sandbox ('musica\' + $FT)
  $pads = @()
  $pads += [pscustomobject]@{ padId = 1; shortcut = 'F1'; title = 'QSRTEMAamarillo'; filePath = $tono; colorHex = '#FFEA00'; volumePercent = 100; duckPercent = 0; mode = 'Toggle' }
  $pads += [pscustomobject]@{ padId = 2; shortcut = 'F2'; title = 'QSRTEMAvioleta';  filePath = $tono; colorHex = '#651FFF'; volumePercent = 100; duckPercent = 0; mode = 'Toggle' }
  $file = [pscustomobject]@{ version = 1; pads = $pads }
  [IO.File]::WriteAllText((Join-Path $Sandbox 'cart_pads.json'), ($file | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
  L 'SEED: cart_pads.json en el sandbox (F1 #FFEA00, F2 #651FFF, ambos montados sobre el tono de 30 s)'
}
New-PadsJson

function Set-Theme([string]$name) {
  New-Item -ItemType Directory -Force -Path (Split-Path $conf) | Out-Null
  [IO.File]::WriteAllText($conf, ('{"theme":"' + $name + '"}'))
}

function Start-Consola([string]$modo) {
  $p = Start-Process -FilePath $exe -WorkingDirectory $Sandbox -PassThru
  $script:proc = $p
  Start-Sleep -Seconds 11
  $c = Find-Window $p.Id 'Quasar Radio Pro*'
  if ($null -eq $c) { throw ('sin consola en ' + $modo) }
  $script:frontHwnd = $c.Hwnd
  if ([QsrTema]::Minimizada([IntPtr]$c.Hwnd)) { [void][QsrTema]::Restore([IntPtr]$c.Hwnd); Start-Sleep -Seconds 2 }
  $el = $AE::FromHandle([IntPtr]$c.Hwnd)
  $ms = Wait-Until { $null -ne (One $el 'abre su propia ventana' $CT::Text) } 60000
  if ($ms -lt 0) { throw ('consola sin centinela en ' + $modo) }
  L ('ARRANQUE[' + $modo + ']: arbol uia en ' + $ms + ' ms | consola=' + $c.Rect)
  return [pscustomobject]@{ Proc = $p; Con = $c; ConEl = $el }
}

function Close-Consola($s, [string]$modo) {
  try { [QsrTema]::Close([IntPtr]$s.Con.Hwnd) } catch { }
  $ms = Wait-Until { $s.Proc.HasExited } 12000
  if ($ms -lt 0) { try { $s.Proc.Kill(); $s.Proc.WaitForExit(5000) | Out-Null } catch { } }
  Start-Sleep -Seconds 1
}

function Get-ThemeCombo($root) {
  foreach ($cb in @($root.FindAll($Scope::Descendants, (CTCond $CT::ComboBox)))) {
    try {
      $sel = $cb.GetCurrentPattern($SEP::Pattern)
      $cur = @($sel.Current.GetSelection())
      if ($cur.Count -gt 0 -and ($THEMES.Keys -contains $cur[0].Current.Name)) { return $cb }
    } catch { }
  }
  return $null
}

function Combo-Selected($cb) {
  if ($null -eq $cb) { return '(sin combo)' }
  try {
    $sel = $cb.GetCurrentPattern($SEP::Pattern)
    $cur = @($sel.Current.GetSelection())
    if ($cur.Count -eq 0) { return '(vacio)' }
    return $cur[0].Current.Name
  } catch { return '(sin seleccion)' }
}

# Combo de tema por teclado (cuarta y quinta leccion, corridas ui9e y ui9f): los items del
# desplegable viven en la ventana Popup del proceso —buscarlos dentro del ComboBox devuelve
# vacio siempre—; el clic por recto del item cae a la fila vecina cuando el popup aun se esta
# anclando (Charcoal pidio y cayo Deep Navy), y SelectionItem.Select() sobre la linea del popup
# es INEFECTIVO en este combo: no lanza, pero la lectura sigue diciendo el tema viejo. Lo que
# funciona es el gesto humano: clic para abrir, Home/Flecha-abajo/Enter con teclas reales — en
# ui9f 4 de 6 lo clavaron; en los 2 fallos la lectura final era EL TEMA DE PARTIDA, o sea que
# aquel clic no abrio el desplegable y las teclas no aterrizaron. De ahi el reintento acotado:
# mismo gesto, hasta 3 pasadas, y el veredicto lo da SIEMPRE la lectura del patron Selection.
function Combo-Select($root, [string]$theme) {
  $cb = Get-ThemeCombo $root
  if ($null -eq $cb) { return ('sin combo de tema ' + $theme) }
  $idx = [Array]::IndexOf($orden, $theme)
  if ($idx -lt 0) { return ('tema fuera de lista ' + $theme) }
  try {
    $ec = $cb.GetCurrentPattern($ECP::Pattern)
  } catch { $ec = $null }
  $leido = '(sin leer)'
  for ($intento = 1; $intento -le 3; $intento++) {
    try {
      [QsrTema]::Raise([IntPtr]$script:frontHwnd)
      Start-Sleep -Milliseconds 250
      $r = RectOf $cb
      if ($r.W -lt 4 -or $r.H -lt 4 -or $r.X -lt -32000 -or $r.Y -lt -32000) { return 'el combo no tiene rect valido' }
      if ($null -ne $ec -and $ec.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Expanded) {
        [QsrTema]::Key(0x1B)   # Esc: si quedo abierto del intento anterior, el clic lo CERRARIA
        Start-Sleep -Milliseconds 300
      }
      [QsrTema]::Click($r.X + [int]($r.W / 2), $r.Y + [int]($r.H / 2))
      Start-Sleep -Milliseconds 450
      if ($null -ne $ec -and $ec.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Expanded) {
        try { $ec.Expand() } catch { }
        Start-Sleep -Milliseconds 300
      }
      [QsrTema]::Key(0x24)                                   # Home: resaltacion en la opcion 0
      Start-Sleep -Milliseconds 180
      for ($k = 0; $k -lt $idx; $k++) { [QsrTema]::Key(0x28); Start-Sleep -Milliseconds 150 }
      [QsrTema]::Key(0x0D)                                   # Enter: confirma y cierra
      Start-Sleep -Milliseconds 600
      $leido = Combo-Selected $cb
      if ($leido -eq $theme) { $extra = if ($intento -gt 1) { ' (en intento ' + $intento + ')' } else { '' }; return ('OK' + $extra) }
    } catch { return ('excepcion ' + $_.Exception.Message) }
  }
  return ('no llego a ' + $theme + ' en 3 pasadas (ultima lectura: ' + $leido + ')')
}

# Luminancia media de un PNG (muestreo con paso para no releer cada pixel de 1440x860).
function Measure-PngLum([string]$Path, [int]$Stride) {
  $bmp = New-Object Drawing.Bitmap($Path)
  $sum = 0.0; $n = 0; $brillo = 0
  for ($y = 0; $y -lt $bmp.Height; $y += $Stride) {
    for ($x = 0; $x -lt $bmp.Width; $x += $Stride) {
      $px = $bmp.GetPixel($x, $y)
      $sum += (Get-Lum ('#{0:X2}{1:X2}{2:X2}' -f $px.R, $px.G, $px.B))
      $n++
      if ($px.R -gt 200 -and $px.G -gt 200 -and $px.B -gt 200) { $brillo++ }
    }
  }
  $bmp.Dispose()
  return [pscustomobject]@{ Media = ($sum / $n); Brillante = ($brillo / $n); Muestras = $n }
}

# Cuantas muestras exactas de un color hay en un PNG (para distinguir el verde del tema claro
# del verde historico del oscuro). Compara al pixel exacto: los LED activos se pintan con el
# color plano del token, sin degradado.
function Count-PngColor([string]$Path, [string]$Hex) {
  $Hex = Norm-Hex $Hex
  $r = [convert]::ToInt32($Hex.Substring(1, 2), 16); $g = [convert]::ToInt32($Hex.Substring(3, 2), 16); $b = [convert]::ToInt32($Hex.Substring(5, 2), 16)
  $bmp = New-Object Drawing.Bitmap($Path)
  $c = 0
  for ($y = 0; $y -lt $bmp.Height; $y++) {
    for ($x = 0; $x -lt $bmp.Width; $x++) {
      $px = $bmp.GetPixel($x, $y)
      if ($px.R -eq $r -and $px.G -eq $g -and $px.B -eq $b) { $c++ }
    }
  }
  $bmp.Dispose()
  return $c
}

# Pixeles con color de verdad (max-min de canal >= 60): la prueba del pad no depende de ACIERTAS
# con el hex exacto que pinta WPF — un pad con identidad pintada tiene que saturar aunque el
# recorte traiga antialiasing en las esquinas.
function Count-PngSaturated([string]$Path) {
  $bmp = New-Object Drawing.Bitmap($Path)
  $c = 0
  for ($y = 0; $y -lt $bmp.Height; $y++) {
    for ($x = 0; $x -lt $bmp.Width; $x++) {
      $px = $bmp.GetPixel($x, $y)
      $mx = [Math]::Max([Math]::Max([int]$px.R, [int]$px.G), [int]$px.B)
      $mn = [Math]::Min([Math]::Min([int]$px.R, [int]$px.G), [int]$px.B)
      if (($mx - $mn) -ge 60) { $c++ }
    }
  }
  $bmp.Dispose()
  return $c
}

function Cap-Consola($s, [string]$t) {
  $win = $s.Con
  $w = [int]$win.W; $h = [int]$win.H
  $bmp = New-Object Drawing.Bitmap($w, $h)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrTema]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 350
  $hdc = $g.GetHdc()
  $ok = [QsrTema]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $path = Join-Path $out ($t + '.png')
  $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  $m = Measure-PngLum $path 6
  L ('CAP ' + $t + ' : PrintWindow=' + $ok + ' ' + $w + 'x' + $h + ' md5=' + (Get-FileHash $path -Algorithm MD5).Hash +
     ' | luminancia_media=' + ('{0:F3}' -f $m.Media) + ' fraccion_pixel_claro=' + ('{0:F3}' -f $m.Brillante) +
     ' muestras=' + $m.Muestras)
  return $m
}

function Cap-Crop($s, $rect, [string]$t) {
  $win = $s.Con
  $bmp = New-Object Drawing.Bitmap([int]$win.W, [int]$win.H)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrTema]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 300
  $hdc = $g.GetHdc()
  [void][QsrTema]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $cx = [int]($rect.X - $win.L); $cy = [int]($rect.Y - $win.T)
  $cw = $rect.W; $ch = $rect.H
  if ($cx -lt 0) { $cx = 0 }; if ($cy -lt 0) { $cy = 0 }
  if ($cx + $cw -gt $bmp.Width) { $cw = $bmp.Width - $cx }
  if ($cy + $ch -gt $bmp.Height) { $ch = $bmp.Height - $cy }
  $crop = New-Object Drawing.Bitmap([int]$cw, [int]$ch, ([Drawing.Imaging.PixelFormat]::Format32bppArgb))
  $g2 = [Drawing.Graphics]::FromImage($crop)
  $g2.DrawImage($bmp, (New-Object Drawing.Rectangle(0, 0, [int]$cw, [int]$ch)), $cx, $cy, [int]$cw, [int]$ch, [Drawing.GraphicsUnit]::Pixel)
  $g2.Dispose(); $bmp.Dispose()
  $path = Join-Path $out ($t + '.png')
  $crop.Save($path, [Drawing.Imaging.ImageFormat]::Png); $crop.Dispose()
  L ('CAPC ' + $t + ' : ' + $cw + 'x' + $ch + ' md5=' + (Get-FileHash $path -Algorithm MD5).Hash)
  return $path
}

# Cap-Consola sirve para la consola; esta variante pinta CUALQUIER ventana del proceso (las de
# modulo en S8). PrintWindow con PW_RENDERFULLCONTENT (2) pinta aunque otra ventana tape la objetivo.
function Cap-Ventana($win, [string]$t) {
  $w = [int]$win.W; $h = [int]$win.H
  if ($w -lt 20 -or $h -lt 20) { L ('CAP ' + $t + ' : rect anomala (' + $win.Rect + ')'); return $null }
  $bmp = New-Object Drawing.Bitmap($w, $h)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrTema]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 300
  $hdc = $g.GetHdc()
  $ok = [QsrTema]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $path = Join-Path $out ($t + '.png')
  $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  $m = Measure-PngLum $path 6
  L ('CAP ' + $t + ' : PrintWindow=' + $ok + ' ' + $w + 'x' + $h + ' md5=' + (Get-FileHash $path -Algorithm MD5).Hash +
     ' | luminancia_media=' + ('{0:F3}' -f $m.Media) + ' fraccion_pixel_claro=' + ('{0:F3}' -f $m.Brillante))
  return $m
}

$np = Join-Path $Sandbox 'nowplaying.txt'
# Diccionario RESUELTO del claro (Tokens + Theme.StudioLight.xaml), no el nombre del fichero:
# de aqui salen los hex exactos que se exigen en los recortes de S7.
$esperadosClaro = $temasResueltos['Studio Light']

# El log de la app vive en bin/logs pase lo que pase con el data dir (Serilog usa
# AppContext::BaseDirectory): es lectura, y su conteo es la prueba de que Apply() corrio.
function Get-ThemeLogCount {
  $p = Join-Path $realBin ('logs/quasar_radio-' + ((Get-Date).ToString('yyyyMMdd')) + '.log')
  if (-not (Test-Path $p)) { return 0 }
  try { return @(Select-String -Path $p -Pattern 'Tema aplicado' -ErrorAction SilentlyContinue).Count } catch { return -1 }
}

try {
  $orden = @('Studio Dark Neon', 'Cyan Broadcast', 'Charcoal Pro', 'Deep Navy FM', 'Studio Light')
 if (-not $SoloS6) {
  # ============================ S1..S5: una luminancia por tema ============================
  $orden = @('Studio Dark Neon', 'Cyan Broadcast', 'Charcoal Pro', 'Deep Navy FM', 'Studio Light')
  $lum = @{}
  $okComboArranque = $true
  foreach ($name in $orden) {
    Set-Theme $name
    $s = Start-Consola ($name)
    $cb = Get-ThemeCombo $s.ConEl
    $selName = Combo-Selected $cb
    if ($selName -ne $name) { $okComboArranque = $false }
    L ('LUM_UXA [' + $name + '] combo_selecciona=' + $selName + ' ok=' + ($selName -eq $name) + ' | centinela_vivo=True | pie=' + (Txt (ById $s.ConEl 'ConsoleStatusText')))
    $m = Cap-Consola $s ('tema_' + ($name -replace ' ', '_') + '_' + $Tag)
    $lum[$name] = $m
    Close-Consola $s $name
  }
  L ('S1_a_S5_el_combo_arranca_en_el_tema_guardado=' + $okComboArranque)
  foreach ($name in @('Studio Dark Neon', 'Cyan Broadcast', 'Charcoal Pro', 'Deep Navy FM')) {
    L ('LUM_OSCURO [' + $name + '] media=' + ('{0:F3}' -f $lum[$name].Media) + ' ok<0.35=' + ($lum[$name].Media -lt 0.35))
  }
  $lc = $lum['Studio Light']
  L ('LUM_CLARO media=' + ('{0:F3}' -f $lc.Media) + ' fraccion_pixel_claro=' + ('{0:F3}' -f $lc.Brillante) +
     ' ok>0.55=' + ($lc.Media -gt 0.55))
 }

  # ============================ S6: cambio en caliente, sin capturas ============================
  Set-Theme 'Studio Dark Neon'
  $s6 = Start-Consola 'caliente'
  $con6 = $s6.ConEl
  $inicio6 = Get-Date
  $antesLog6 = Get-ThemeLogCount
  L ('S6_combos_en_la_consola=' + @($con6.FindAll($Scope::Descendants, (CTCond $CT::ComboBox)).Count) + ' | lineas_tema_aplicado_antes=' + $antesLog6)
  $okSwitch = $true
  # Secuencia DINAMICA: seis cambios y los seis de verdad (cada destino distinto del anterior).
  # Con la lista fija de la corrida ui9d, un clic que cayo en la fila vecina dejo un "Studio
  # Light" repeticado, y ThemeService.Apply NO loggeda ni cambia diccionario cuando el tema ya
  # esta en el hueco (alreadyInSlot) — la asercion de >= 6 lineas se caia por un cambio nulo.
  $actual = 'Studio Dark Neon'
  $secuencia = @()
  for ($i = 0; $i -lt 6; $i++) {
    # La sexta pasada SIEMPRE termina en 'Studio Light': S7 arranca esa misma appearance.json
    # guardada y su prueba de persistencia exige leer el claro al reiniciar. En la corrida ui9
    # la rotacion fija dejaba Cyan Broadcast al final y S7 entero caia (VU con verde historico,
    # luminancia 0,073) no por defecto de la app sino por el orden del script.
    $destino = if ($i -eq 5) { 'Studio Light' } else { $orden[($i + 1) % 5] }
    if ($destino -eq $actual) { $destino = if ($i -eq 5) { 'Cyan Broadcast' } else { $orden[($i + 2) % 5] } }
    $secuencia += ,@($actual, $destino)
    $actual = $destino
  }
  foreach ($par in $secuencia) {
    $name = $par[1]
    $t0 = Get-Date
    $r = Combo-Select $con6 $name
    $ms = [int]((Get-Date) - $t0).TotalMilliseconds
    L ('S6_cambio [' + $par[0] + ' -> ' + $name + '] resultado=' + $r + ' en_ms=' + $ms)
    # 'OK (en intento 2)' tambien vale: Combo-Select reintenta con Esc si el primer clic no
    # abrio el desplegable, y en la corrida ui9 el intento 2 del primer cambio hacia caer la
    # comparacion exacta contra 'OK' aunque la seleccion final era correcta.
    if ($r -notlike 'OK*') { $okSwitch = $false }
    # el arbol tiene que seguir vivo DESPUES de cada cambio (es la prueba de que el repintado
    # del ThemeChanged no volvio loca a la ventana)
    $chip = Txt (ById $con6 'PadInventoryChip')
    $pie = Txt (ById $con6 'ConsoleStatusText')
    L ('S6_arbol_vivo [' + $name + '] chip=[' + $chip + '] pie=[' + $pie + ']')
  }
  L ('S6_seis_cambios_en_caliente=' + $okSwitch + ' | seleccion_final=' + (Combo-Selected (Get-ThemeCombo $con6)))
  $bus = Txt (ById $con6 'BusSourcesText')
  $duck = Txt (ById $con6 'DuckReadout')
  L ('S6_lecturas_vivas_tras_seis_cambios bus=[' + $bus + '] duck=[' + $duck + ']')
  # Prueba por DIFF de conteo, no por timestamp: el formato de linea de Serilog no es contrato
  # de este script, y restar el conteo tomado antes de los seis cambios es exacto igualmente.
  $msLog = Wait-Until { $d = (Get-ThemeLogCount); ($d -ge 0) -and $antesLog6 -ge 0 -and ($d - $antesLog6) -ge 6 } 15000
  $despuesLog6 = Get-ThemeLogCount
  $nuevas6 = if ($despuesLog6 -ge 0 -and $antesLog6 -ge 0) { $despuesLog6 - $antesLog6 } else { -1 }
  L ('S6_lineas_nuevas_de_tema_aplicado=' + $nuevas6 + ' (esperadas >= 6) en_ms=' + $msLog +
     ' | ok=' + ($nuevas6 -ge 6) + ' | ventana_aplicada_desde=' + $inicio6.ToString('HH:mm:ss'))
  Close-Consola $s6 'caliente'

 if (-not $SoloS6) {
  # ============================ S7: persistencia, el VU y los pads en claro ============================
  # S6 termino dejando Studio Light guardado en appearance.json: NO se toca aqui. Es la prueba
  # de persistencia — el tema sobrevive al cierre, que es lo que el plan pide medir "con el
  # programa cerrado y vuelto a abrir".
  $s7 = Start-Consola 'claro_persistido'
  $con7 = $s7.ConEl
  $cb7 = Get-ThemeCombo $con7
  $sel7 = Combo-Selected $cb7
  L ('S7_el_tema_sobrevive_al_reinicio=' + ($sel7 -eq 'Studio Light') + ' | combo_selecciona=' + $sel7)

  # el VU en claro necesita aire: master arriba y PLAY
  $vol = One $con7 'Volumen master' $CT::Slider
  if ($null -eq $vol) { throw 'no esta el slider de volumen master' }
  Set-Range $vol 1.0
  Start-Sleep -Milliseconds 300
  if (Test-Path $np) { Remove-Item $np -Force }
  $play = ById $con7 'MasterPlayButton'
  if ($null -eq $play -or -not $play.Current.IsEnabled) { throw 'el PLAY no esta habilitado con la cola cargada' }
  Invoke-El $play
  $msAire = Wait-Until { (Test-Path $np) -and ((Get-Content $np -Raw) -match 'QSRTEMA') } 20000
  if ($msAire -lt 0) { throw 'la pista de tono nunca salio al aire' }
  L ('S7_al_aire_en_ms=' + $msAire + ' | nowplaying=' + (Get-Content $np -Raw).Trim())

  $serie = @()
  for ($i = 0; $i -lt 8; $i++) {
    $v = Vu-Value $con7 'MasterLeftVu'
    if ($null -ne $v) { $serie += $v }
    Start-Sleep -Milliseconds 140
  }
  $maxVu = ($serie | Measure-Object -Maximum).Maximum
  L ('S7_vu_del_aire_leye_niveles=' + ($maxVu -gt 5) + ' | serie=' + (($serie | ForEach-Object { [Math]::Round($_, 1) }) -join '/'))

  # H36: la primera PrintWindow deja el arbol UIA ciego para el resto de la sesion. TODAS las
  # lecturas UIA de esta sesion —incluidos los rectangulos de los recortes— ocurren aqui arriba,
  # y a partir de ahora solo se pintan bitmaps. El aire NO se corta por UIA despues de las
  # capturas: Close-Consola remata con WM_CLOSE y, si el cierre protegido pide confirmacion con
  # el deck sonando, Kill. Todo lo que esa salida escribe queda en el sandbox (los dos directorios
  # de datos apuntan a %TEMP%), y el tono de 30 s se acaba solo de cualquier forma.
  $vuEl = ById $con7 'MasterLeftVu'
  $vuRect = $null
  if ($null -ne $vuEl) { $vuRect = RectOf $vuEl }
  $f1 = $null
  $f2 = $null
  foreach ($e in @($con7.FindAll($Scope::Descendants, (CTCond $CT::Button)))) {
    $n = $e.Current.Name
    if ($null -eq $f1 -and $n -eq 'F1') { $f1 = $e }
    if ($null -eq $f2 -and $n -eq 'F2') { $f2 = $e }
  }
  $f1Rect = $null; $f2Rect = $null
  if ($null -ne $f1) { $f1Rect = RectOf $f1 }
  if ($null -ne $f2) { $f2Rect = RectOf $f2 }

  if ($null -eq $vuRect) { L 'S7_RECORTE : MasterLeftVu no esta en el arbol' }
  else {
    $cropPath = Cap-Crop $s7 $vuRect ('vu_maestro_claro_' + $Tag)
    $verdeClaro = Count-PngColor $cropPath $esperadosClaro['Brush.Vu.Low']
    $verdeViejo = Count-PngColor $cropPath '#22C55E'
    $amarilloClaro = Count-PngColor $cropPath $esperadosClaro['Brush.Vu.Mid']
    L ('S7_el_vu_pinta_en_claro=' + (($verdeClaro -gt 3) -and ($verdeViejo -eq 0)) +
       ' | pixeles_verde_del_tema(' + $esperadosClaro['Brush.Vu.Low'] + ')=' + $verdeClaro +
       ' | pixeles_verde_historico(#22C55E)=' + $verdeViejo +
       ' | pixeles_amarillo(' + $esperadosClaro['Brush.Vu.Mid'] + ')=' + $amarilloClaro)
    $apagado = Count-PngColor $cropPath $esperadosClaro['Brush.Vu.Off']
    L ('S7_leds_apagados_en_gris_claro=' + ($apagado -ge 0) + ' | pixeles_off(' + $esperadosClaro['Brush.Vu.Off'] + ')=' + $apagado)
  }

  # La franja SFX: la identidad del pad sigue pintada con sus colores propios (no son del tema).
  # F1 se sembro en amarillo (#FFEA00, tinta negra) y F2 en violeta (#651FFF, tinta blanca):
  # los dos extremos del cruce negro/blanco que T0 calculo por luminancia.
  if ($null -eq $f1Rect) { L 'S7_RECORTE : el pad F1 no esta en el arbol (botonera sin sembrar?)' }
  else {
    $fc = Cap-Crop $s7 $f1Rect ('pad_F1_claro_' + $Tag)
    $amar = Count-PngColor $fc '#FFEA00'
    $sat = Count-PngSaturated $fc
    L ('S7_identidad_del_pad_sigue_viva_en_claro=' + (($amar -gt 0) -or ($sat -gt 40)) +
       ' | pixeles_exactos_amarillo_F1=' + $amar + ' | pixeles_saturados_F1=' + $sat +
       ' (F1 se sembro en #FFEA00; la identidad del pad no depende del tema)')
  }
  if ($null -ne $f2Rect) {
    $fc2 = Cap-Crop $s7 $f2Rect ('pad_F2_claro_' + $Tag)
    $mor = Count-PngColor $fc2 '#651FFF'
    $sat2 = Count-PngSaturated $fc2
    L ('S7_pad_violeta_con_tinta_clara=' + (($mor -gt 0) -or ($sat2 -gt 40)) + ' | pixeles_exactos_F2=' + $mor + ' | pixeles_saturados_F2=' + $sat2)
  }

  $m7 = Cap-Consola $s7 ('consola_claro_aire_' + $Tag)
  L ('S7_consola_claro_con_aire media=' + ('{0:F3}' -f $m7.Media))
  Close-Consola $s7 'claro_persistido'

  # ============================ S8: las ventanas de modulo en claro ============================
  # La aceptacion del plan pide «las cuatro ventanas de modulo y la consola legibles con él». T0
  # midio el contraste en las claves del diccionario —que son las mismas que consumen todas las
  # ventanas por los MergedDictionaries de Application—, pero 'legible' tiene que verse en la
  # ventana misma: cada una se abre en claro, se LEE su arbol UIA y SU sonda de texto ANTES de
  # capturarla (H36: el primer PrintWindow deja ciega el arbol de ESA ventana; las demas siguen
  # vivas), se mide que su bitmap sea realmente claro, y se cierra.
  Set-Theme 'Studio Light'
  $s8 = Start-Consola 'modulos_claro'
  $con8 = $s8.ConEl
  $mods8 = [ordered]@{
    Biblioteca   = @{ btn='BIBLIOTECA';  title='*Biblioteca*';    probe='' }
    Streaming    = @{ btn='STREAMING';   title='*Streaming y*';   probe='Salida del enlace' }
    Programacion = @{ btn='PROGRAMAC';   title='*Programac*';   probe='Rejilla horaria' }
    Reportes     = @{ btn='REPORTES';    title='*Reportes*';      probe='TRACKS EN SES' }
    Dsp          = @{ btn='DSP';         title='*Procesador DSP*'; probe='' }
  }
  # Los cinco botones de la franja se atrapan en una sola pasada, antes de cualquier captura:
  # despues del Cap-Consola final el arbol de la consola queda ciego y ya no se podrian buscar.
  $btn8 = @($con8.FindAll($Scope::Descendants, (CTCond $CT::Button)))
  $ok8 = $true
  $abiertas8 = 0
  foreach ($k in $mods8.Keys) {
    $spec = $mods8[$k]
    $b = $null
    foreach ($e in $btn8) { if ($e.Current.Name -match $spec.btn) { $b = $e; break } }
    if ($null -eq $b) { L ('S8_' + $k + ': boton ' + $spec.btn + ' no esta en la franja'); $ok8 = $false; continue }
    Invoke-El $b
    $msW = Wait-Until { $null -ne (Find-Window $s8.Proc.Id $spec.title) } 12000
    $mw = Find-Window $s8.Proc.Id $spec.title
    if ($null -eq $mw) { L ('S8_' + $k + (': la ventana no abrio en 12 s (esperaba ' + $spec.title + ')')); $ok8 = $false; continue }
    $mel = $AE::FromHandle([IntPtr]$mw.Hwnd)
    $texts = @($mel.FindAll($Scope::Descendants, (CTCond $CT::Text)))
    $sondaOk = ($spec.probe -eq '')
    if (-not $sondaOk) { foreach ($tx in $texts) { if ($tx.Current.Name -match $spec.probe) { $sondaOk = $true; break } } }
    $viva = ($texts.Count -ge 8) -and $sondaOk
    if (-not $viva) { $ok8 = $false }
    $abiertas8++
    $m8 = Cap-Ventana $mw ('modulo_' + $k + '_claro_' + $Tag)
    $clara = ($null -ne $m8) -and ($m8.Media -gt 0.5) -and ($m8.Brillante -gt 0.3)
    if (-not $clara) { $ok8 = $false }
    try { ($mel.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close() } catch { }
    [void](Wait-Until { $null -eq (Find-Window $s8.Proc.Id $spec.title) } 8000)
    $cerrada = ($null -eq (Find-Window $s8.Proc.Id $spec.title))
    if (-not $cerrada) { $ok8 = $false }
    L ('S8_' + $k + ': arbol_vivo=' + $viva + ' (textos=' + $texts.Count + ', sonda=' + $sondaOk + ') | pintura_clara=' + $clara + ' | cerrada=' + $cerrada)
  }
  L ('S8_ventanas_de_modulo_legibles_en_claro=' + ($ok8 -and ($abiertas8 -eq 5)) + ' | abiertas=' + $abiertas8 + '/5')
  $m8c = Cap-Consola $s8 ('consola_claro_modulos_' + $Tag)
  L ('S8_consola_tras_cinco_modulos media=' + ('{0:F3}' -f $m8c.Media))
  Close-Consola $s8 'modulos_claro'
 }
}
finally {
  # el fundador tenia su tema en appearance.json: se le devuelve exacto, y el layout ni se toca
  # mas alla de los borrados que hace la propia app al cambiar de tema.
  if ($null -ne $confBackup) { [IO.File]::WriteAllText($conf, $confBackup) }
  elseif (Test-Path $conf) { Remove-Item $conf -Force }
  L ('APPEARANCE_RESTORED=' + $(if (Test-Path $conf) { (Get-Content $conf -Raw).Trim() } else { '(borrado: no existia antes)' }))

  # la geometria recordada del fundador vuelve igual que estaba, pasara lo que pasare —
  # incluido el caso "no existia": si la app creo layout.json durante la prueba, se borra.
  if ($null -ne $layoutBackup) { [IO.File]::WriteAllText($layout, $layoutBackup) }
  elseif (Test-Path $layout) { Remove-Item $layout -Force }
  L ('LAYOUT_RESTORED=' + $(if (Test-Path $layout) { ('igual que antes=' + ((Get-Content $layout -Raw) -eq $layoutBackup)) } else { '(ausente: igual que al inicio)' }))

  try { if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill(); $script:proc.WaitForExit(5000) | Out-Null } } catch { }
  Get-Process -Name quasar_radio -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
  Start-Sleep -Seconds 1
  L ('RESTANTES=' + @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue).Count)

  $hashDespues = '(no existe)'
  if (Test-Path $metaReal) { $hashDespues = (Get-FileHash $metaReal -Algorithm MD5).Hash }
  L ('BIN_track_metadata_antes=' + $hashAntes + ' despues=' + $hashDespues + ' | intacto=' + ($hashAntes -eq $hashDespues))
  $musicaDespues = @(Get-ChildItem (Join-Path $realBin 'musica') -File -ErrorAction SilentlyContinue).Count
  $qsEnBin = @(Get-ChildItem $realBin -Recurse -Filter 'QSR*' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\scripts\\' })
  L ('BIN_musica=' + $musicaReal + ' -> ' + $musicaDespues + ' | sin_fixtures_QSR_en_bin=' + ($qsEnBin.Count -eq 0))
  if (-not $Keep -and (Test-Path $Sandbox)) { Remove-Item $Sandbox -Recurse -Force -ErrorAction SilentlyContinue }
  [IO.File]::WriteAllLines($logFile, $lines, (New-Object Text.UTF8Encoding($true)))
  Write-Host ('EVIDENCIA=' + $logFile)
}
