param(
  [string]$Theme = 'Studio Dark Neon',
  [string]$Tag = 'ui4',
  [string]$Sandbox = (Join-Path $env:TEMP 'qsr_bib_test'),
  [switch]$Keep
)

# UI-4: verificacion en vivo de la ventana de Biblioteca.
#
# Se ejecuta SIEMPRE contra una libreria de ensayo en %TEMP%, nunca contra la libreria real.
# La libreria real vive en bin/Debug/net10.0-windows (musica/, horarias/...) y no esta en git:
# un error aqui la borraria sin copia. Por eso la prueba arranca el exe con -WorkingDirectory
# $Sandbox: AudioDatabaseService escanea "musica", "jingles"... relativos al directorio actual
# (LoadLibrary) y TrackMetadataService escribe track_metadata.json tambien relativo al directorio
# actual. Cambiando el CWD cambiamos la libreria de la que vive la aplicacion.
#
# Los nombres de los archivos de ensayo llevan el prefijo QSRBIB. Jamas HRS ni MIN: con esos
# nombres se pisaron 31 grabaciones reales de horarias/ (ver docs/UI_BASELINE.md).

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

if (-not ('QsrBib' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class BibWin { public IntPtr Hwnd; public string Title; public bool Visible; public string Rect; public int L; public int T; public int W; public int H; }
public static class QsrBib {
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
  // WPF pinta por hardware: PrintWindow sin PW_RENDERFULLCONTENT devolvia el cliente en negro
  // y con la ventana detapa, SetForegroundWindow falla desde un proceso de script, asi que se
  // simula un ALT antes de subir la ventana (mismo truco que verificar_consola.ps1).
  public static void Raise(IntPtr h) {
    ShowWindow(h, 5); BringWindowToTop(h);
    keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
    SetForegroundWindow(h);
  }
  public static void Close(IntPtr h) { SendMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); } // WM_CLOSE
  static int _target; static List<BibWin> _found;
  public static List<BibWin> Top(int pid) {
    _target = pid; _found = new List<BibWin>();
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == _target) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        var wi = new BibWin();
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
$Cond = [System.Windows.Automation.Condition]

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
# El texto de estado se busca por AutomationId (x:Name en WPF), no por Nombre: su Nombre ES el
# mensaje, y buscarlo por una etiqueta fija devolveria la etiqueta en vez del mensaje.
function ById($root, $id) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
  return $root.FindFirst($Scope::Descendants, $c)
}
function Get-Estado($win) {
  $e = ById $win 'StatusText'
  if ($null -eq $e) { return '(no hay texto de estado)' }
  return $e.Current.Name
}
# Las filas de un DataGrid de WPF salen como hijos directos en la mayoria de los casos; si la
# vista de control las anida (virtualizacion), se busca por descendientes.
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

$exe = 'C:\Users\CY-KILL\Desktop\Programación y Datanálisis\quasar_radio\bin\Debug\net10.0-windows\quasar_radio.exe'
$out = Join-Path $env:TEMP 'qsr_ui'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$lines = New-Object Collections.ArrayList
function L($m) { $lines.Add($m) | Out-Null; Write-Host $m }

function CapWindow($win, $t) {
  if ($null -eq $win) { L "CAP $t : sin ventana"; return }
  $w = [int]$win.W; $h = [int]$win.H
  if ($w -lt 50 -or $h -lt 50) { L "CAP $t : rect invalida"; return }
  $bmp = New-Object Drawing.Bitmap($w, $h)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrBib]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 350
  $hdc = $g.GetHdc()
  $ok = [QsrBib]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $path = Join-Path $out "$t.png"
  $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  L ("CAP $t : PrintWindow=$ok ${w}x${h} md5=" + (Get-FileHash $path -Algorithm MD5).Hash)
}

# ---- WAV de ensayo: PCM 16 bits mono, generado aqui para no depender de archivos reales.
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

# ---- fixture: 7 pistas + 1 ilegible en musica/, 2 en jingles/
function New-Sandbox {
  if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'musica') | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'jingles') | Out-Null
  for ($i = 1; $i -le 7; $i++) {
    New-QsrWav (Join-Path $Sandbox ('musica\QSRBIB Artista ' + $i + ' - Pista ' + $i + '.wav')) 2.0 (200 + $i * 40)
  }
  New-QsrWav (Join-Path $Sandbox 'jingles\QSRBIB Sintonia - Indicativo.wav') 1.0 660
  New-QsrWav (Join-Path $Sandbox 'jingles\QSRBIB Sintonia - Cierre.wav')    1.0 720
  # .mp3 con basura: existe en disco pero NAudio no lo abre, Duration = Zero.
  # Es la rama "No se pueden leer" del arbol de ESTADO.
  [IO.File]::WriteAllBytes((Join-Path $Sandbox 'musica\QSRBIB Roto - Pista.mp3'), ([byte[]](1..128)))
  L ('FIXTURES: 7 wav + 1 mp3 roto en musica, 2 wav en jingles -> ' + $Sandbox)
}

$pre = @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)
if ($pre.Count -gt 0) { throw "Ya hay $($pre.Count) instancia(s) abiertas; abortando para no tocar la emision." }

# La geometria recordada de esta prueba no debe heredar ni dejar la de una sesion anterior.
$layout = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
if (Test-Path $layout) { Remove-Item $layout -Force }

New-Sandbox
Set-Content -Path (Join-Path $env:APPDATA 'QuasarRadio\appearance.json') -Value ('{"theme":"' + $Theme + '"}') -Encoding UTF8

$proc = $null
try {
  $proc = Start-Process -FilePath $exe -WorkingDirectory $Sandbox -ArgumentList '--ui-check', 'Library' -PassThru
  Start-Sleep -Seconds 12

  # ---------- B1: la ventana de modulo existe y la consola sigue viva
  $wins = [QsrBib]::Top($proc.Id)
  $bib = $wins | Where-Object { $_.Visible -and $_.Title -like '*Biblioteca*' } | Select-Object -First 1
  $con = $wins | Where-Object { $_.Visible -and $_.Title -like 'Quasar Radio Pro*' } | Select-Object -First 1
  L ('B1_ventana_biblioteca=' + ($null -ne $bib) + ' | ' + $bib.Rect)
  L ('B1_consola_viva=' + ($null -ne $con) + ' | ' + $con.Rect)
  if ($null -eq $bib) {
    # Si la ventana no llega a existir casi siempre es el XAML al instanciarla: el log del
    # modulo trae la excepcion real, y sin ella se esta adivinando.
    $lg = (Get-ChildItem (Join-Path (Split-Path $exe) 'logs') -Filter 'quasar_radio-*.log' -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1)
    if ($null -ne $lg) {
      $tail = Get-Content $lg.FullName -Tail 40
      $start = ($tail | Select-String -SimpleMatch 'No se pudo construir' | Select-Object -Last 1).LineNumber
      if ($start) { foreach ($t in $tail | Select-Object -Skip ($start - 1) -First 8) { L ('LOG: ' + $t) } }
    }
    throw 'sin ventana de Biblioteca'
  }

  # FromHandle en vez de buscar entre los hijos del escritorio: las ventanas de modulo se abren
  # con Owner = MainWindow y UIA no las sube al root (por el root solo asoma la consola), aunque
  # EnumWindows las ve perfectamente. El HWND que devuelve EnumWindows es la puerta fiable.
  $win = $AE::FromHandle([IntPtr]$bib.Hwnd)
  if ($null -eq $win) { throw 'Biblioteca no es alcanzable por UIA' }
  L ('B1_uia_name=' + $win.Current.Name)

  # Centinela = la linea de estado, lo ultimo que se publica en esta ventana. Los primeros
  # FindAll sobre un arbol que todavia se esta pintando devuelven solo la parte de arriba, y un
  # control que si existe parece no existir (paso documentado en verificar_consola.ps1).
  $esp = [Diagnostics.Stopwatch]::StartNew()
  while ($esp.ElapsedMilliseconds -lt 60000) {
    if ($null -ne (ById $win 'StatusText')) { break }
    Start-Sleep -Milliseconds 500
  }
  L ('B1_biblioteca_lista_en_ms=' + [int]$esp.ElapsedMilliseconds)

  # ---------- B2: el arbol de categorias con sus conteos reales
  $tree = One $win 'categ' $CT::List
  if ($null -eq $tree) { L 'B2_arbol: NO ENCONTRADO'; }
  else {
    $items = $tree.FindAll($Scope::Children, (CTCond $CT::ListItem))
    L ('B2_arbol_items=' + $items.Count)
    foreach ($it in $items) { L ('   NODO: [' + $it.Current.Name + ']') }
  }

  # ---------- B3: la tabla pinta las 10 filas del fixture
  $grid = One $win 'Tabla de tracks' $CT::DataGrid
  $rows = @()
  if ($null -eq $grid) { L 'B3_tabla: NO ENCONTRADA' }
  else {
    $rows = Get-Rows $grid
    L ('B3_filas=' + $rows.Count + ' (esperadas 10: 7 wav + 1 ilegible + 2 jingles)')
  }

  function Get-Row($pattern) {
    foreach ($r in $rows) {
      foreach ($t in $r.FindAll($Scope::Descendants, (CTCond $CT::Text))) {
        if ($t.Current.Name -match $pattern) { return $r }
      }
    }
    return $null
  }
  function Row-Text($row) {
    $parts = @()
    foreach ($t in $row.FindAll($Scope::Descendants, (CTCond $CT::Text))) { $parts += $t.Current.Name }
    return ($parts -join ' | ')
  }
  function Select-Rows($list) {
    $first = $true
    foreach ($r in $list) {
      $p = $r.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
      if ($first) { $p.Select(); $first = $false } else { $p.AddToSelection() }
    }
    Start-Sleep -Milliseconds 700
  }

  # ---------- B4: seleccion multiple y resumen
  $r2 = Get-Row 'Pista 2'
  $r4 = Get-Row 'Pista 4'
  if ($null -eq $r2 -or $null -eq $r4) { L 'B4_seleccion: filas del lote no encontradas' }
  else {
    Select-Rows @($r2, $r4)
    L ('B4_fila2: ' + (Row-Text $r2))
    $sum = One $win 'seleccionados' $CT::Text
    L ('B4_resumen_seleccion=' + $sum.Current.Name)
  }

  # ---------- B5: edicion en lote (genero) y persistencia en el JSON de ensayo
  # 'Aplicar g.nero' con la 'e' acentuada: la N de Name de la casilla lleva tilde y los
  # patrones ASCII puros no la encuentran (coste de escribir la interfaz en espanol).
  $chk = One $win 'Aplicar g.nero' $CT::CheckBox
  $cbo = One $win '^G.?nero para el lote$' $CT::ComboBox
  $btn = One $win 'Aplicar.*lote' $CT::Button
  if ($null -eq $chk -or $null -eq $cbo -or $null -eq $btn) {
    L ('B5_lote: faltan controles (chk=' + ($null -ne $chk) + ' cbo=' + ($null -ne $cbo) + ' btn=' + ($null -ne $btn) + ')')
  } else {
    L ('B5_lote_boton_habilitado=' + $btn.Current.IsEnabled)
    if ($chk.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) {
      [void]$chk.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    }
    [void]$cbo.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Lote Verify')
    Start-Sleep -Milliseconds 400
    [void]$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    $json = Join-Path $Sandbox 'track_metadata.json'
    if (Test-Path $json) {
      $meta = Get-Content $json -Raw | ConvertFrom-Json
      $marcados = @($meta | Where-Object { $_.Genre -eq 'Lote Verify' })
      $otros = @($meta | Where-Object { $_.Genre -ne 'Lote Verify' })
      L ('B5_json_total=' + $meta.Count + ' | genero nuevo=' + $marcados.Count + ' | sin cambiar=' + $otros.Count)
      foreach ($m in $marcados) { L ('   LOTEADO: ' + $m.FilePath) }
      L ('B5_json_es_la_copia_de_ensayo=' + (($marcados.Count -gt 0) -and ((Split-Path $marcados[0].FilePath) -notmatch 'Debug')))
    } else { L 'B5_json: no se escribio track_metadata.json' }
    L ('B5_estado=' + (Get-Estado $win))
    # el arbol tiene que reflejar el genero nuevo sin cerrar la ventana
    $tree2 = One $win 'categ' $CT::List
    $nuevo = @($tree2.FindAll($Scope::Children, (CTCond $CT::ListItem)) | Where-Object { $_.Current.Name -match 'Lote Verify' })
    L ('B5_arbol_nodo_genero_nuevo=' + ($nuevo.Count -eq 1) + ' | ' + ($nuevo | ForEach-Object { $_.Current.Name }))
  }

  # ---------- B6: inspector de un track (metadatos individuales + CUE)
  $rows = Get-Rows $grid
  $r1 = Get-Row 'Pista 1'
  if ($null -eq $r1) { L 'B6_inspector: fila no encontrada' }
  else {
    Select-Rows @($r1)
    $tb = One $win 'T.tulo del track' $CT::Edit
    $ap = One $win 'Aplicar metadatos del track' $CT::Button
    if ($null -eq $tb -or $null -eq $ap) { L ('B6_inspector: campos ausentes (tb=' + ($null -ne $tb) + ' btn=' + ($null -ne $ap) + ')') }
    else {
      L ('B6_tarjeta_habilitada=' + $ap.Current.IsEnabled)
      [void]$tb.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Titulo Verify')
      Start-Sleep -Milliseconds 400
      [void]$ap.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      Start-Sleep -Seconds 2
      $meta = Get-Content (Join-Path $Sandbox 'track_metadata.json') -Raw | ConvertFrom-Json
      $cambiado = @($meta | Where-Object { $_.Title -eq 'Titulo Verify' })
      L ('B6_titulo_persistido=' + ($cambiado.Count -eq 1) + ' | ' + $cambiado[0].FilePath)
    }
  }

  # ---------- B7: encolar la seleccion y despues AUTO, viendo la cola de la consola
  # Antes de leer la cola de la consola hay que esperar a que su arbol este completo: mientras
  # el hilo de UI esta ocupado, FindAll(Descendants) publica solo una parte del tree y contar
  # filas sale lo que sea. El centinela es la pista de la franja de modulos, lo ultimo en pintarse.
  $conEl = $AE::FromHandle([IntPtr]$con.Hwnd)
  $espera = [Diagnostics.Stopwatch]::StartNew()
  while ($espera.ElapsedMilliseconds -lt 60000) {
    if ($null -ne (One $conEl 'abre su propia ventana' $CT::Text)) { break }
    Start-Sleep -Milliseconds 500
  }
  L ('B7_consola_lista_en_ms=' + [int]$espera.ElapsedMilliseconds)
  $conGrid = $null
  if ($null -ne $conEl) { $conGrid = ($conEl.FindAll($Scope::Descendants, (CTCond $CT::DataGrid)) | Select-Object -First 1) }
  function Cola { if ($null -eq $conGrid) { return -1 } return (Get-Rows $conGrid).Count }
  $antes = Cola
  L ('B7_filas_pintadas_en_la_cola_antes=' + $antes + ' (proxy: el DataGrid solo pinta las que caben en pantalla; el numero real lo dice el texto de estado)')

  $rows = Get-Rows $grid
  $q = @((Get-Row 'Pista 3'), (Get-Row 'Pista 5'))
  if ($q[0] -and $q[1]) {
    Select-Rows $q
    $bq = One $win 'Encolar los tracks seleccionados' $CT::Button
    L ('B7_encolar_habilitado=' + $bq.Current.IsEnabled)
    [void]$bq.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    # El texto de estado lo pone la propia cola de emision (_playlist.Count), asi que ahi esta
    # el numero real; las filas de UIA son solo las que caben en la ventana.
    L ('B7_estado_tras_encolar=' + (Get-Estado $win) + ' | filas_pintadas=' + (Cola))
  } else { L 'B7_encolar: filas no encontradas' }

  $auto = One $win 'AUTO PLAYLIST' $CT::Button
  if ($null -eq $auto) { L 'B8_auto: boton no encontrado' }
  else {
    [void]$auto.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 3
    L ('B8_auto_estado=' + (Get-Estado $win))
    L ('B8_cola_despues_de_auto=' + (Cola) + ' (AUTO vacia la cola y la rellena desde la libreria)')
    $conWins = [QsrBib]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like 'Quasar Radio Pro*' }
    CapWindow ($conWins | Select-Object -First 1) "${Tag}_consola_tras_auto"
  }

  # ---------- B9: archivo ilegible visible en la tabla y en el filtro de problematicos
  $rows = Get-Rows $grid
  $rota = Get-Row 'Roto'
  if ($null -eq $rota) { L 'B9_roto: fila no encontrada' } else { L ('B9_fila_rota: ' + (Row-Text $rota)) }
  $cbx = One $win 'Mostrar solo archivos' $CT::CheckBox
  if ($null -eq $cbx) { L 'B9_filtro: casilla no encontrada' }
  else {
    [void]$cbx.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Seconds 2
    $solo = Get-Rows $grid
    L ('B9_solo_problematicos_filas=' + $solo.Count)
    $tree = One $win 'categ' $CT::List
    foreach ($it in $tree.FindAll($Scope::Children, (CTCond $CT::ListItem))) {
      if ($it.Current.Name -match 'leer|disco') { L ('   NODO ESTADO: [' + $it.Current.Name + ']') }
    }
    [void]$cbx.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Seconds 1
  }

  # ---------- B10: las operaciones de administrador siguen accesibles sin login
  # Desde AUTH-1 (2026-10-22) el boton de una operacion de nivel NO esta apagado: el nivel se
  # pide al pulsarlo (AuthGate), no dejando la funcion invisible para quien cura la musica.
  # Que la puerta sigue existiendo se comprueba en scripts/ui/verificar_permisos.ps1 (P8), que
  # ve salir el dialogo de identificacion y comprueba que al VOLVER no se escribe nada.
  # AUTH-1: ninguna de estas operaciones sigue pidiendo clave para habilitarse; la clave ahora
  # es un sello en el momento del clic (ver verificar_permisos.ps1, P7 y P8). El unico False
  # esperado es QUITAR: su comando pide seleccion, no permiso, y en esta prueba no hay ninguna
  # fila marcada.
  $puertas = [ordered]@{
    'RECARGAR' = 'RECARGAR'; 'COMPROBAR' = 'COMPROBAR ARCHIVOS'; 'GUARDAR' = 'GUARDAR'
    'CARGAR JSON' = 'CARGAR JSON'; 'CSV' = 'CSV'; 'QUITAR' = 'QUITAR SELECCIONADOS'
  }
  foreach ($k in $puertas.Keys) {
    $b = One $win $puertas[$k] $CT::Button
    if ($null -eq $b) { L ("B10_$k : boton no encontrado") } else { L ("B10_$k : accesible_sin_login=" + $b.Current.IsEnabled) }
  }

  # ---------- B11: evidencia visual
  $wins = [QsrBib]::Top($proc.Id)
  $bib = $wins | Where-Object { $_.Visible -and $_.Title -like '*Biblioteca*' } | Select-Object -First 1
  CapWindow $bib "${Tag}_biblioteca"
  try {
    $wp = $win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    # El wrapper administrado de UIA no expone Maximize(): el estado de ventana se cambia con
    # SetWindowVisualState, y si no la evidencia salia recortada a 1320x820 con las tarjetas
    # de lote y CUE por debajo del pliegue.
    [void]$wp.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
    Start-Sleep -Seconds 2
    $bib2 = ([QsrBib]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like '*Biblioteca*' } | Select-Object -First 1)
    CapWindow $bib2 "${Tag}_biblioteca_max"
    L ('B11_maximizada=' + $bib2.Rect)
  } catch { L ('B11_max: ' + $_.Exception.Message) }

  # ---------- B12: cerrar el modulo no cierra la emisora y la consola se entera
  [void][QsrBib]::Close([IntPtr]$bib.Hwnd)
  Start-Sleep -Seconds 3
  $sigue = @([QsrBib]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like '*Biblioteca*' }).Count
  $consola = @([QsrBib]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like 'Quasar Radio Pro*' }).Count
  L ('B12_biblioteca_desaparecio=' + ($sigue -eq 0))
  L ('B12_consola_viva=' + ($consola -gt 0))
}
catch { L ('ERROR: ' + $_.Exception.Message) }
finally {
  $logf = (Get-ChildItem (Join-Path (Split-Path $exe) 'logs') -Filter 'quasar_radio-*.log' -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1)
  if ($null -ne $logf) {
    L ('LOG=' + $logf.Name)
    foreach ($m in @('VERIFICACION DE INTERFAZ', 'Modulo abierto', 'Encolados', 'lista automatica', 'Biblioteca cerrada')) {
      $hit = Select-String -Path $logf.FullName -SimpleMatch $m | Select-Object -Last 2
      foreach ($h in $hit) { L ('   ' + $h.Line.Trim()) }
    }
  } else { L 'LOG=no hay archivo de log' }

  if ($null -ne $proc -and -not $proc.HasExited) {
    $proc.Kill(); Start-Sleep -Seconds 2
  }
  L ('RESTANTES=' + (@(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue).Count))
  $layoutAfter = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
  if (Test-Path $layoutAfter) { Remove-Item $layoutAfter -Force }   # no dejar la geometria de ensayo
  if (-not $Keep -and (Test-Path $Sandbox)) { Remove-Item $Sandbox -Recurse -Force }
  else { L ('SANDBOX_CONSERVADO=' + $Sandbox) }
  $res = Join-Path $out "uia_${Tag}_biblioteca.txt"
  [IO.File]::WriteAllLines($res, $lines, (New-Object Text.UTF8Encoding($true)))
  Write-Host ('EVIDENCIA=' + $res)
}
