param(
  [string]$Theme = 'Studio Dark Neon',
  [string]$Tag = 'ui7',
  [string]$Sandbox = (Join-Path $env:TEMP 'qsr_med_test'),
  [switch]$Keep
)

# UI-7: verificacion en vivo de la medicion de la consola y de la franja nueva bajo los decks.
#
# Cubre las dos quejas del fundador ("el vu metro del nivel de salida master no hace nada y el
# detector de silencio aparentemente tampoco hace nada" y "debajo de los vumetros de los deck A y B
# hay otra especie de vumetros sin funcion") con medidas leidas desde fuera por UIA, no con
# capturas: los cuatro medidores exponen su nivel por el patron RangeValue/Value y el nivel de
# salida tiene lectura en cifras. La clausa del plan que se comprueba aqui es: "con un deck sonando
# y master a 0 dB los dos VU de deck y el master se mueven; con master a 0 y aire sonando el
# detector dispara en su umbral y el aviso queda fijo en el pie; y la franja que quedaba libre bajo
# los decks lleva botones reales: repetir pista y pre-escucha".
#
# Se ejecuta SIEMPRE contra una libreria de ensayo en %TEMP%, nunca contra la real. La libreria de
# la emisora vive en bin/Debug/net10.0-windows (musica/, horarias/, ...) y no esta en git: un error
# aqui la borrarla sin copia. El exe arranca con -WorkingDirectory $Sandbox porque
# AudioDatabaseService escanea "musica" relativo al directorio actual y TrackMetadataService
# escribe track_metadata.json tambien relativo al directorio actual.
#
# Los archivos de ensayo llevan el prefijo QSRMED. Jamas HRS ni MIN: con esos nombres se pisaron
# 31 grabaciones reales de horarias/ (ver docs/UI_BASELINE.md).
#
# RUIDO: las ventanas M1/M1b/M3 son los unicos segundos con el master arriba. Sin ellas no hay
# forma de demostrar que el VU de salida responde a la senal y escala con el fader. El resto del
# tiempo, y toda la sesion 2 (el bucle), el master esta en 0 y no sale nada por la tarjeta. La
# pre-escucha si suena: sale por su propia salida, por eso se apaga a los ~0,5 s de pulsarla.

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if (-not ('QsrMed' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class MedWin { public IntPtr Hwnd; public string Title; public bool Visible; public string Rect; public int L; public int T; public int W; public int H; }
public static class QsrMed {
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
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
  // WPF pinta por hardware: PrintWindow sin PW_RENDERFULLCONTENT devolvio el cliente en negro y
  // con la ventana tapada SetForegroundWindow falla desde un proceso de script (el mismo truco de
  // verificar_consola.ps1 y verificar_rotacion.ps1).
  public static void Raise(IntPtr h) {
    ShowWindow(h, 5); BringWindowToTop(h);
    keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
    SetForegroundWindow(h);
  }
  public static void Restore(IntPtr h) { ShowWindow(h, 9); BringWindowToTop(h); }
  public static bool Minimizada(IntPtr h) { return IsIconic(h); }
  public static void Close(IntPtr h) { SendMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }
  static int _target; static List<MedWin> _found;
  public static List<MedWin> Top(int pid) {
    _target = pid; _found = new List<MedWin>();
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == _target) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        var wi = new MedWin();
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
$VP    = [System.Windows.Automation.ValuePattern]
$RVP   = [System.Windows.Automation.RangeValuePattern]

function CTCond($ct) { New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ct) }
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

# LA MEDIDA. El medidor expone RangeValue (0..100, lectura) y Value (el mismo numero como texto).
# Si el enlace apunta al ViewModel equivocado, el patron existe y el valor se lee, pero no se mueve
# nunca: eso es justo lo que una captura de pantalla tapaba.
function Vu-Value($root, $id) {
  $e = ById $root $id
  if ($null -eq $e) { return $null }
  try { return [double]$e.GetCurrentPattern($RVP::Pattern).Current.Value } catch { }
  try { return [double]$e.GetCurrentPattern($VP::Pattern).Current.Value } catch { return $null }
}

# Lectura de la barra del detector (ProgressBar, Maximum=1): es el nivel lineal que ve el
# SilenceDetector, no el porcentaje pintado de los medidores.
function Bar-Value($root, $id) {
  $e = ById $root $id
  if ($null -eq $e) { return $null }
  try { return [Math]::Round([double]$e.GetCurrentPattern($RVP::Pattern).Current.Value, 4) } catch { return $null }
}

# N lecturas seguidas: min / max / ultima. Un medidor clavado a cero no da max>0 con el aire
# sonando, y uno que sigue el fader tiene que cambiar de max entre dos ventanass.
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
  if ($id -notmatch '^(Deck[AB]Vu|MasterLeftVu|MasterRightVu)$') { throw ('Describe-Vu: id de medidor inesperado: ' + $id) }
  $s = Vu-Serie $root $id $n $everyMs
  L ($rotulo + ' [' + $id + '] lecturas=' + $s.n + ' min=' + $s.min + ' max=' + $s.max + ' ultimo=' + $s.last)
  return $s
}

$exe    = 'C:\Users\CY-KILL\Desktop\Programación y Datanálisis\quasar_radio\bin\Debug\net10.0-windows\quasar_radio.exe'
$logdir = Join-Path (Split-Path $exe) 'logs'
$out    = Join-Path $env:TEMP 'qsr_ui'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$lines = New-Object Collections.ArrayList
function L($m) { $lines.Add($m) | Out-Null; Write-Host $m }

function CapWindow($win, $t) {
  if ($null -eq $win) { L ('CAP ' + $t + ' : sin ventana'); return }
  $w = [int]$win.W; $h = [int]$win.H
  if ($w -lt 50 -or $h -lt 50) { L ('CAP ' + $t + ' : rect invalida'); return }
  $bmp = New-Object Drawing.Bitmap($w, $h)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrMed]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 350
  $hdc = $g.GetHdc()
  $ok = [QsrMed]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $path = Join-Path $out ($t + '.png')
  $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  L ('CAP ' + $t + ' : PrintWindow=' + $ok + ' ' + $w + 'x' + $h + ' md5=' + (Get-FileHash $path -Algorithm MD5).Hash)
}

function CapWindowCrop($win, $x, $y, $w, $h, $t) {
  if ($w -lt 20 -or $h -lt 20) { L ('CAP ' + $t + ' : recorte invalido ' + $w + 'x' + $h); return }
  $bmp = New-Object Drawing.Bitmap([int]$win.W, [int]$win.H)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrMed]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 300
  $hdc = $g.GetHdc()
  [void][QsrMed]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)
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

# Recorte a partir del rect de un elemento, con margenes en px. Devuelve sin hacer nada si el
# elemento no esta en el arbol: la evidencia visual nunca puede tumbar la medicion.
function Crop-Element($win, $root, $id, [int]$up, [int]$height, [int]$width, $t) {
  $e = ById $root $id
  if ($null -eq $e) { L ('CAP ' + $t + ' : sin elemento ' + $id); return }
  $r = RectOf $e
  CapWindowCrop $win $r.X ($r.Y - $up) $width $height $t
}

# ---- WAV de ensayo: PCM 16 bits mono, tono senoidal. Amplitud 9000/32768 = 0,27 lineal
# (-11 dBFS): por debajo del techo para poder comprobar que el VU de salida ESCALA con el fader.
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

$FL = 'QSRMED Larga - Noventa s.wav'   # 90 s: da tiempo a las cuatro ventanass de medicion
$FR = 'QSRMED Bucle - Seis s.wav'      # 6 s : la del REPETIR, se oyen 2 vueltas y pico

function New-Sandbox([string]$file, [double]$seg) {
  if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'musica')     | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'jingles')    | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'importados') | Out-Null
  New-QsrWav (Join-Path $Sandbox ('musica\' + $file)) $seg 240
  L ('FIXTURES: ' + $file + ' (' + $seg + ' s, 240 Hz) y nada mas en musica/ -> ' + $Sandbox)
}

function Find-Window($pid_, $pattern) {
  return ([QsrMed]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like $pattern } | Select-Object -First 1)
}

# Arranca la consola con la libreria de ensayo y espera a que el arbol de UIA este publicado.
function Start-Consola([string]$modo) {
  $p = Start-Process -FilePath $exe -WorkingDirectory $Sandbox -PassThru
  Start-Sleep -Seconds 11
  $c = Find-Window $p.Id 'Quasar Radio Pro*'
  if ($null -eq $c) { throw ('sin consola en ' + $modo) }
  if ([QsrMed]::Minimizada([IntPtr]$c.Hwnd)) { [void][QsrMed]::Restore([IntPtr]$c.Hwnd); Start-Sleep -Seconds 2 }
  $el = $AE::FromHandle([IntPtr]$c.Hwnd)
  # Centinela: la frase mas profunda del arbol (la franja de modulos). Mientras el escaneo de la
  # libreria tiene ocupado al hilo de UI, FindAll devuelve solo la parte de arriba (H11).
  $ms = Wait-Until { $null -ne (One $el 'abre su propia ventana' $CT::Text) } 60000
  if ($ms -lt 0) { throw ('consola sin centinela en ' + $modo) }
  L ('ARRANQUE[' + $modo + ']: arbol uia completo en ' + $ms + ' ms tras los 11 s de cortesia | consola=' + $c.Rect)
  return [pscustomobject]@{ Proc = $p; Con = $c; ConEl = $el }
}

function Meta-PlayCount([string]$file) {
  $p = Join-Path $Sandbox 'track_metadata.json'
  if (-not (Test-Path $p)) { return -1 }
  try {
    $m = @(Get-Content $p -Raw | ConvertFrom-Json)
    $hit = @($m | Where-Object { $_.FilePath -like ('*' + $file) }) | Select-Object -First 1
    if ($null -eq $hit) { return -1 }
    return [int]$hit.PlayCount
  } catch { return -1 }
}

$np = Join-Path $Sandbox 'nowplaying.txt'

$pre = @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)
if ($pre.Count -gt 0) { throw ('Ya hay ' + $pre.Count + ' instancia(s) abiertas; abortando para no tocar la emision.') }

# Huella de la libreria REAL antes de empezar: la prueba trabaja en %TEMP%, y si algo se escribe en
# bin/ durante la corrida este informe lo dice.
$realBin = Split-Path $exe
$metaReal = Join-Path $realBin 'track_metadata.json'
$hashAntes = '(no existe)'
if (Test-Path $metaReal) { $hashAntes = (Get-FileHash $metaReal -Algorithm MD5).Hash }
$musicaReal = @(Get-ChildItem (Join-Path $realBin 'musica') -File -ErrorAction SilentlyContinue).Count

$layout = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
if (Test-Path $layout) { Remove-Item $layout -Force }
$conf = Join-Path $env:APPDATA 'QuasarRadio\appearance.json'
Set-Content -Path $conf -Value ('{"theme":"' + $Theme + '"}') -Encoding UTF8

$script:proc = $null

try {
  L ('TEMA=' + $Theme)

  # ================================ SESION 1: medicion ================================
  New-Sandbox $FL 90.0
  $s1 = Start-Consola 'sesion 1'
  $script:proc = $s1.Proc
  $con = $s1.ConEl
  $win = $s1.Con
  $wr = RectOf $con

  # ---- G1: los cuatro medidores y los dos botones de cada deck existen y se leen desde fuera
  foreach ($id in @('DeckAVu', 'DeckBVu', 'MasterLeftVu', 'MasterRightVu')) {
    $e = ById $con $id
    if ($null -eq $e) { L ('G1_medidor [' + $id + '] : NO ESTA EN EL ARBOL'); continue }
    $r = RectOf $e
    $lee = Vu-Value $con $id
    L ('G1_medidor [' + $id + '] class=' + $e.Current.ClassName + ' name=' + $e.Current.Name +
       ' rect=' + $r.X + ',' + $r.Y + ' ' + $r.W + 'x' + $r.H + ' valor_al_parar=' + $lee +
       ' legible_por_uia=' + ($null -ne $lee))
  }
  foreach ($id in @('DeckARepeatButton', 'DeckAPreListenButton', 'DeckBRepeatButton', 'DeckBPreListenButton')) {
    $e = ById $con $id
    if ($null -eq $e) { L ('G1_boton [' + $id + '] : NO ESTA EN EL ARBOL'); continue }
    $r = RectOf $e
    L ('G1_boton [' + $id + '] name=' + $e.Current.Name + ' rect=' + $r.X + ',' + $r.Y + ' ' + $r.W + 'x' + $r.H +
       ' habilitado=' + $e.Current.IsEnabled)
  }
  # La franja tiene que estar DONDE dice el fundador: debajo del VU del deck, dentro de la ventana.
  $vuA = ById $con 'DeckAVu'; $repA = ById $con 'DeckARepeatButton'; $pflA = ById $con 'DeckAPreListenButton'
  if ($null -ne $vuA -and $null -ne $repA -and $null -ne $pflA) {
    $rv = RectOf $vuA; $rb = RectOf $repA; $rp = RectOf $pflA
    $debajo = (($rb.Y -ge ($rv.Y + $rv.H - 2)) -and ($rp.Y -ge ($rv.Y + $rv.H - 2)))
    $dentro = (($rp.X + $rp.W -le $wr.X + $wr.W) -and ($rb.Y + $rb.H -le $wr.Y + $wr.H + 2))
    $lado   = ([Math]::Abs($rb.X - $rp.X) -gt 20)
    L ('G1_franja_debajo_del_vu=' + $debajo + ' | dentro_de_la_ventana=' + $dentro + ' | botones_lado_a_lado=' + $lado)
  }
  $leds = @($con.FindAll($Scope::Descendants, (CTCond $CT::Custom)) | Where-Object { $_.Current.ClassName -match 'LedVuMeter' })
  L ('G1_ledvu_en_la_consola=' + $leds.Count + ' (esperados 4: deck A, deck B, master L, master R)')

  # ---- G2: con la emisora parada NO hay aviso (el detector viejo disparaba con la consola vacia)
  Start-Sleep -Seconds 2
  $st0 = Txt (ById $con 'SilenceStateText')
  $sec0 = Txt (ById $con 'SilenceAlertSeconds')
  L ('G2_estado_del_detector_al_parar=' + $st0 + ' | aviso_fijo=' + (Txt (ById $con 'SilenceAlertText')) +
     ' | segundos=[' + $sec0 + ']')
  L ('G2_sin_aviso_con_la_consola_parada=' + (($st0 -eq 'SIN AIRE') -and ($sec0 -eq '')))
  $r0 = Describe-Vu $con 'MasterLeftVu' 'G2_master_parada' 6 150
  L ('G2_master_sin_aire_en_cero=' + ($r0.max -le 1.0))
  L ('G2_lectura_de_salida=' + (Txt (ById $con 'MasterLevelReadout')))

  # ---- aire: master a 0 antes de pulsar PLAY; cada ventanass de medida sube su propio fader
  $vol = One $con 'Volumen master' $CT::Slider
  if ($null -eq $vol) { throw 'no esta el slider de volumen master' }
  Set-Range $vol 0
  Start-Sleep -Milliseconds 300
  L ('AIRE_master_preparado=' + [Math]::Round($vol.GetCurrentPattern($RVP::Pattern).Current.Value, 2))
  $play = ById $con 'MasterPlayButton'
  if ($null -eq $play -or -not $play.Current.IsEnabled) { throw 'el PLAY de la consola no esta habilitado con la cola cargada' }
  Invoke-El $play
  $msAire = Wait-Until { (Test-Path $np) -and ((Get-Content $np -Raw) -match 'Larga') } 20000
  if ($msAire -lt 0) { throw 'la pista larga nunca salio al aire; sin emision no hay nada que medir' }
  L ('AIRE_al_aire_en_ms=' + $msAire + ' | nowplaying=' + (Get-Content $np -Raw).Trim())
  # C2 (efecto lateral de UI-7): el pie de la consola se enlaza al reenvio de MainViewModel, y ese
  # reenvio no reavisaba cuando cambiaba el sub-ViewModel. Sin el puente, el pie se queda con la
  # frase del arranque y el operador no ve ni "AL AIRE". Se exige aqui porque es lo que el operador
  # mira para saber si lo que acaba de pulsar hizo algo.
  $msPie = Wait-Until { ((Txt (ById $con 'ConsoleStatusText')) -match 'AL AIRE') } 12000
  L ('C2_el_pie_de_la_consola_refleja_el_aire=' + ($msPie -ge 0) + ' (a los ' + $msPie + ' ms) | pie=' +
     (Txt (ById $con 'ConsoleStatusText')))

  # ---- M1: con el fader arriba (0 dB) se mueven el VU del deck Y los dos de salida
  Set-Range $vol 1.0
  Start-Sleep -Milliseconds 500
  $dA = Describe-Vu $con 'DeckAVu' 'M1_master_arriba' 12 150
  $mL = Describe-Vu $con 'MasterLeftVu' 'M1_master_arriba' 12 150
  $mR = Describe-Vu $con 'MasterRightVu' 'M1_master_arriba' 12 150
  $dB = Describe-Vu $con 'DeckBVu' 'M1_master_arriba' 5 150
  L ('M1_lectura_de_salida=' + (Txt (ById $con 'MasterLevelReadout')))
  $bar1 = Bar-Value $con 'SilenceLevelBar'
  L ('M1_barra_del_detector_con_nivel=' + ($null -ne $bar1) + ' | valor_lineal=' + $bar1 + ' | estado=' + (Txt (ById $con 'SilenceStateText')))
  $lee1 = Txt (ById $con 'MasterLevelReadout')
  L ('M1_lectura_con_cifras_de_decibelios=' + (($lee1 -match 'SALIDA') -and ($lee1 -match 'dB')))
  L ('M1_deck_A_mide_senal=' + ($dA.max -gt 5) + ' | master_L_mide_senal=' + ($mL.max -gt 5) +
     ' | master_R_mide_senal=' + ($mR.max -gt 5) + ' (el WAV es monofonico: L y R van iguales; que los tres SE MOVERIAN con el fader lo demuestran M1b, M2 y M3)')
  L ('M1_deck_B_en_cero_sin_sonar=' + ($dB.max -le 2))
  Crop-Element $win $con 'DeckAVu' 26 96 620 ($Tag + '_deckA_franja')
  CapWindow $win ($Tag + '_con_aire_master_arriba')

  # ---- M1b: el de salida ESCALA con el fader; el del deck no (post-fader contra pre-fader)
  Set-Range $vol 0.3
  Start-Sleep -Milliseconds 800
  $dA3 = Describe-Vu $con 'DeckAVu' 'M1b_master_al_30' 9 150
  $mL3 = Describe-Vu $con 'MasterLeftVu' 'M1b_master_al_30' 9 150
  $ratio = -1; if ($mL.max -gt 0) { $ratio = [Math]::Round($mL3.max / $mL.max, 2) }
  # Se compara el MINIMO, no el maximo: con la herramienta de UIA y el PrintWindow comiendo el hilo
  # de UI, la toma previa al fader del deck da algun pico suelto (buffer underrun del WaveOut, se
  # ve como max=100 cuando el resto de la serie esta a 76). El pico no es efecto del fader: lo que
  # tiene que demostrarse es que el valor TIPO del medidor de canal no se mueve al bajar el master.
  $dARatio = -1; if ($dA.min -gt 0) { $dARatio = [Math]::Round($dA3.min / $dA.min, 2) }
  L ('M1b_master_baja_con_el_fader=' + (($ratio -gt 0.05) -and ($ratio -lt 0.65)) + ' | ratio_nivel=' + $ratio + ' (fader 0,30 => ~0,30)')
  L ('M1b_deck_no_baja_con_el_fader=' + (($dARatio -gt 0.85) -and ($dARatio -lt 1.15)) + ' | ratio_deck=' + $dARatio +
     ' (min ' + $dA.min + ' -> ' + $dA3.min + '; max ' + $dA.max + ' -> ' + $dA3.max + ', la toma del deck es pre-fader)')
  $vigila = Txt (ById $con 'SilenceStateText')
  L ('M1b_aun_vigilando_sin_aviso=' + ($vigila -eq 'VIGILANDO') + ' | estado=' + $vigila +
     ' (0,30 x 0,27 = 0,08, por encima del umbral de -26 dB)')

  # ---- M2: master a 0 CON el aire sonando => la salida se va a cero y el detector dispara
  Set-Range $vol 0
  $msAlarma = Wait-Until { (Txt (ById $con 'SilenceStateText')) -eq 'SILENCIO DETECTADO' } 15000
  L ('M2_dispara_en_ms=' + $msAlarma + ' (umbral -26 dB, 2 s minimos: la ventana del historial son 12 lecturas de 100 ms)')
  L ('M2_dispara_dentro_de_lo_esperado=' + (($msAlarma -ge 1500) -and ($msAlarma -le 15000)))
  $dA0 = Describe-Vu $con 'DeckAVu' 'M2_master_a_cero' 7 150
  $mL0 = Describe-Vu $con 'MasterLeftVu' 'M2_master_a_cero' 7 150
  $mR0 = Describe-Vu $con 'MasterRightVu' 'M2_master_a_cero' 7 150
  L ('M2_deck_sigue_vivo=' + ($dA0.max -gt 5) + ' | salida_en_cero=' + (($mL0.max -lt 1) -and ($mR0.max -lt 1)) +
     ' (con el master bajado el VU de salida NO pinta tope: ese era el bug que reporto el fundador)')
  L ('M2_lectura_de_salida=' + (Txt (ById $con 'MasterLevelReadout')))
  $bar0 = Bar-Value $con 'SilenceLevelBar'
  L ('M2_barra_del_detector_baja_al_umbral=' + (($null -ne $bar0) -and ($bar0 -lt 0.05)) + ' | valor_lineal=' + $bar0 +
     ' (el umbral del detector es 0,05 = -26 dB)')

  # El aviso FIJO: dos lecturas a 3 s, la segunda sigue en pie y con mas segundos. Antes el aviso
  # se apagaba solo a los 2 s aunque el aire siguiera mudo.
  $av1 = Txt (ById $con 'SilenceAlertText'); $s1a = Txt (ById $con 'SilenceAlertSeconds')
  Start-Sleep -Seconds 3
  $av2 = Txt (ById $con 'SilenceAlertText'); $s2a = Txt (ById $con 'SilenceAlertSeconds')
  $n1 = -1.0; $n2 = -1.0
  if ($s1a -match '([0-9]+[.,][0-9]+)') { $n1 = [double]($matches[1] -replace ',', '.') }
  if ($s2a -match '([0-9]+[.,][0-9]+)') { $n2 = [double]($matches[1] -replace ',', '.') }
  $fijo = (($av1 -match 'SILENCIO DETECTADO') -and ($av2 -match 'SILENCIO DETECTADO'))
  L ('M2_aviso_fijo_en_el_pie=' + $fijo + ' | ' + $av1 + ' [' + $s1a + '] -> ' + $av2 + ' [' + $s2a + ']')
  L ('M2_segundos_acumulados=' + (($n2 -gt $n1) -and ($n2 -ge 3)) + ' | ' + $n1 + ' s -> ' + $n2 + ' s (cuenta, no parpadeo)')
  Crop-Element $win $con 'SilenceAlertText' 16 44 700 ($Tag + '_pie_aviso_silencio')
  CapWindow $win ($Tag + '_silencio_detectado')

  # ---- M3: vuelve el nivel => el aviso lo apaga la senal, no un temporizador
  Set-Range $vol 1.0
  $msClear = Wait-Until { (Txt (ById $con 'SilenceStateText')) -eq 'VIGILANDO' } 10000
  L ('M3_el_aviso_se_apaga_al_volver_el_nivel_en_ms=' + $msClear + ' | estado=' + (Txt (ById $con 'SilenceStateText')) +
     ' | segundos=[' + (Txt (ById $con 'SilenceAlertSeconds')) + ']')
  $mL1 = Describe-Vu $con 'MasterLeftVu' 'M3_master_de_nuevo_arriba' 7 150
  L ('M3_la_salida_vuelve_a_moverse=' + ($mL1.max -gt 5))

  # ---- P1: REPETIR enciende y apaga el estado del deck en el aire (el bucle: sesion 2)
  $aireTxt = Txt (ById $con 'ConsoleStatusText')
  $deckEnAire = 'A'; if ($aireTxt -match 'DECK B') { $deckEnAire = 'B' }
  L ('P1_deck_en_el_aire=' + $deckEnAire + ' (leido del estado de la consola, no supuesto)')
  $btnRep = ById $con ('Deck' + $deckEnAire + 'RepeatButton')
  if ($null -eq $btnRep) { throw ('no esta el boton REPETIR del deck ' + $deckEnAire) }
  Invoke-El $btnRep
  Start-Sleep -Milliseconds 700
  $est1 = Txt (ById $con 'ConsoleStatusText')
  L ('P1_repetir_al_pulsar=' + ($est1 -match 'repitiendo') + ' | estado=' + $est1)
  $btnRep2 = ById $con ('Deck' + $deckEnAire + 'RepeatButton')
  Invoke-El $btnRep2
  Start-Sleep -Milliseconds 700
  $est2 = Txt (ById $con 'ConsoleStatusText')
  L ('P1_repetir_al_quitarlo=' + ($est2 -match 'rotacion normal') + ' | estado=' + $est2)

  # ---- P2: pre-escucha (PFL) del deck en el aire. Se apaga a los ~0,5 s: la pre-escucha sale por
  # su propia salida, no por el master, asi que ese medio segundo si se oye en el monitor.
  $btnPfl = ById $con ('Deck' + $deckEnAire + 'PreListenButton')
  if ($null -eq $btnPfl) { throw ('no esta el boton PRE-ESCUCHA del deck ' + $deckEnAire) }
  Invoke-El $btnPfl
  $msPfl = Wait-Until { ((Txt (ById $con 'ConsoleStatusText')) -match ('Pre-escucha deck ' + $deckEnAire)) } 8000
  $pflTxt = Txt (ById $con 'ConsoleStatusText')
  L ('P2_pre_escucha_on_ms=' + $msPfl + ' | estado=' + $pflTxt)
  L ('P2_dice_que_no_sale_al_aire=' + ($pflTxt -match 'no sale al aire'))
  Start-Sleep -Milliseconds 500
  Invoke-El (ById $con ('Deck' + $deckEnAire + 'PreListenButton'))
  $msPflOff = Wait-Until { ((Txt (ById $con 'ConsoleStatusText')) -match ('Pre-escucha del deck ' + $deckEnAire + ' apagada')) } 8000
  L ('P2_pre_escucha_off_ms=' + $msPflOff + ' | estado=' + (Txt (ById $con 'ConsoleStatusText')))

  # ---- S1: al PARAR la salida queda en cero de inmediato y el detector vuelve a SIN AIRE
  Invoke-El (ById $con 'MasterStopButton')
  Start-Sleep -Milliseconds 800
  $sd = Describe-Vu $con ('Deck' + $deckEnAire + 'Vu') 'S1_tras_parar' 5 150
  $sm = Describe-Vu $con 'MasterLeftVu' 'S1_tras_parar' 5 150
  L ('S1_deck_en_cero=' + ($sd.max -le 1) + ' | salida_en_cero=' + ($sm.max -le 1) +
     ' (Bus.Forget al parar, sin esperar la ventana de 250 ms del bus)')
  $msSin = Wait-Until { (Txt (ById $con 'SilenceStateText')) -eq 'SIN AIRE' } 8000
  L ('S1_detector_vuelve_a_sin_aire_ms=' + $msSin + ' | estado=' + (Txt (ById $con 'SilenceStateText')))

  # ---- P3: sin pista cargada la pre-escucha lo dice en la consola en vez de quedarse muda
  Invoke-El (ById $con ('Deck' + $deckEnAire + 'PreListenButton'))
  Start-Sleep -Milliseconds 800
  $p3 = Txt (ById $con 'ConsoleStatusText')
  L ('P3_pre_escucha_sin_pista=' + ($p3 -match 'no tiene ninguna pista cargada') + ' | estado=' + $p3)
  CapWindow $win ($Tag + '_consola_sesion1_final')

  $s1.Proc.Kill(); Start-Sleep -Seconds 3
  $script:proc = $null

  # ================================ SESION 2: el bucle ================================
  # Aqui el master NO se sube en toda la prueba: el REPETIR se demuestra por nowplaying.txt, por el
  # log y por el contador de la ficha, sin que suene nada en la cabina.
  New-Sandbox $FR 6.0
  $s2 = Start-Consola 'sesion 2'
  $script:proc = $s2.Proc
  $con2 = $s2.ConEl
  $win2 = $s2.Con

  $vol2 = One $con2 'Volumen master' $CT::Slider
  Set-Range $vol2 0
  Start-Sleep -Milliseconds 300
  $t0 = Get-Date
  Invoke-El (ById $con2 'MasterPlayButton')
  $msA2 = Wait-Until { (Test-Path $np) -and ((Get-Content $np -Raw) -match 'Bucle') } 20000
  if ($msA2 -lt 0) { throw 'la pista de 6 s nunca salio al aire' }
  $txt2 = Txt (ById $con2 'ConsoleStatusText')
  $dk = 'A'; if ($txt2 -match 'DECK B') { $dk = 'B' }
  L ('R0_al_aire_en_ms=' + $msA2 + ' | deck=' + $dk + ' | estado=' + $txt2)

  $b = ById $con2 ('Deck' + $dk + 'RepeatButton')
  if ($null -eq $b) { throw ('no esta el boton REPETIR del deck ' + $dk) }
  Invoke-El $b
  Start-Sleep -Milliseconds 600
  $stRep = Txt (ById $con2 'ConsoleStatusText')
  L ('R1_repetir_puesto=' + ($stRep -match 'repitiendo') + ' | a los ' +
     [Math]::Round(((Get-Date) - $t0).TotalSeconds, 1) + ' s de una pista de 6 s | estado=' + $stRep)
  $vc = Meta-PlayCount $FR
  L ('R1_ficha_contador_al_empezar=' + $vc + ' (-1 = aun sin ficha en el JSON del sandbox)')

  # 15 s > 2 vueltas de 6 s. La pista tiene que SEGUIR en el aire: sin REPETIR se habria acabado a
  # los 6 s y el aire se cierra solo (el arreglo de UI-4 paso B sigue valiendo).
  Start-Sleep -Seconds 15
  $seg1 = [Math]::Round(((Get-Date) - $t0).TotalSeconds, 1)
  $np1 = (Get-Content $np -Raw).Trim()
  $sigue = ($np1 -match 'Bucle')
  $vuDeck = Describe-Vu $con2 ('Deck' + $dk + 'Vu') 'R2_durante_el_bucle' 5 150
  $vuMas = Describe-Vu $con2 'MasterLeftVu' 'R2_durante_el_bucle' 5 150
  L ('R2_sigue_el_mismo_tema_a_los_' + $seg1 + '_s=' + $sigue + ' | nowplaying=' + $np1 + ' (pista de 6 s: habria acabado 2 veces)')
  L ('R2_el_deck_sigue_vivo=' + ($vuDeck.max -gt 5) + ' | la_salida_sigue_en_cero=' + ($vuMas.max -lt 1) +
     ' (el bucle no abre el master)')
  $vc2 = Meta-PlayCount $FR
  $noRepite = 'no comparable (sin ficha)'; if (($vc -ge 0) -and ($vc2 -ge 0)) { $noRepite = ($vc2 -eq $vc) }
  L ('R2_repetir_no_vuelve_a_atribuir=' + $noRepite + ' | contador=' + $vc + ' -> ' + $vc2 + ' tras 2 vueltas')

  # El aviso de silencio se mantiene fijo durante todo el bucle: master a 0 = aire mudo de verdad.
  $stB = Txt (ById $con2 'SilenceStateText')
  $sB  = Txt (ById $con2 'SilenceAlertSeconds')
  $nB = -1.0; if ($sB -match '([0-9]+[.,][0-9]+)') { $nB = [double]($matches[1] -replace ',', '.') }
  L ('R3_aviso_fijo_durante_el_bucle=' + (($stB -eq 'SILENCIO DETECTADO') -and ($nB -ge 8)) + ' | ' + $stB + ' [' + $sB + ']')
  CapWindow $win2 ($Tag + '_bucle_con_aviso_fijo')

  # Fuera el REPETIR: la pista termina esta vez y el aire se cierra por su cuenta.
  Invoke-El (ById $con2 ('Deck' + $dk + 'RepeatButton'))
  Start-Sleep -Milliseconds 600
  $msFin = Wait-Until { (Test-Path $np) -and (-not ((Get-Content $np -Raw) -match 'Bucle')) } 25000
  $npFin = '(sin archivo)'; if (Test-Path $np) { $npFin = (Get-Content $np -Raw).Trim() }
  L ('R4_al_quitar_el_repetir_termina_en_ms=' + $msFin + ' | nowplaying=' + $npFin)
  L ('R4_el_aire_se_cerro_solo=' + (($msFin -ge 0) -and ($npFin -match 'Quasar Radio Pro')))
  $vc3 = Meta-PlayCount $FR
  L ('R4_contador_final=' + $vc3 + ' | emitida_al_menos_una_vez=' + ($vc3 -ge 1))
  $msSin2 = Wait-Until { (Txt (ById $con2 'SilenceStateText')) -eq 'SIN AIRE' } 8000
  L ('R4_detector_vuelve_a_sin_aire_al_terminar_ms=' + $msSin2 + ' | estado=' + (Txt (ById $con2 'SilenceStateText')))
  $vFin = Describe-Vu $con2 'MasterLeftVu' 'R4_salida_al_terminar' 4 150
  L ('R4_la_salida_queda_en_cero=' + ($vFin.max -le 1))

  # ---- SRC: la decoracion que el fundador pidio quitar ya no esta en el XAML
  $xaml = 'C:\Users\CY-KILL\Desktop\Programación y Datanálisis\quasar_radio\MainWindow.xaml'
  $src = Get-Content $xaml -Raw
  $spectrum = ([regex]::Matches($src, 'Deck[AB]_Spectrum')).Count
  $vuBarra  = ([regex]::Matches($src, 'Deck[AB]_VuLevel')).Count
  $story    = ([regex]::Matches($src, 'BeginStoryboard')).Count
  L ('SRC_itemscontrol_de_espectro=' + $spectrum + ' | barras_animadas_en_xaml=' + $story +
     ' | vu_falso_del_deck_enlazado_a_ProgressBar=' + ([regex]::Matches($src, 'ProgressBar[^>]*Deck[AB]_VuLevel')).Count +
     ' | Deck[AB]_VuLevel_mencionado=' + $vuBarra + ' (2: solo los dos LedVuMeter)')
  L ('SRC_botones_repetir=' + ([regex]::Matches($src, 'Deck[AB]RepeatButton')).Count +
     ' | botones_pre_escucha=' + ([regex]::Matches($src, 'Deck[AB]PreListenButton')).Count)

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
    foreach ($m in @('Silencio prolongado en la salida', 'Aire recuperado tras el silencio detectado',
                     'Boton REPETIR del deck', 'REPETIR ', 'PFL deck', 'en REPETIR')) {
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

  $hashDespues = '(no existe)'
  if (Test-Path $metaReal) { $hashDespues = (Get-FileHash $metaReal -Algorithm MD5).Hash }
  $musicaDespues = @(Get-ChildItem (Join-Path $realBin 'musica') -File -ErrorAction SilentlyContinue).Count
  $qsrEnBin = @(Get-ChildItem $realBin -Recurse -Filter 'QSRMED*' -ErrorAction SilentlyContinue).Count
  $c1 = $hashAntes.Substring(0, [Math]::Min(12, $hashAntes.Length))
  $c2 = $hashDespues.Substring(0, [Math]::Min(12, $hashDespues.Length))
  L ('BIN_track_metadata_antes=' + $c1 + ' despues=' + $c2 + ' | intacto=' + ($hashAntes -eq $hashDespues))
  L ('BIN_musica_archivos=' + $musicaReal + ' -> ' + $musicaDespues + ' | sin_fixtures_QSRMED_en_bin=' + ($qsrEnBin -eq 0))

  $layoutAfter = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
  if (Test-Path $layoutAfter) { Remove-Item $layoutAfter -Force }
  if (-not $Keep -and (Test-Path $Sandbox)) { Remove-Item $Sandbox -Recurse -Force }
  else { L ('SANDBOX_CONSERVADO=' + $Sandbox) }

  $res = Join-Path $out ('uia_' + $Tag + '_medicion.txt')
  [IO.File]::WriteAllLines($res, $lines, (New-Object Text.UTF8Encoding($true)))
  Write-Host ('EVIDENCIA=' + $res)
}
