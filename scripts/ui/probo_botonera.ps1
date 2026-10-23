# Sonda puntual: por que E3_franja_botones sale vacio en verificar_consola si la franja esta
# pintada. Vuelca los nombres de TODOS los botones de la consola por dos caminos distintos de
# UIA: el elemento que sube del root del escritorio y el que sale de FromHandle(HWND).
# Es temporal: se borra cuando se sepa cual de los dos es el arbol completo.

param([int]$Espera = 14)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$ErrorActionPreference = 'Stop'

$exe = 'C:\Users\CY-KILL\Desktop\Programación y Datanálisis\quasar_radio\bin\Debug\net10.0-windows\quasar_radio.exe'
$AE = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]
$Scope = [System.Windows.Automation.TreeScope]
$bc = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Button)

$proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
try {
  Start-Sleep -Seconds $Espera
  $root = $AE::RootElement
  $byRoot = $null
  foreach ($w in $root.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($w.Current.ProcessId -eq $proc.Id -and $w.Current.Name -like '*Quasar*') { $byRoot = $w }
  }
  if ($null -eq $byRoot) { throw 'sin consola por root' }
  Write-Output ('ROOT  name=' + $byRoot.Current.Name)
  $n = @($byRoot.FindAll($Scope::Descendants, $bc))
  Write-Output ('ROOT  botones=' + $n.Count + ' :: ' + (($n | ForEach-Object { $_.Current.Name }) -join ' | '))

  $hwnd = [int]$byRoot.Current.NativeWindowHandle
  $byHwnd = $AE::FromHandle([IntPtr]$hwnd)
  $m = @($byHwnd.FindAll($Scope::Descendants, $bc))
  Write-Output ('HWND  hwnd=' + $hwnd + ' botones=' + $m.Count + ' :: ' + (($m | ForEach-Object { $_.Current.Name }) -join ' | '))

  $t = @($byHwnd.FindAll($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text))))
  Write-Output ('HWND  textos=' + $t.Count)
  $f = @($m | Where-Object { $_.Current.Name -match 'BIBLIOTECA|STREAMING|PROGRAMAC|REPORTES|DSP' })
  Write-Output ('HWND  franja=' + $f.Count + ' :: ' + (($f | ForEach-Object { $_.Current.Name }) -join ' | '))
  $f2 = @($n | Where-Object { $_.Current.Name -match 'BIBLIOTECA|STREAMING|PROGRAMAC|REPORTES|DSP' })
  Write-Output ('ROOT  franja=' + $f2.Count)
}
finally {
  try { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null } catch { }
  Start-Sleep -Seconds 1
  Write-Output ('RESTANTES=' + @(Get-Process -Name quasar_radio -ErrorAction SilentlyContinue).Count)
}
