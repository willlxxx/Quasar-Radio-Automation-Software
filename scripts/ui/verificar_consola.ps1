param([string]$Theme = 'Studio Dark Neon', [string]$Tag = 'ui3a', [switch]$Maximize, [string]$Tab = '')

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

if (-not ('QsrWin' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class WinInfo { public IntPtr Hwnd; public string Title; public bool Visible; public string Rect; public int L; public int T; public int W; public int H; }
public static class QsrWin {
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
  // Con la consola minimizada UIA reporta el rectangulo en -32000 y el arbol sale vacio o a
  // medias: las medidas D1/E1/E3 dependen de que la ventana este visible de verdad.
  public static void Restore(IntPtr h) { ShowWindow(h, 9); BringWindowToTop(h); }   // SW_RESTORE
  public static bool Minimizada(IntPtr h) { return IsIconic(h); }
  [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  // WPF pinta por hardware: con la ventana tapada, PrintWindow devolvia el cliente en blanco.
  // Windows solo deja cambiar el foco a un proceso que acaba de recibir un teclado, asi que se
  // simula un ALT antes; sin ese truco SetForegroundWindow devuelve false y la evidencia salia
  // con el navegador del fundador por delante (blanco).
  public static void Raise(IntPtr h) {
    ShowWindow(h, 5); BringWindowToTop(h);
    keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
    SetForegroundWindow(h);
  }
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
  static int _target; static List<WinInfo> _found;
  public static List<WinInfo> Top(int pid) {
    _target = pid; _found = new List<WinInfo>();
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == _target) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        var wi = new WinInfo();
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

$exe  = 'C:\Users\CY-KILL\Desktop\Programación y Datanálisis\quasar_radio\bin\Debug\net10.0-windows\quasar_radio.exe'
$out  = 'C:\Users\CY-KILL\AppData\Local\Temp\qsr_ui'
$conf = Join-Path $env:APPDATA 'QuasarRadio\appearance.json'

Set-Content -Path $conf -Value ('{"theme":"' + $Theme + '"}') -Encoding UTF8
$lines = New-Object Collections.ArrayList
function L($m) { $lines.Add($m) | Out-Null }

function CapRect($x, $y, $w, $h, $t) {
  if ($w -lt 50 -or $h -lt 50) { L "CAP $t : rect invalida $x,$y,$w,$h"; return }
  $bmp = New-Object Drawing.Bitmap($w, $h)
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object Drawing.Size($w, $h)))
  $path = Join-Path $out "$t.png"
  $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L "CAP $t : ${w}x${h} md5=$((Get-FileHash $path -Algorithm MD5).Hash)"
}

# PrintWindow pinta la ventana aunque otra este delante. CopyFromScreen capturo el navegador
# del fundador por encima de la consola (ver H6 en docs/UI_BASELINE.md), asi que la evidencia
# completa de la ventana se toma aqui y los recortes siguen siendo de pantalla.
function CapWindow($win, $t) {
  if ($null -eq $win) { L "CAP $t : sin ventana"; return }
  $w = [int]$win.W; $h = [int]$win.H
  if ($w -lt 50 -or $h -lt 50) { L "CAP $t : rect invalida"; return }
  $bmp = New-Object Drawing.Bitmap($w, $h)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrWin]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 350
  $hdc = $g.GetHdc()
  $ok = [QsrWin]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)   # PW_RENDERFULLCONTENT
  $g.ReleaseHdc($hdc)
  $g.Dispose()
  $path = Join-Path $out "$t.png"
  $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  L "CAPW $t : PrintWindow=$ok ${w}x${h} md5=$((Get-FileHash $path -Algorithm MD5).Hash)"
}

# Recorte a partir del bitmap de PrintWindow: CapRect captura lo que hay en el monitor, y
# cuando el fundador tiene otra ventana delante la evidencia salia en blanco o con su navegador.
function CapWindowCrop($win, $x, $y, $w, $h, $t) {
  if ($w -lt 20 -or $h -lt 20) { L "CAP $t : recorte invalido ${w}x${h}"; return }
  $bmp = New-Object Drawing.Bitmap([int]$win.W, [int]$win.H)
  $g = [Drawing.Graphics]::FromImage($bmp)
  [void][QsrWin]::Raise([IntPtr]$win.Hwnd); Start-Sleep -Milliseconds 350
  $hdc = $g.GetHdc()
  [void][QsrWin]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $cx = [int]($x - $win.L); $cy = [int]($y - $win.T)
  if ($cx -lt 0) { $cx = 0 }; if ($cy -lt 0) { $cy = 0 }
  if ($cx + $w -gt $bmp.Width) { $w = $bmp.Width - $cx }
  if ($cy + $h -gt $bmp.Height) { $h = $bmp.Height - $cy }
  if ($w -lt 20 -or $h -lt 20) { L "CAP $t : recorte fuera de la ventana"; $bmp.Dispose(); return }
  $crop = New-Object Drawing.Bitmap([int]$w, [int]$h, ([Drawing.Imaging.PixelFormat]::Format32bppArgb))
  $g2 = [Drawing.Graphics]::FromImage($crop)
  $g2.DrawImage($bmp, (New-Object Drawing.Rectangle(0, 0, [int]$w, [int]$h)), $cx, $cy, [int]$w, [int]$h, [Drawing.GraphicsUnit]::Pixel)
  $g2.Dispose()
  $path = Join-Path $out "$t.png"
  $crop.Save($path, [Drawing.Imaging.ImageFormat]::Png)
  $crop.Dispose(); $bmp.Dispose()
  L "CAPC $t : PrintWindow ${w}x${h} md5=$((Get-FileHash $path -Algorithm MD5).Hash)"
}

function Wins($ownerPid) {
  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $all = @()
  foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($w.Current.ProcessId -eq $ownerPid) { $all += $w }
  }
  return $all
}

function Find-Text($root, $pattern) {
  $tc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
  foreach ($e in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tc)) {
    if ($e.Current.Name -match $pattern) { return $e }
  }
  return $null
}

# Reconsulta con paciencia. La consola viva tiene reloj, VU y formas de onda moviendose todo el
# tiempo, y un FindAll(Descendants) sobre un arbol que se esta re-publicando sale por partes:
# en la corrida c4 falto D1 (NIVEL DE SALIDA / VOLUMEN MASTER / DETECTOR SILENCIO), la franja SFX
# y los botones de modulos EN LA CONSULTA, mientras la evidencia de PrintWindow los muestra
# pintados. Antes de escribir "NO APARECE EN EL ARBOL" se vuelve a preguntar varias veces.
function Find-Text2($root, $pattern, [int]$tries = 6) {
  for ($i = 1; $i -le $tries; $i++) {
    $e = Find-Text $root $pattern
    if ($null -ne $e -and $e.Current.BoundingRectangle.X -gt -20000) {
      if ($i -gt 1) { L ('  (aparecio en la reconsulta ' + $i + ' de ' + $pattern + ')') }
      return $e
    }
    Start-Sleep -Milliseconds 700
  }
  return $null
}

# Lo mismo para una coleccion: se resuelve por conteo en el punto de uso (ver E3).

function RectOf($e) {
  $r = $e.Current.BoundingRectangle
  return [pscustomobject]@{ X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
}

$pre = @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)
if ($pre.Count -gt 0) { throw "Ya hay $($pre.Count) instancia(s) abiertas; abortando." }

# La geometria que el programa guarda al cerrarse se recupera al arrancar: si la corrida anterior
# termino maximizada (o la termino el fundador a mano), D2 mide -8,-8 1616x876 y da False en las
# cuatro comprobaciones de "cabe en el area util". Se arranca siempre de cero.
$layout = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
if (Test-Path $layout) { Remove-Item $layout -Force }

$proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
try {
  Start-Sleep -Seconds 10
  L ("TEMA=" + $Theme)

  # ---------- D2: la consola debe caber en el area util del monitor que la recibe
  $t = ([QsrWin]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like '*Quasar*' } | Select-Object -First 1)
  if ($null -eq $t) { throw 'sin ventana de consola' }
  $bounds = New-Object Drawing.Rectangle($t.L, $t.T, $t.W, $t.H)
  $scr = [System.Windows.Forms.Screen]::FromRectangle($bounds)
  $wa = $scr.WorkingArea
  $b  = $scr.Bounds
  L ("MONITOR: bounds=" + $b.X + "," + $b.Y + " " + $b.Width + "x" + $b.Height + " work=" + $wa.X + "," + $wa.Y + " " + $wa.Width + "x" + $wa.Height)
  L ("CONSOLA: " + $t.Rect)
  L ("D2_barra_titulo_en_area=" + ($t.T -ge $wa.Y))
  L ("D2_dentro_arriba=" + ($t.T + $t.H -le $wa.Y + $wa.Height))
  L ("D2_dentro_izq=" + ($t.L -ge $wa.X))
  L ("D2_dentro_der=" + ($t.L + $t.W -le $wa.X + $wa.Width))

  $main = $null
  foreach ($w in (Wins $proc.Id)) { if ($w.Current.Name -like '*Quasar*') { $main = $w } }
  if ($null -eq $main) { throw 'sin ventana principal (UIA)' }

  # ---------- ARRANQUE: el arbol de UIA tiene que estar completo antes de medir nada
  # FindAll(Descendants) sobre una ventana de WPF devuelve lo que la aplicacion alcanza a
  # publicar mientras su hilo de UI esta ocupado: preguntando durante el escaneo de la libreria
  # salian las etiquetas de arriba y las franjas de abajo NO, y el informe parecia un boton roto
  # que si funciona. El centinela es el texto mas profundo del arbol (la pista de la franja de
  # modulos); cuando ese aparece, todo lo demas ya esta publicado. El tiempo que tarda es la
  # medida real del arranque con la libreria del fundador (hilo de UI ocupado: A1/1.4 del plan
  # de arquitectura).
  # Con la consola minimizada UIA sigue publicando el arbol, pero con todos los rectangulos en
  # -32000: D1/E1/E3 saldrian "NO APARECE" o con rects imposibles (corrida c7: la franja SFX en
  # -31977,-31405). Se restaura antes de medir y se deja constancia en el informe.
  $hwndMain = [int]$main.Current.NativeWindowHandle
  if ([QsrWin]::Minimizada([IntPtr]$hwndMain)) {
    L 'ARRANQUE_consola_minimizada=True : se restaura antes de medir'
    [void][QsrWin]::Restore([IntPtr]$hwndMain)
    Start-Sleep -Seconds 2
  } else { L 'ARRANQUE_consola_minimizada=False' }

  # Centinela: el arbol de UIA se publica por partes mientras el escaneo de la libreria tiene
  # ocupado al hilo de UI (H11). La etiqueta mas profunda de la franja de modulos NO bastaba: en
  # las corridas de ui4b 'BOTONERA SFX' se publicaba despues que ella, y E1 salia "NO APARECE EN
  # EL ARBOL" con la franja pintada en la captura. Se esperan las dos etiquetas y un conteo de
  # botones que ya no cambia entre dos consultas seguidas.
  $btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  $espera = [Diagnostics.Stopwatch]::StartNew()
  $listo = $false; $prev = -1; $igual = 0; $n = 0
  while ($espera.ElapsedMilliseconds -lt 90000) {
    $a = Find-Text $main 'abre su propia ventana'
    $b = Find-Text $main 'BOTONERA SFX'
    $n = @($main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)).Count
    if ($n -eq $prev) { $igual++ } else { $igual = 0; $prev = $n }
    # Vale solo si ademas esta en pantalla: minimizada, la etiqueta existe pero su rect es -32000.
    if ($null -ne $a -and $null -ne $b -and $b.Current.BoundingRectangle.X -gt -20000 -and $n -ge 40 -and $igual -ge 2) {
      $listo = $true; break
    }
    Start-Sleep -Milliseconds 500
  }
  L ("ARRANQUE_arbol_uia_completo=" + $listo + " en_ms=" + [int]$espera.ElapsedMilliseconds +
     " botones=" + $n + " (tras los 10 s de cortesía)")

  # Que ventana es la que se esta midiendo. $main es la ULTIMA cuyo titulo trae "Quasar": si el
  # programa tiene abierta otra ventana con ese texto (un modulo, un aviso), la eleccion cambia
  # de una corrida a otra y D1/E1/E3 se leen sobre un arbol que no es la consola. Se vuelcan
  # todas las candidatas con su conteo de botones para que el informe lo diga sin adivinar.
  foreach ($w in (Wins $proc.Id)) {
    if ($w.Current.Name -like '*Quasar*') {
      $nb = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)).Count
      L ('   VENTANA hwnd=' + [int]$w.Current.NativeWindowHandle + ' botones=' + $nb + ' titulo=' + $w.Current.Name)
    }
  }

  if ($Maximize) {
    # UIA no expone SetState a traves de PowerShell; se usa el ShowWindow de Win32.
    if (-not ('U32' -as [type])) {
      Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class U32 {
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
}
'@
    }
    [void][U32]::ShowWindow([IntPtr]$t.Hwnd, 3)   # SW_MAXIMIZE
    Start-Sleep -Seconds 3
    $t2 = ([QsrWin]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like '*Quasar*' } | Select-Object -First 1)
    L ("MAXIMIZADA rect=" + $t2.Rect)
    # La captura tambien se aplaza: PrintWindow mata el arbol de UIA de la ventana (ver nota
    # junto a $cropMaster) y aqui despues todavia se miden D1, E1 y E3.
    $capMaxWin = $t2
  }

  # Tras maximizar, la geometria leida antes ya no vale: se vuelve a tomar ventana y rect.
  $t = ([QsrWin]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like '*Quasar*' } | Select-Object -First 1)
  $mr = RectOf $main

  # Las capturas van TODAS al final, despues de medir. Motivo, medido con una sonda aparte el
  # 2026-10-23: el PrintWindow de la consola deja el arbol de UIA de esa ventana sin contenido
  # para el resto de la corrida (los 62 botones pasan a 9, y esos 9 son Minimizar/Maximizar/
  # Cerrar de las tres ventanas del proceso; ya no aparece nada ni por AutomationId, ni re-
  # resolviendo el HWND, y no se recupera ni esperando ocho segundos). La ventana sigue pintada
  # -la misma captura lo demuestra- y el programa responde, pero desde ese momento UIA no ve su
  # contenido, asi que cualquier D1/E1/E3 que se mida despues sale "NO APARECE EN EL ARBOL".
  # Antes de la primera captura se guarda el rectangulo de cada recorte y al final se pintan.
  $cropMaster = $null
  $cropSfx = $null

  # ---------- D1: el VU master y el volumen master deben estar pintados dentro de la ventana
  foreach ($pat in @('NIVEL DE SALIDA', 'VOLUMEN MASTER', 'DETECTOR SILENCIO')) {
    $e = Find-Text2 $main $pat
    if ($null -eq $e) { L ("D1_TXT [$pat] : NO APARECE EN EL ARBOL"); continue }
    $r = RectOf $e
    $dentro = ($r.Y -ge $mr.Y) -and ($r.Y + $r.H -le $mr.Y + $mr.H + 2) -and ($r.X -ge $mr.X)
    L ("D1_TXT [$($e.Current.Name)] rect=" + $r.X + "," + $r.Y + " " + $r.W + "x" + $r.H + " dentro=" + $dentro)
  }

  $vu = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Custom)
  $leds = @()
  for ($try = 1; $try -le 6; $try++) {
    $leds = @()
    foreach ($c in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $vu)) {
      if ($c.Current.ClassName -match 'LedVuMeter') { $leds += $c }
    }
    if ($leds.Count -gt 0) { break }
    Start-Sleep -Milliseconds 700
  }
  L ("D1_ledvu_controls=" + $leds.Count)
  foreach ($c in $leds) {
    $r = RectOf $c
    L ("  LED class=" + $c.Current.ClassName + " rect=" + $r.X + "," + $r.Y + " " + $r.W + "x" + $r.H + " visible=" + $c.Current.IsOffscreen)
  }

  # recorte del panel master para la evidencia visual (se pinta al final, ver nota de arriba)
  $lbl = Find-Text2 $main 'NIVEL DE SALIDA' 2
  if ($null -ne $lbl) {
    $r = RectOf $lbl
    $x = [Math]::Max($mr.X, $r.X - 14)
    $y = [Math]::Max($mr.Y, $r.Y - 74)
    $w = [Math]::Min($mr.W, $r.X + 420) - $x
    $h = [Math]::Min($mr.H, $r.Y + 150) - $y
    $cropMaster = @($x, $y, $w, $h)
  }

  # ---------- E1/E2 (UI-3b paso 3): franja SFX siempre visible y cero pestanas
  $sfx = Find-Text2 $main 'BOTONERA SFX'
  if ($null -eq $sfx) { L 'E1_franja_sfx: NO APARECE EN EL ARBOL' }
  else {
    $r = RectOf $sfx
    $dentro = ($r.Y -ge $mr.Y) -and ($r.Y + $r.H -le $mr.Y + $mr.H + 2) -and ($r.X -ge $mr.X)
    L ("E1_botonera_sfx rect=" + $r.X + "," + $r.Y + " " + $r.W + "x" + $r.H + " dentro=" + $dentro)
    $btn = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $pads = @(); $nom = @()
    foreach ($b in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btn)) {
      # El nombre del pad sale de AutomationProperties.Name (F1..F12): sin el, WPF no lo
      # deduce del contenido complejo y el pad era invisible para UIA.
      if ($b.Current.Name -match '^F(1[0-2]|[1-9])$') { $pads += $b; $nom += $b.Current.Name }
    }
    L ("E1_pads_visibles=" + $pads.Count + " [" + (($nom | Sort-Object {[int]($_.Substring(1))}) -join ',') + "]")
    foreach ($p in ($pads | Select-Object -First 3)) {
      $pr = RectOf $p
      L ("  pad rect=" + $pr.X + "," + $pr.Y + " " + $pr.W + "x" + $pr.H)
    }
    # recorte de la franja para la evidencia visual (se pinta al final)
    $x = [Math]::Max($mr.X, $r.X - 14)
    $y = [Math]::Max($mr.Y, $r.Y - 10)
    $cropSfx = @($x, $y, ([Math]::Min($mr.W - 20, $mr.X + $mr.W - $x - 20)), ([Math]::Min($mr.H, $r.Y + 140) - $y))
  }
  $tic = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
  L ("E2_pestanas_de_suite=" + (@($main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tic)).Count))

  # ---------- E3 (UI-4): BASE DE DATOS dejo de ser un panel de la consola; su puerta es la franja
  # Aqui la ausencia ES el resultado esperado, asi que se reconsulta poco (2 intentos): lo
  # suficiente para no cantar un "no esta" que era el arbol a medias.
  $bdd = Find-Text2 $main 'BASE DE DATOS' 2
  if ($null -eq $bdd) { L 'E3_panel_base_de_datos_en_consola=no esta (correcto)' }
  else {
    $r = RectOf $bdd
    L ('E3_panel_base_de_datos_en_consola=SIGUE: rect=' + $r.X + ',' + $r.Y + ' ' + $r.W + 'x' + $r.H)
  }
  # Los cinco botones de la franja se buscan sobre el total de botones del arbol, y el total se
  # reimprime en el informe: en la corrida c5 salio E3_franja_botones=[] con la franja pintada en
  # la captura, y sin el conteo no se puede distinguir "arbol a medias" de "nombre que no
  # coincide". La sonda probo_botonera.ps1 vio 55 botones y los cinco por su nombre.
  $btn2 = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  $todos = @()
  for ($try = 1; $try -le 6; $try++) {
    $todos = @($main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btn2))
    if ($todos.Count -ge 40) { break }
    Start-Sleep -Milliseconds 700
  }
  $franja = @()
  foreach ($b in $todos) {
    if ($b.Current.Name -match 'BIBLIOTECA|STREAMING|PROGRAMAC|REPORTES|DSP') { $franja += $b.Current.Name }
  }
  L ('E3_botones_en_el_arbol=' + $todos.Count + ' | franja=[' + ($franja -join ', ') + ']')

  # D3 (abrir pestana): sin caso desde UI-3b paso 3, la consola ya no tiene TabControl.
  # Se conserva por si una fase futura vuelve a montar pestanas dentro de la consola.
  if ($Tab -ne '') {
    $tcc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
    $hit = $null
    foreach ($ti in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tcc)) {
      if ($ti.Current.Name -match $Tab) { $hit = $ti }
    }
    if ($null -eq $hit) { L ("TAB [$Tab] no encontrada") }
    else {
      try {
        ($hit.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
        L ('TAB_seleccionada=' + $hit.Current.Name)
      } catch { L ('SELECT fallo: ' + $_.Exception.Message) }
      Start-Sleep -Seconds 2
      $mr = RectOf $main
      CapRect $mr.X $mr.Y $mr.W $mr.H ("$($Tag)_tab")
      $bx = $mr.X + [int]($mr.W * 0.575)
      $by = $mr.Y + [int]($mr.H * 0.52)
      CapRect $bx $by ([int]($mr.W * 0.425)) ([int]($mr.H * 0.45)) ("$($Tag)_botones")
    }
  }

  # franja de los decks: altura real de la fila que antes era fija en 210
  $deck = Find-Text $main 'VOLUMEN MASTER'
  if ($null -ne $deck) {
    $r = RectOf $deck
    L ("DECK_fila_hasta_volumen=" + ($r.Y + $r.H - $mr.Y))
  }

  # ---------- las capturas, al final de todo lo que se mide por UIA (ver la nota de $cropMaster)
  CapWindow $t ("$($Tag)_consola")
  if ($null -ne $cropMaster) { CapWindowCrop $t $cropMaster[0] $cropMaster[1] $cropMaster[2] $cropMaster[3] ("$($Tag)_master") }
  if ($null -ne $cropSfx) { CapWindowCrop $t $cropSfx[0] $cropSfx[1] $cropSfx[2] $cropSfx[3] ("$($Tag)_franja_sfx") }
  if ($null -ne $capMaxWin) { CapWindow $capMaxWin ("$($Tag)_consola_max") }
}
catch { L ('ERROR: ' + $_.Exception.Message) }
finally {
  foreach ($w in (Wins $proc.Id)) {
    try { ($w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close() } catch {}
  }
  Start-Sleep -Seconds 3
  L ('SALIO_SIN_MATARLO=' + $proc.HasExited)
  if (-not $proc.HasExited) { $proc.Kill(); Start-Sleep -Seconds 2 }
  L ('RESTANTES=' + (@(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue).Count))
  $logf = Join-Path $out "uia_${Tag}_log.txt"
  [IO.File]::WriteAllLines($logf, $lines, (New-Object Text.UTF8Encoding($true)))
  Get-Content $logf
}
