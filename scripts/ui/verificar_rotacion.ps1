param(
  [string]$Theme = 'Studio Dark Neon',
  [string]$Tag = 'ui4b',
  [string]$Sandbox = (Join-Path $env:TEMP 'qsr_rot_test'),
  [switch]$Keep
)

# UI-4 paso B: verificación en vivo de la rotación real, la persistencia de lo importado y la
# gestión de géneros (renombrar / quitar) en la ventana de Biblioteca.
#
# Se ejecuta SIEMPRE contra una librería de ensayo en %TEMP%, nunca contra la real. La librería
# de la emisora vive en bin/Debug/net10.0-windows (musica/, horarias/, ...) y no está en git: un
# error aquí la borraría sin copia. El exe arranca con -WorkingDirectory $Sandbox porque
# AudioDatabaseService escanea "musica", "jingles"... relativos al directorio actual y
# TrackMetadataService escribe track_metadata.json también relativo al directorio actual.
#
# Los archivos de ensayo llevan el prefijo QSRROT. Jamás HRS ni MIN: con esos nombres se
# pisaron 31 grabaciones reales de horarias/ (ver docs/UI_BASELINE.md).
#
# El master de salida se baja a 0 antes de pulsar PLAY: la atribución se comprueba con el aire
# realmente empezado (ahora suena, contador, rotación, estadísticas), pero en la cabina del
# fundador no tiene que oírse nada.

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if (-not ('QsrRot' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class RotWin { public IntPtr Hwnd; public string Title; public bool Visible; public string Rect; public int L; public int T; public int W; public int H; }
public static class QsrRot {
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
  [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
  // WPF pinta por hardware: PrintWindow sin PW_RENDERFULLCONTENT devolvía el cliente en negro,
  // y con la ventana tapada SetForegroundWindow falla desde un proceso de script (el mismo
  // truco que verificar_consola.ps1 y verificar_biblioteca.ps1).
  public static void Raise(IntPtr h) {
    ShowWindow(h, 5); BringWindowToTop(h);
    keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
    SetForegroundWindow(h);
  }
  public static void Close(IntPtr h) { SendMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); } // WM_CLOSE
  static int _target; static List<RotWin> _found;
  public static List<RotWin> Top(int pid) {
    _target = pid; _found = new List<RotWin>();
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == _target) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        var wi = new RotWin();
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

$AE   = [System.Windows.Automation.AutomationElement]
$CT   = [System.Windows.Automation.ControlType]
$Scope= [System.Windows.Automation.TreeScope]

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
# El texto de estado y los numeradores se buscan por AutomationId (x:Name en WPF), no por
# Nombre: su Nombre ES el valor, y buscarlos por una etiqueta fija devolvería la etiqueta.
function ById($root, $id) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
  return $root.FindFirst($Scope::Descendants, $c)
}
function Get-Rows($g) {
  if ($null -eq $g) { return @() }
  $r = @($g.FindAll($Scope::Children, (CTCond $CT::DataItem)))
  if ($r.Count -eq 0) { $r = @($g.FindAll($Scope::Descendants, (CTCond $CT::DataItem))) }
  return $r
}
function Row-Cells($row) {
  $parts = @()
  foreach ($t in $row.FindAll($Scope::Descendants, (CTCond $CT::Text))) { $parts += $t.Current.Name }
  return $parts
}
function Row-Text($row) { return ((Row-Cells $row) -join ' | ') }
function Get-Estado($win) {
  $e = ById $win 'StatusText'
  if ($null -eq $e) { return '(sin texto de estado)' }
  return $e.Current.Name
}
# Invoke sobre un boton apagado lanza el criptico "No se permite la operación en un elemento que
# no se puede habilitar" de UIA, que no dice que boton era ni en que paso estabamos, y la corrida
# se corta sin evidencia. Se comprueba aqui: si el mando llega apagado, el fallo tiene nombre.
function Invoke-El($e) {
  if ($null -eq $e) { throw 'Invoke-El: no hay elemento que invocar (ausente del arbol UIA)' }
  if (-not $e.Current.IsEnabled) { throw ('Invoke-El: el boton [' + (Txt $e) + '] esta apagado y aun se pidio pulsarlo') }
  [void]$e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Set-Value($e, $v) { [void]$e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($v) }
function Set-Range($e, $v) { [void]$e.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue($v) }
function Txt($e) { if ($null -eq $e) { return '(ausente)' } return $e.Current.Name }

function Wait-Until([scriptblock]$Test, [int]$Ms) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $Ms) {
    try { if (& $Test) { return [int]$sw.ElapsedMilliseconds } } catch { }
    Start-Sleep -Milliseconds 250
  }
  return -1
}

$exe = 'C:\Users\CY-KILL\Desktop\Programación y Datanálisis\quasar_radio\bin\Debug\net10.0-windows\quasar_radio.exe'
$logdir = Join-Path (Split-Path $exe) 'logs'
$out = Join-Path $env:TEMP 'qsr_ui'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$lines = New-Object Collections.ArrayList
function L($m) { $lines.Add($m) | Out-Null; Write-Host $m }

function CapWindow($win, $t) {
  if ($null -eq $win) { L "CAP $t : sin ventana"; return }
  $w = [int]$win.W; $h = [int]$win.H
  if ($w -lt 50 -or $h -lt 50) { L "CAP $t : rect inválida"; return }
  $bmp = New-Object Drawing.Bitmap($w, $h)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrRot]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 350
  $hdc = $g.GetHdc()
  $ok = [QsrRot]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)
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

$F1 = 'QSRROT Artista Uno - Pista 1.wav'   # 2 s: se emite entera (fin natural)
$F2 = 'QSRROT Artista Dos - Pista 2.wav'   # 2 s: llega con género "Renombrar" y 7 veces emitida
$F3 = 'QSRROT Artista Tres - Pista 3.wav'  # 6 s: se corta a los ~2 s (tiempo real de emisión)
$FJ = 'QSRROT Sintonia - Corto.wav'        # 1 s en jingles/
$FI = 'QSRROT Fuera de Carpeta - Tema.wav' # 2 s en importados/: LoadLibrary NO escanea esa carpeta

function New-Sandbox {
  if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'musica')     | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'jingles')    | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'importados') | Out-Null
  New-QsrWav (Join-Path $Sandbox ('musica\'     + $F1)) 2.0 240
  New-QsrWav (Join-Path $Sandbox ('musica\'     + $F2)) 2.0 320
  New-QsrWav (Join-Path $Sandbox ('musica\'     + $F3)) 6.0 400
  New-QsrWav (Join-Path $Sandbox ('jingles\'    + $FJ)) 1.0 660
  New-QsrWav (Join-Path $Sandbox ('importados\' + $FI)) 2.0 520

  # Las claves van en camelCase porque TrackMetadataService lee con PropertyNamingPolicy =
  # CamelCase: con PascalCase el JSON se deserializaría en vacío y la prueba no probaría nada.
  $semilla = @(
    [ordered]@{
      filePath   = (Join-Path $Sandbox ('musica\' + $F2))
      title      = 'Pista 2 sembrada'
      artist     = 'QSRROT Artista Dos'
      genre      = 'Renombrar'
      playCount  = 7
      lastPlayed = '2026-10-18T09:15:00'
    },
    [ordered]@{
      filePath   = (Join-Path $Sandbox ('importados\' + $FI))
      title      = 'Tema fuera de carpeta'
      artist     = 'QSRROT Importado'
      genre      = 'Import Verify'
      playCount  = 3
      lastPlayed = '2026-10-19T21:05:00'
    }
  )
  $semilla | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $Sandbox 'track_metadata.json') -Encoding UTF8
  L ('FIXTURES: 3 wav en musica/ (2s, 2s, 6s), 1 jingle, 1 wav en importados/ fuera del escaneo, y track_metadata.json sembrado -> ' + $Sandbox)
}

function Get-Meta {
  $p = Join-Path $Sandbox 'track_metadata.json'
  if (-not (Test-Path $p)) { return @() }
  return @(Get-Content $p -Raw | ConvertFrom-Json)
}
function Meta-By-File($meta, $file) {
  return @($meta | Where-Object { $_.FilePath -like ('*' + $file) }) | Select-Object -First 1
}

function Find-Window($pid_, $pattern) {
  return ([QsrRot]::Top($pid_) | Where-Object { $_.Visible -and $_.Title -like $pattern } | Select-Object -First 1)
}

# Arranca el programa con la librería de ensayo y devuelve consola + Biblioteca ya pintadas.
function Start-Qsr([string]$modo) {
  $p = Start-Process -FilePath $exe -WorkingDirectory $Sandbox -ArgumentList '--ui-check', 'Library' -PassThru
  Start-Sleep -Seconds 12
  $bib = Find-Window $p.Id '*Biblioteca*'
  $con = Find-Window $p.Id 'Quasar Radio Pro*'
  if ($null -eq $bib) { throw "sin ventana de Biblioteca en $modo" }
  if ($null -eq $con) { throw "sin consola en $modo" }
  $bibEl = $AE::FromHandle([IntPtr]$bib.Hwnd)
  $conEl = $AE::FromHandle([IntPtr]$con.Hwnd)
  # Centinela: la línea de estado es lo último que se publica. Los FindAll sobre un árbol que
  # todavía se está pintando devuelven solo la parte de arriba (coste documentado en las
  # pruebas anteriores), así que contar filas antes de tiempo sale lo que sea.
  $ms = Wait-Until { $null -ne (ById $bibEl 'StatusText') } 60000
  if ($ms -lt 0) { throw "Biblioteca sin centinela en $modo" }
  $ms2 = Wait-Until { $null -ne (One $conEl 'abre su propia ventana' $CT::Text) } 60000
  L ("ARRANQUE[$modo]: biblioteca lista en ${ms} ms, consola en ${ms2} ms")
  return [pscustomobject]@{ Proc = $p; Bib = $bib; Con = $con; BibEl = $bibEl; ConEl = $conEl }
}

$pre = @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)
if ($pre.Count -gt 0) { throw "Ya hay $($pre.Count) instancia(s) abiertas; abortando para no tocar la emisión." }

$layout = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
if (Test-Path $layout) { Remove-Item $layout -Force }
New-Sandbox
Set-Content -Path (Join-Path $env:APPDATA 'QuasarRadio\appearance.json') -Value ('{"theme":"' + $Theme + '"}') -Encoding UTF8

$script:proc = $null
try {
  # ==================================================================== SESIÓN 1
  $s1 = Start-Qsr 'sesión 1'
  $script:proc = $s1.Proc
  $win = $s1.BibEl
  $con = $s1.ConEl

  # ---- R1: las dos ventanas por las que pasa el paso B
  L ('R1_biblioteca=' + $s1.Bib.Rect + ' | consola=' + $s1.Con.Rect)
  L ('R1_estado_arranque=' + (Get-Estado $win))

  # ---- R2: persistencia de lo importado (el escaneo no ve importados/) y una sola fila por
  # archivo. La siembra escribe Pista 2 con ruta ABSOLUTA y el escaneo la guarda relativa: si el
  # cruzado de fichas comparara las cadenas tal cual, aqui saldrian 6 filas (la pista duplicada
  # con dos contadores), que es exactamente el fallo que se arreglo en TrackMetadataService.
  $grid = One $win 'Tabla de tracks' $CT::DataGrid
  $rows = Get-Rows $grid
  L ('R2_filas=' + $rows.Count + ' (esperadas 5: 3 de musica/ + 1 jingle + 1 importado recuperado del JSON, sin duplicar Pista 2)')
  $imp = $null
  foreach ($r in $rows) { if ((Row-Text $r) -match 'Fuera de Carpeta') { $imp = $r } }
  L ('R2_fila_importada_presente=' + ($null -ne $imp))
  if ($null -ne $imp) { L ('   IMPORTADA: ' + (Row-Text $imp)) }
  L ('R2_estado_restaura_importados=' + ((Get-Estado $win) -match 'importados recuperados'))

  # ---- R3: siembra de rotación desde el JSON (el motor ya no arranca en blanco)
  $rot = One $win '^Rotaci' $CT::Text
  L ('R3_rotacion=' + (Txt $rot))
  $tree = One $win 'categ' $CT::List
  $nodos = @($tree.FindAll($Scope::Children, (CTCond $CT::ListItem)))
  L ('R3_nodo_renombrar=' + (@($nodos | Where-Object { $_.Current.Name -match 'Renombrar' }).Count -eq 1) +
     ' | nodo_import_verify=' + (@($nodos | Where-Object { $_.Current.Name -match 'Import Verify' }).Count -eq 1))
  L ('   NODOS: ' + (($nodos | ForEach-Object { $_.Current.Name }) -join ' / '))

  # ---- R4: aire real cortado (6 s cortados a ~2 s) => una emisión y 2 s, no 6 s
  $vol = One $con 'Volumen master' $CT::Slider
  if ($null -eq $vol) { throw 'R4: no se encuentra el slider de master para silenciar' }
  Set-Range $vol 0
  Start-Sleep -Milliseconds 400
  $vr = $vol.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
  L ('R4_master_silenciado=' + [Math]::Round($vr.Current.Value, 2))

  function Select-Row($pat) {
    $rs = Get-Rows (One $win 'Tabla de tracks' $CT::DataGrid)
    foreach ($r in $rs) {
      if ((Row-Text $r) -match $pat) {
        $r.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 600
        return $r
      }
    }
    return $null
  }
  function Encolar($rotulo = 'R4') {
    $b = One $win 'Encolar los tracks seleccionados' $CT::Button
    if ($null -eq $b) { throw 'no está el botón ENCOLAR' }
    L ($rotulo + '_encolar_habilitado=' + $b.Current.IsEnabled)
    Invoke-El $b
    Start-Sleep -Seconds 1
  }
  function Cola-Console-Rows {
    # La Biblioteca se abre como ventana hija de la consola y UIA la cuelga dentro de su árbol:
    # un FindAll(Descendants, DataGrid) desde la consola devuelve PRIMERO la "Tabla de tracks" de
    # la Biblioteca (las 5 filas de la librería) y su "Vaciar". Por eso la cola de emisión se
    # localiza por AutomationId (x:Name="ConsoleQueueGrid"), que es único.
    $cg = ById $con 'ConsoleQueueGrid'
    if ($null -eq $cg) { throw 'no está la rejilla de emisión (ConsoleQueueGrid) en la consola' }
    return @(Get-Rows $cg).Count
  }
  # La consola llega con la cola armada desde la carpeta musica/ (MainViewModel hace
  # LoadDefaultMusicFolder al arrancar), asi que para que "lo que sale al aire" sea solo lo que
  # esta prueba encola hay que vaciar la cola y comprobar que se vacio de verdad.
  function Vaciar-Cola {
    $v = ById $con 'ClearQueueButton'
    if ($null -eq $v) { throw 'no esta el boton Vaciar de la rejilla de emision' }
    if (-not $v.Current.IsEnabled) {
      # El comando se deshabilita con la cola vacia (CanOperate && Playlist.Count > 0): si ya
      # esta vacia no hay que pulsar nada, y si no lo esta es que algo no se desmonto.
      $n0 = Cola-Console-Rows
      if ($n0 -eq 0) { return 0 }
      throw "Vaciar esta deshabilitado con $n0 filas todavia en la cola"
    }
    Invoke-El $v
    $ms = Wait-Until { (Cola-Console-Rows) -eq 0 } 8000
    $n = Cola-Console-Rows
    if ($n -ne 0) { throw "Vaciar no desmonto la cola de la consola (quedaron $n filas)" }
    return $ms
  }
  # nowplaying.txt se escribe en el directorio de trabajo (la carpeta de ensayo). Se borra antes
  # de cada PLAY: si no, la comprobacion del "ahora suena" puede contestar con lo que dej6 la
  # emision anterior y salir verdadera sin que haya sonado nada.
  $np = Join-Path $Sandbox 'nowplaying.txt'
  function Empezar-Aire($titulo) {
    if (Test-Path $np) { Remove-Item $np -Force }
    Invoke-El (ById $con 'MasterPlayButton')
    return (Wait-Until { (Test-Path $np) -and ((Get-Content $np -Raw) -match $titulo) } 15000)
  }

  $msCola = Vaciar-Cola
  L ('R4_cola_vaciada_en_ms=' + $msCola + ' | filas=' + (Cola-Console-Rows) +
     ' | estado_consola=' + (Txt (ById $con 'ConsoleStatusText')))

  $r3 = Select-Row 'Pista 3'
  if ($null -eq $r3) { throw 'R4: fila de Pista 3 no encontrada' }
  L ('   FILA ANTES DE EMITIR: ' + (Row-Text $r3))
  Encolar
  L ('R4_cola_tras_encolar_filas=' + (Cola-Console-Rows) + ' | estado=' + (Get-Estado $win))

  $play = ById $con 'MasterPlayButton'
  $stop = ById $con 'MasterStopButton'
  if ($null -eq $play -or $null -eq $stop) {
    throw ('R4: faltan los mandos maestros (play=' + ($null -ne $play) + ' stop=' + ($null -ne $stop) + ')')
  }
  $esperaPlay = Wait-Until { (ById $con 'MasterPlayButton').Current.IsEnabled } 8000
  $play = ById $con 'MasterPlayButton'
  L ('R4_play_habilitado=' + $play.Current.IsEnabled + ' (tras ' + $esperaPlay + ' ms esperando a la cola)')
  $t0 = Get-Date
  $msNp = Empezar-Aire 'Pista 3'
  if ($msNp -lt 0) { throw 'R4: nowplaying.txt nunca dijo Pista 3; la atribucion no esta corriendo' }
  L ('R4_al_aire_en_ms=' + $msNp + ' | nowplaying=' + (Get-Content $np -Raw).Trim())
  Start-Sleep -Milliseconds 2200
  $corte = ((Get-Date) - $t0).TotalSeconds
  # El $stop cogido antes del PLAY ya no vale: al arrancar el motor WPF sustituye el peer de UIA
  # del boton y el elemento guardado se queda invocado-en-vacio («No se permite la operación en un
  # elemento que no se puede habilitar»). Se vuelve a buscar por AutomationId y se espera a que
  # este habilitado, igual que se hizo con el PLAY.
  $msStop = Wait-Until { $s = ById $con 'MasterStopButton'; (($null -ne $s) -and $s.Current.IsEnabled) } 8000
  $stop = ById $con 'MasterStopButton'
  if ($null -eq $stop) { throw 'R4: el STOP de la consola desaparecio del arbol' }
  L ('R4_stop_habilitado=' + $stop.Current.IsEnabled + ' (tras ' + $msStop + ' ms buscando el boton vivo)')
  Invoke-El $stop
  Start-Sleep -Seconds 2
  L ('R4_se_corto_a_los_s=' + [Math]::Round($corte, 1))

  $m = Get-Meta
  $p3 = Meta-By-File $m $F3
  L ('R4_vegas_Pista3=' + $p3.PlayCount + ' | emitido=' + $p3.LastPlayed)
  L ('R4_aire_persistido_en_json=' + (($p3.PlayCount -ge 1) -and ($null -ne $p3.LastPlayed)))
  $r3b = $null
  foreach ($r in Get-Rows $grid) { if ((Row-Text $r) -match 'Pista 3') { $r3b = $r } }
  if ($null -ne $r3b) { L ('   FILA TRAS EMITIR: ' + (Row-Text $r3b)) }

  # ---- R5: fin natural (pista de 2 s) => la emision se cierra sola y "ahora suena" se limpia
  # Costaba antes de este paso: en AUTO, al acabarse la pista sin siguiente, el motor llamaba a
  # TriggerCrossfadeToNext, no habia a quien cruzar, y PlaybackFinished no se disparaba nunca (la
  # consola se quedaba en AL AIRE con el deck clavado y la pista sin cerrar en estadisticas).
  L ('R5_cola_vaciada_en_ms=' + (Vaciar-Cola) + ' | despues=' + (Cola-Console-Rows))
  $r1 = Select-Row 'Pista 1'
  if ($null -eq $r1) { throw 'R5: fila de Pista 1 no encontrada' }
  Encolar 'R5'
  $msNp1 = Empezar-Aire 'Pista 1'
  L ('R5_al_aire_en_ms=' + $msNp1)
  $msFin = Wait-Until { (Test-Path $np) -and ((Get-Content $np -Raw) -match 'Quasar Radio Pro') } 25000
  L ('R5_fin_natural_cerrado_en_ms=' + $msFin + ' | nowplaying=' + (Get-Content $np -Raw).Trim())
  $m = Get-Meta
  $p1 = Meta-By-File $m $F1
  L ('R5_vegas_Pista1=' + $p1.PlayCount + ' (si fuera 2, la misma emision se apunto dos veces)')

  # ---- R6: las cuatro cifras de Reportes salen de esa atribución
  $abrir = One $con 'Abrir Reportes' $CT::Button
  if ($null -eq $abrir) { throw 'R6: no está el botón Abrir Reportes en la consola' }
  Invoke-El $abrir
  Start-Sleep -Seconds 4
  $rep = Find-Window $script:proc.Id '*Reportes*'
  if ($null -eq $rep) { throw 'R6: la ventana de Reportes no se abrió' }
  $repEl = $AE::FromHandle([IntPtr]$rep.Hwnd)
  L ('R6_tracks_en_sesion=' + (Txt (ById $repEl 'StatSessionTracks')) +
     ' | tiempo_total=' + (Txt (ById $repEl 'StatSessionTime')) +
     ' | unicos=' + (Txt (ById $repEl 'StatUniqueTracks')) +
     ' | registros=' + (Txt (ById $repEl 'StatTotalRecords')))
  $secs = -1
  if ((Txt (ById $repEl 'StatSessionTime')) -match '^(\d+):(\d+):(\d+)$') {
    $secs = [int]$matches[1] * 3600 + [int]$matches[2] * 60 + [int]$matches[3]
  }
  L ('R6_tiempo_coherente_con_el_corte=' + ($secs -ge 3 -and $secs -le 6) + " (${secs} s; los 6 s de la pista cortada nunca deben entrar)")
  L ('R6_dos_aire_dos_registros=' + ((Txt (ById $repEl 'StatSessionTracks')) -eq '2' -and (Txt (ById $repEl 'StatTotalRecords')) -eq '2'))
  [void][QsrRot]::Close([IntPtr]$rep.Hwnd)
  Start-Sleep -Seconds 1

  # ---- R6b: reproducir NO es editar (la regresión del género pisado)
  # Coste real de este paso B: con una pista de la emisora sonando de verdad, la atribución
  # volcaba los dieciocho campos del track de la cola sobre la ficha guardada y borraba lo que el
  # operador habia escrito a mano. Paso en falso que se vio en la libreria del fundador: el genero
  # «Pop» de una cumbia real se convirtio en «General» sin abrir Biblioteca.
  #
  # Se ataca por fuera del programa, que es la unica forma de que la prueba muerda: ya cargada la
  # libreria, se pisan en el JSON tres campos editoriales de la ficha del importado (comentario,
  # prioridad y cue in). El objeto que vive en memoria NO los conoce, asi que cualquier guardado
  # que vuelque el track entero se lleva el centinela por delante. Despues se emite la pista y se
  # exige que el centinela siga donde estaba y que solo playCount/lastPlayed se hayan movido.
  $rutaMeta = Join-Path $Sandbox 'track_metadata.json'
  $marca = 'centinela R6b ' + (Get-Date -Format 'HHmmss')
  $metaI = Get-Content $rutaMeta -Raw | ConvertFrom-Json
  foreach ($it in @($metaI | Where-Object { $_.FilePath -like ('*' + $FI) })) {
    $it | Add-Member -NotePropertyName comment -NotePropertyValue $marca -Force
    $it | Add-Member -NotePropertyName priority -NotePropertyValue 9 -Force
    $it | Add-Member -NotePropertyName cueIn -NotePropertyValue 1.25 -Force
  }
  @($metaI) | ConvertTo-Json -Depth 4 | Set-Content -Path $rutaMeta -Encoding UTF8
  $pisa = Meta-By-File (Get-Meta) $FI
  L ('R6b_centinela_sembrado=' + ($pisa.Comment -eq $marca) + ' | prioridad=' + $pisa.Priority +
     ' | cueIn=' + $pisa.CueIn + ' | fichas_de_esa_pista=' +
     (@(Get-Meta | Where-Object { $_.FilePath -like ('*' + $FI) }).Count))

  L ('R6b_cola_vaciada_en_ms=' + (Vaciar-Cola))
  $ri = Select-Row 'Fuera de Carpeta'
  if ($null -eq $ri) { throw 'R6b: la fila del importado fuera de carpeta no esta en la rejilla' }
  Encolar 'R6b'
  # Por el helper Meta-By-File y forzado a [int]: con el pipeline suelto la variable salía con las
  # cuatro cifras de las cuatro fichas y la comparación del contador no significaba nada.
  $vc = [int](Meta-By-File (Get-Meta) $FI).PlayCount
  $msAire = Empezar-Aire 'fuera de carpeta'
  if ($msAire -lt 0) { throw 'R6b: el importado nunca salio al aire; sin emision no hay nada que comprobar' }
  $msApunte = Wait-Until { ([int](Meta-By-File (Get-Meta) $FI).PlayCount) -gt $vc } 20000
  Start-Sleep -Seconds 2
  $pisa2 = Meta-By-File (Get-Meta) $FI
  $fichas = @(Get-Meta | Where-Object { $_.FilePath -like ('*' + $FI) }).Count
  $entera = (($pisa2.Comment -eq $marca) -and ($pisa2.Priority -eq 9) -and ([Math]::Round($pisa2.CueIn, 2) -eq 1.25))
  $despues = [int](Meta-By-File (Get-Meta) $FI).PlayCount
  L ('R6b_al_aire_en_ms=' + $msAire + ' | contador=' + $vc + ' -> ' + $despues +
     ' | apuntado=' + ($despues -gt $vc) + ' (a los ' + $msApunte + ' ms)')
  L ('R6b_ficha_editorial_intacta=' + $entera + ' | comentario=' + $pisa2.Comment +
     ' | prioridad=' + $pisa2.Priority + ' | cueIn=' + $pisa2.CueIn)
  L ('R6b_sin_duplicar_la_ficha=' + ($fichas -eq 1) + ' | genero_intacto=' + ($pisa2.Genre -eq 'Import Verify'))

  # ---- R7: renombrar el género del nodo elegido
  [void][QsrRot]::Close([IntPtr]$rep.Hwnd)
  Start-Sleep -Seconds 1
  # El árbol de géneros virtualiza sus filas (H21) y WPF sustituye el peer de UIA cuando cambia el
  # estado de la ventana (H26). Un nodo cogido por encima del pliegue, o cogido antes de tiempo,
  # petaba al seleccionar con «No se permite la operación en un elemento que no se puede
  # habilitar», que es justo lo que le pasó a R8 al cambiar el orden del árbol tras la emisión de
  # R6b. Estos tres helpers son de la herramienta, no de la app: buscar, bajar y volver a coger.
  function Nodos-Actual {
    $t = One $win 'categ' $CT::List
    return @($t.FindAll($Scope::Children, (CTCond $CT::ListItem)))
  }
  function Mover-Arbol([double]$y) {
    $t = One $win 'categ' $CT::List
    foreach ($c in (@($t) + @($t.FindAll($Scope::Descendants, (CTCond $CT::Pane))))) {
      try {
        $sp = $c.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        if ($null -ne $sp) {
          $sp.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, $y)
          Start-Sleep -Milliseconds 600
          return $c.Current.ControlType.ProgrammaticName
        }
      } catch { }
    }
    return '(sin ScrollPattern)'
  }
  function Seleccionar-Nodo([string]$pat) {
    foreach ($y in @(0, 55, 100)) {
      Mover-Arbol $y | Out-Null
      foreach ($intento in 1..4) {
        $n = @(Nodos-Actual | Where-Object { $_.Current.Name -match $pat }) | Select-Object -First 1
        if ($null -eq $n) { break }
        try {
          $n.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
          Start-Sleep -Milliseconds 500
          return $n
        } catch { Start-Sleep -Milliseconds 400 }
      }
    }
    return $null
  }
  $nodo = Seleccionar-Nodo '^Renombrar'
  if ($null -eq $nodo) { throw 'R7: el nodo de género Renombrar no está en el árbol' }
  Start-Sleep -Milliseconds 800
  $caja = One $win 'Nombre nuevo para el g.nero' $CT::Edit
  $btnRen = One $win 'Renombrar el g.nero elegido' $CT::Button
  $btnQuitar = ById $win 'DeleteGenreButton'
  if ($null -eq $caja -or $null -eq $btnRen -or $null -eq $btnQuitar) {
    throw ('R7: faltan controles de la tarjeta de género (caja=' + ($null -ne $caja) +
           ' renombrar=' + ($null -ne $btnRen) + ' quitar=' + ($null -ne $btnQuitar) + ')')
  }
  L ('R7_tarjeta_de_genero_visible=cierto | resumen=' + (Txt (One $win 'pista\(s\)' $CT::Text)) + ' | boton=' + $btnQuitar.Current.Name)
  Set-Value $caja 'Renombrado Verify'
  Start-Sleep -Milliseconds 400
  Invoke-El $btnRen
  Start-Sleep -Seconds 2
  L ('R7_estado=' + (Get-Estado $win))
  # Y ahora a leer el nodo nuevo, que sale por debajo del pliegue: al abrirse la tarjeta de
  # «GÉNERO ELEGIDO» el viewport del árbol se encoge y abajo no hay contenedor materializado
  # (H21; en la captura se comprueba que el operador sí lo ve al bajar).
  [void][QsrRot]::Raise([IntPtr]$s1.Bib.Hwnd)
  Start-Sleep -Milliseconds 900
  $donde = Mover-Arbol 100
  $msNodo = Wait-Until { (@(Nodos-Actual | Where-Object { $_.Current.Name -match 'Renombrado Verify' }).Count -eq 1) } 10000
  $nodos = Nodos-Actual
  L ('R7_nodo_nuevo=' + (@($nodos | Where-Object { $_.Current.Name -match 'Renombrado Verify' }).Count -eq 1) +
     ' (a los ' + $msNodo + ' ms, bajando el arbol por ' + $donde + ') | nodo_viejo_desaparecio=' +
     (@($nodos | Where-Object { $_.Current.Name -match '^Renombrar' }).Count -eq 0))
  L ('   NODOS TRAS RENOMBRAR: ' + (($nodos | ForEach-Object { $_.Current.Name }) -join ' / '))
  CapWindow (Find-Window $script:proc.Id '*Biblioteca*') "${Tag}_biblioteca_tras_renombrar"
  Mover-Arbol 0 | Out-Null
  $m = Get-Meta
  $p2 = Meta-By-File $m $F2
  L ('R7_json_genero_Pista2=' + $p2.Genre + ' | playCount=' + $p2.PlayCount + ' (renombrar no toca las veces emitidas)')

  # ---- R8: quitar un género armándose (segundo clic), sin cuadro modal que tape el aire
  $nodo2 = Seleccionar-Nodo 'Import Verify'
  if ($null -eq $nodo2) { throw 'R8: el nodo Import Verify no está en el árbol (con la herramienta de selección)' }
  Start-Sleep -Milliseconds 800
  $btnQuitar = ById $win 'DeleteGenreButton'
  $dest = One $win 'G.nero que recibe' $CT::ComboBox
  $destino = '(sin combo)'
  if ($null -ne $dest) {
    try { $destino = $dest.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
    catch { $destino = $dest.Current.Name }
  }
  L ('R8_etiqueta_antes=' + $btnQuitar.Current.Name + ' | genero_destino=' + $destino)
  Invoke-El $btnQuitar
  Start-Sleep -Milliseconds 600
  L ('R8_arma_estado=' + (Get-Estado $win))
  L ('R8_etiqueta_armada=' + (Txt (ById $win 'DeleteGenreButton')))
  $antes = (Meta-By-File (Get-Meta) $FI).Genre
  Invoke-El (ById $win 'DeleteGenreButton')
  Start-Sleep -Seconds 2
  L ('R8_estado_despues=' + (Get-Estado $win))
  $pi = Meta-By-File (Get-Meta) $FI
  L ('R8_genero_' + $antes + ' -> ' + $pi.Genre + ' | playCount=' + $pi.PlayCount + ' (quitar el género no borra el archivo ni el contador)')
  $tree = One $win 'categ' $CT::List
  $nodos = @($tree.FindAll($Scope::Children, (CTCond $CT::ListItem)))
  L ('R8_nodo_quitado=' + (@($nodos | Where-Object { $_.Current.Name -match 'Import Verify' }).Count -eq 0))
  # Al desaparecer el nodo seleccionado se cierra su tarjeta: "ausente" también es un final
  # correcto; lo que no puede pasar es que el botón siga armado (⚠ ¿OTRA VEZ?) esperando un clic.
  $btnAfter = ById $win 'DeleteGenreButton'
  $desarmado = ($null -eq $btnAfter) -or ($btnAfter.Current.Name -notmatch 'OTRA VEZ')
  L ('R8_boton_desarmado=' + $desarmado + ' | etiqueta=' + (Txt $btnAfter) +
     ' (ausente = la tarjeta se cerr6 con el nodo)')

  # ---- R9: evidencia visual de la sesión 1
  CapWindow (Find-Window $script:proc.Id '*Biblioteca*') "${Tag}_biblioteca_pasoB"
  CapWindow (Find-Window $script:proc.Id 'Quasar Radio Pro*') "${Tag}_consola_pasoB"

  # El reinicio se hace en el mismo sandbox: es la única forma de probar que lo que se ve al
  # arrancar vuelve del disco y no de la memoria de esta prueba.
  $s1.Proc.Kill(); Start-Sleep -Seconds 3
  $script:proc = $null

  # ==================================================================== SESIÓN 2
  $s2 = Start-Qsr 'sesión 2'
  $script:proc = $s2.Proc
  $win2 = $s2.BibEl
  $e2 = Get-Estado $win2
  L ('R10_estado_rearranque=' + $e2)
  L ('R10_restaura_fichas=' + ($e2 -match 'fichas guardadas aplicadas') +
     ' | restaura_importados=' + ($e2 -match 'importados recuperados') +
     ' | siembra_rotacion=' + ($e2 -match 'historial de rotaci'))
  $g2 = One $win2 'Tabla de tracks' $CT::DataGrid
  $rows2 = Get-Rows $g2
  L ('R10_filas=' + $rows2.Count + ' (las 5 de la sesión 1: el importado de importados/ sigue ahí)')
  $t2 = One $win2 'categ' $CT::List
  $n2 = @($t2.FindAll($Scope::Children, (CTCond $CT::ListItem)))
  L ('R10_nodo_renombrado_verify=' + (@($n2 | Where-Object { $_.Current.Name -match 'Renombrado Verify' }).Count -eq 1) +
     ' | nodo_import_verify_desaparecio=' + (@($n2 | Where-Object { $_.Current.Name -match 'Import Verify' }).Count -eq 0))
  L ('R10_rotacion=' + (Txt (One $win2 '^Rotaci' $CT::Text)) + ' (las salidas al aire son por proceso; las pistas seguidas vuelven del JSON)')
  foreach ($r in $rows2) {
    $tx = Row-Text $r
    if ($tx -match 'Pista 1|Pista 2|Pista 3|Fuera de Carpeta') { L ('   FILA: ' + $tx) }
  }
  $m2 = Get-Meta
  foreach ($f in @($F1, $F2, $F3, $FI)) {
    $mm = Meta-By-File $m2 $f
    if ($null -eq $mm) { L ('   JSON: ' + $f + ' -> SIN FICHA') }
    else { L ('   JSON: ' + $f + ' -> género=' + $mm.Genre + ' emitidas=' + $mm.PlayCount) }
  }
  $bak = Join-Path $Sandbox 'track_metadata_backup.json'
  L ('R10_escritura_atomica_copia=' + (Test-Path $bak) +
     ' | sin_tmp_sueltos=' + (-not (Test-Path ((Join-Path $Sandbox 'track_metadata.json') + '.tmp'))))

  # ---- R11: cerrar Biblioteca no cierra la emisora
  $bib2 = Find-Window $script:proc.Id '*Biblioteca*'
  [void][QsrRot]::Close([IntPtr]$bib2.Hwnd)
  Start-Sleep -Seconds 3
  L ('R11_biblioteca_desaparecio=' + (@([QsrRot]::Top($script:proc.Id) | Where-Object { $_.Visible -and $_.Title -like '*Biblioteca*' }).Count -eq 0))
  L ('R11_consola_viva=' + (@([QsrRot]::Top($script:proc.Id) | Where-Object { $_.Visible -and $_.Title -like 'Quasar Radio Pro*' }).Count -gt 0))
  CapWindow (Find-Window $script:proc.Id 'Quasar Radio Pro*') "${Tag}_consola_tras_cerrar_biblioteca"
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
    foreach ($m in @('VERIFICACION DE INTERFAZ', 'Libreria cargada', 'Atribuida reproduccion', 'Encolados', 'Emisión detenida', 'Género renombrado')) {
      $hit = Select-String -Path $logf.FullName -SimpleMatch $m | Select-Object -Last 2
      foreach ($h in $hit) { L ('   ' + $h.Line.Trim()) }
    }
    $q = Select-String -Path $logf.FullName -Pattern 'quitado' | Select-Object -Last 1
    if ($q) { L ('   ' + $q.Line.Trim()) }
  } else { L 'LOG=no hay archivo de log' }

  if ($null -ne $script:proc -and -not $script:proc.HasExited) {
    $script:proc.Kill(); Start-Sleep -Seconds 2
  }
  foreach ($z in @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)) { $z.Kill() }
  Start-Sleep -Seconds 1
  L ('RESTANTES=' + (@(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue).Count))
  $layoutAfter = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
  if (Test-Path $layoutAfter) { Remove-Item $layoutAfter -Force }
  if (-not $Keep -and (Test-Path $Sandbox)) { Remove-Item $Sandbox -Recurse -Force }
  else { L ('SANDBOX_CONSERVADO=' + $Sandbox) }
  $res = Join-Path $out "uia_${Tag}_rotacion.txt"
  [IO.File]::WriteAllLines($res, $lines, (New-Object Text.UTF8Encoding($true)))
  Write-Host ('EVIDENCIA=' + $res)
}
