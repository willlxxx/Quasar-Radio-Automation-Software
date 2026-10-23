param([string]$Theme = 'Studio Dark Neon', [string]$Tag = 'ui3b', [string]$Solo = '', [switch]$SkipDsp)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

if (-not ('QsrMod' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class ModWin { public IntPtr Hwnd; public string Title; public bool Visible; public string Rect; public int L; public int T; public int W; public int H; }
public static class QsrMod {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
  static int _target; static List<ModWin> _found;
  public static List<ModWin> Top(int pid) {
    _target = pid; _found = new List<ModWin>();
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == _target) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        var wi = new ModWin();
        wi.Hwnd = h; wi.Title = sb.ToString(); wi.Visible = IsWindowVisible(h);
        wi.L = r.Left; wi.T = r.Top; wi.W = r.Right - r.Left; wi.H = r.Bottom - r.Top;
        wi.Rect = r.Left + "," + r.Top + " " + wi.W + "x" + wi.H;
        _found.Add(wi);
      }
      return true;
    }, IntPtr.Zero);
    return _found;
  }
  public static void Click(int x, int y) {
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(120);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);   // LEFTDOWN
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);   // LEFTUP
  }
  public static void Close(IntPtr h) { SendMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); } // WM_CLOSE
}
'@
}

function Find-Button($root, $pattern) {
  $bc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  $hit = $null
  foreach ($b in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
    if ($b.Current.Name -match $pattern) { $hit = $b }
  }
  return $hit
}

function Find-Any($root, $pattern, $ct) {
  $cc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
  $hits = @()
  foreach ($e in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cc)) {
    if ($e.Current.Name -match $pattern) { $hits += $e }
  }
  return $hits
}

function RectOf($e) {
  $r = $e.Current.BoundingRectangle
  return [pscustomobject]@{ X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
}

function Wins($rootPid) {
  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $all = @()
  foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($w.Current.ProcessId -eq $rootPid) { $all += $w }
  }
  return $all
}

$exe = 'C:\Users\CY-KILL\Desktop\Programación y Datanálisis\quasar_radio\bin\Debug\net10.0-windows\quasar_radio.exe'
$bin = Split-Path $exe
$out = Join-Path $env:TEMP 'qsr_modulos'
New-Item -ItemType Directory -Force -Path $out | Out-Null

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
  L "CAP $t : ${w}x${h}"
}

# PrintWindow pinta la ventana aunque otra este delante: con el raton real y el usuario
# trabajando, CopyFromScreen capturo el navegador por encima de la ventana del modulo.
function CapWindow($win, $t) {
  if ($null -eq $win) { L "CAP $t : sin ventana"; return }
  $w = [int]$win.W; $h = [int]$win.H
  if ($w -lt 50 -or $h -lt 50) { L "CAP $t : rect invalida"; return }
  $bmp = New-Object Drawing.Bitmap($w, $h)
  $g = [Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  $ok = [QsrMod]::PrintWindow([IntPtr]$win.Hwnd, $hdc, 2)   # PW_RENDERFULLCONTENT
  $g.ReleaseHdc($hdc)
  $g.Dispose()
  $path = Join-Path $out "$t.png"
  $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  L "CAP $t : PrintWindow=$ok ${w}x${h}"
}

$pre = @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue)
if ($pre.Count -gt 0) { throw "Ya hay $($pre.Count) instancia(s) abiertas; abortando." }

# La geometria recordada de esta prueba no debe heredar la de una sesion anterior.
$layout = Join-Path $env:APPDATA 'QuasarRadio\layout.json'
if (Test-Path $layout) { Remove-Item $layout -Force }

$proc = Start-Process -FilePath $exe -WorkingDirectory $bin -PassThru
try {
  Start-Sleep -Seconds 10
  Set-Content -Path (Join-Path $env:APPDATA 'QuasarRadio\appearance.json') -Value ('{"theme":"' + $Theme + '"}') -Encoding UTF8

  $console = [QsrMod]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like '*Quasar*' } | Select-Object -First 1
  if ($null -eq $console) { throw 'sin ventana de consola' }
  L ('CONSOLA: ' + $console.Rect)

  $main = $null
  foreach ($w in (Wins $proc.Id)) { if ($w.Current.Name -like 'Quasar Radio Pro*') { $main = $w } }
  if ($null -eq $main) { throw 'sin ventana principal (UIA)' }

  # ---------- la franja de modulos existe y tiene sus cinco botones
  $mods = [ordered]@{
    Streaming = @{ pattern = 'STREAMING';   title = 'Streaming y';  probe = 'Salida del enlace' }
    Schedule  = @{ pattern = 'PROGRAMAC';   title = 'Programac';    probe = 'Pautas y' }
    Reports   = @{ pattern = 'REPORTES';    title = 'Reportes';     probe = 'TRACKS EN SES' }
  }
  if ($Solo -ne '') {
    foreach ($k in @($mods.Keys)) { if (($Solo -split ',') -notcontains $k) { [void]$mods.Remove($k) } }
    L ('SOLO=' + (($mods.Keys) -join ','))
  }
  $missing = @()
  foreach ($k in $mods.Keys) {
    $b = Find-Button $main $mods[$k].pattern
    if ($null -eq $b) { $missing += $k }
    else { L ("BOTON $k : rect=" + ((RectOf $b).X) + ',' + ((RectOf $b).Y) + ' ' + ((RectOf $b).W) + 'x' + ((RectOf $b).H)) }
  }
  L ("FRANJA_botones_faltantes=" + ($missing -join ','))

  CapWindow $console "${Tag}_consola"

  # ---------- abrir cada modulo y comprobar su ventana
  # Se usa InvokePattern de UIA: el clic de raton real se pierde cuando alguien mueve el
  # puntero mientras el script espera sus 120 ms, y el fallo se parece a un boton roto.
  foreach ($k in $mods.Keys) {
    $b = Find-Button $main $mods[$k].pattern
    if ($null -eq $b) { L ("ABRIR $k : boton no encontrado"); continue }
    $r = RectOf $b

    $mw = $null
    for ($try = 1; $try -le 2 -and $null -eq $mw; $try++) {
      if ($try -eq 1) {
        try {
          ($b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
          L ("INVOCADO $k")
        } catch { L ("INVOKE fallo $k : " + $_.Exception.Message) }
      } else {
        [void][QsrMod]::SetForegroundWindow([IntPtr]$console.Hwnd)
        Start-Sleep -Milliseconds 900
        [QsrMod]::Click($r.X + [int]($r.W / 2), $r.Y + [int]($r.H / 2))
        L ("RATON $k : plan B en " + ($r.X + [int]($r.W / 2)) + ',' + ($r.Y + [int]($r.H / 2)))
      }
      Start-Sleep -Seconds 3
      $mw = [QsrMod]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like ('*' + $mods[$k].title + '*') } | Select-Object -First 1
      if ($null -eq $mw) { L ("INTENTO $try $k : sin ventana") }
    }
    if ($null -eq $mw) { L ("ABRIR $k : NO HAY VENTANA"); continue }
    L ("VENTANA $k : " + $mw.Title + ' | ' + $mw.Rect)
    CapWindow $mw "${Tag}_$k"

    # la franja tiene que marcar el boton del modulo abierto (borde de acento)
    $cf = [QsrMod]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like '*Quasar Radio Pro*' } | Select-Object -First 1
    CapWindow $cf "${Tag}_franja_$k"

    [QsrMod]::Close([IntPtr]$mw.Hwnd)
    Start-Sleep -Seconds 2
    $still = @([QsrMod]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like ('*' + $mods[$k].title + '*') }).Count
    L ("CIERRE $k : desaparecio=" + ($still -eq 0))

    # cerrar el modulo no cierra la emisora
    $consolaSigue = @([QsrMod]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like '*Quasar Radio Pro*' }).Count
    L ("CONSOLA_VIVA_DESPUES_DE_CERRAR_$k = " + ($consolaSigue -gt 0))
  }

  # ---------- AUTH-1: DSP abre SIN clave. Antes esta prueba comprobaba lo contrario
  # (DSP_pide_acceso=True y sin ventana). Desde que la contraseña solo atribuye acciones, la
  # ventana del procesador tiene que abrirse de una con sesion de invitado; el nivel se pide
  # al pulsar una operacion, y eso se comprueba en verificar_permisos.ps1.
  $bdsp = Find-Button $main 'DSP'
  if ($SkipDsp) { L 'DSP : prueba de apertura omitida (-SkipDsp)' }
  elseif ($null -eq $bdsp) { L 'BOTON DSP : no encontrado' }
  else {
    $r = RectOf $bdsp
    [void][QsrMod]::SetForegroundWindow([IntPtr]$console.Hwnd)
    Start-Sleep -Milliseconds 900
    [QsrMod]::Click(($r.X + [int]($r.W / 2)), ($r.Y + [int]($r.H / 2)))
    Start-Sleep -Seconds 3
    $login = [QsrMod]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like 'Quasar Radio | *' } | Select-Object -First 1
    $dspWin = [QsrMod]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like '*Procesador DSP*' } | Select-Object -First 1
    L ('DSP_abre_sin_clave=' + ($null -ne $dspWin) + ' | modal_de_identificacion=' + ($null -ne $login) + ' | ' + $dspWin.Rect)
    if ($null -ne $dspWin) {
      CapWindow $dspWin "${Tag}_dsp_sin_login"
      [QsrMod]::Close([IntPtr]$dspWin.Hwnd)
      Start-Sleep -Seconds 2
      $siguen = @([QsrMod]::Top($proc.Id) | Where-Object { $_.Visible -and $_.Title -like '*Quasar Radio Pro*' }).Count
      L ('CONSOLA_VIVA_DESPUES_DE_CERRAR_DSP=' + ($siguen -gt 0))
    }
  }

  # ---------- geometria recordada
  Start-Sleep -Seconds 2
  if (Test-Path $layout) {
    L ('LAYOUT=' + ((Get-Content $layout -Raw) -replace '\s+', ' '))
  } else { L 'LAYOUT=no existe' }
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
  $logf = Join-Path $out "uia_${Tag}_modulos.txt"
  [IO.File]::WriteAllLines($logf, $lines, (New-Object Text.UTF8Encoding($true)))
  Get-Content $logf
}
