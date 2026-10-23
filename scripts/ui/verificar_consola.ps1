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
  $hdc = $g.GetHdc()
  $ok = [QsrWin]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)   # PW_RENDERFULLCONTENT
  $g.ReleaseHdc($hdc)
  $g.Dispose()
  $path = Join-Path $out "$t.png"
  $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  L "CAPW $t : PrintWindow=$ok ${w}x${h} md5=$((Get-FileHash $path -Algorithm MD5).Hash)"
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

function RectOf($e) {
  $r = $e.Current.BoundingRectangle
  return [pscustomobject]@{ X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
}

$pre = @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)
if ($pre.Count -gt 0) { throw "Ya hay $($pre.Count) instancia(s) abiertas; abortando." }

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
    CapWindow $t2 ("$($Tag)_consola_max")
  }

  $mr = RectOf $main
  CapWindow $t ("$($Tag)_consola")

  # ---------- D1: el VU master y el volumen master deben estar pintados dentro de la ventana
  foreach ($pat in @('NIVEL DE SALIDA', 'VOLUMEN MASTER', 'DETECTOR SILENCIO')) {
    $e = Find-Text $main $pat
    if ($null -eq $e) { L ("D1_TXT [$pat] : NO APARECE EN EL ARBOL"); continue }
    $r = RectOf $e
    $dentro = ($r.Y -ge $mr.Y) -and ($r.Y + $r.H -le $mr.Y + $mr.H + 2) -and ($r.X -ge $mr.X)
    L ("D1_TXT [$($e.Current.Name)] rect=" + $r.X + "," + $r.Y + " " + $r.W + "x" + $r.H + " dentro=" + $dentro)
  }

  $vu = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Custom)
  $leds = @()
  foreach ($c in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $vu)) {
    if ($c.Current.ClassName -match 'LedVuMeter') { $leds += $c }
  }
  L ("D1_ledvu_controls=" + $leds.Count)
  foreach ($c in $leds) {
    $r = RectOf $c
    L ("  LED class=" + $c.Current.ClassName + " rect=" + $r.X + "," + $r.Y + " " + $r.W + "x" + $r.H + " visible=" + $c.Current.IsOffscreen)
  }

  # recorte del panel master para la evidencia visual
  $lbl = Find-Text $main 'NIVEL DE SALIDA'
  if ($null -ne $lbl) {
    $r = RectOf $lbl
    $x = [Math]::Max($mr.X, $r.X - 14)
    $y = [Math]::Max($mr.Y, $r.Y - 74)
    $w = [Math]::Min($mr.W, $r.X + 420) - $x
    $h = [Math]::Min($mr.H, $r.Y + 150) - $y
    CapRect $x $y $w $h ("$($Tag)_master")
  }

  # D3: abrir una pestana concreta y recortar su zona de botones
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
