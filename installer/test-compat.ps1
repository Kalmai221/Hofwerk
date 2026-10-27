# Tests switching between the two Windows versions of Hofwerk on a temporary install:
#   - the compatibility version (in the "compat" folder) knows it is one, and its App settings offer the way back
#   - pressing that button starts the standard version
#   - when the settings say "prefer the compatibility version", Hofwerk.exe starts that instead, and "--standard" still gives the standard one
# The compatibility version is copied in from the build instead of downloaded (the download and unpacking are covered by unit tests).
# The settings file of the person running the test is saved first and put back afterwards.
#
#   powershell -File installer/test-compat.ps1 -Setup dist\Hofwerk-Setup-0.1.0-win-x64.exe -CompatFolder publish\win-x64\compat
param(
    [Parameter(Mandatory = $true)][string]$Setup,
    [Parameter(Mandatory = $true)][string]$CompatFolder
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\protect-install.ps1"; Save-RealInstall   # keep a real install on this machine intact
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$failures = New-Object System.Collections.Generic.List[string]
function Check([string]$what, [bool]$ok) {
    if ($ok) { Write-Host "  ok    $what" } else { Write-Host "  FAIL  $what"; $failures.Add($what) }
}

$Setup = (Resolve-Path $Setup).Path
$CompatFolder = (Resolve-Path $CompatFolder).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ("hofwerk-compattest-" + [guid]::NewGuid().ToString('N'))
$dir = Join-Path $work 'installed'
$settingsFile = Join-Path $env:APPDATA 'Hofwerk\settings.json'
$savedSettings = if (Test-Path $settingsFile) { [IO.File]::ReadAllBytes($settingsFile) } else { $null }
$env:HOFWERK_NO_UPDATE_CHECK = '1'
New-Item -ItemType Directory -Force $work | Out-Null

$AE = [System.Windows.Automation.AutomationElement]
function Find-Named($root, [string]$name) { $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name))) }
function Press($root, [string]$name) {
    $el = Find-Named $root $name
    for ($d = 0; $el -and $d -lt 4; $d++) {
        $p = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return $true }
        if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return $true }
        $el = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($el)
    }
    return $false
}
function Wait-Named($root, [string]$name, [int]$seconds = 10) {
    for ($i = 0; $i -lt $seconds * 4; $i++) { if (Find-Named $root $name) { return $true }; Start-Sleep -Milliseconds 250 }
    return $false
}
# Hofwerk processes that were started from the temporary install, by the full path of their program.
function Running-From([string]$exe) {
    @(Get-Process -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -ieq $exe } catch { $false } })
}
function Wait-Process-From([string]$exe, [int]$seconds = 25) {
    for ($i = 0; $i -lt $seconds * 2; $i++) { $found = Running-From $exe; if ($found.Count -gt 0) { return $found }; Start-Sleep -Milliseconds 500 }
    return @()
}
function Stop-All-From([string]$folder) {
    Get-Process -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -like "$folder*" } catch { $false } } | Stop-Process -Force -ErrorAction SilentlyContinue
}

try {
    $p = Start-Process $Setup -ArgumentList '--silent', '--dir', "`"$dir`"" -Wait -PassThru
    Check 'a test install is made' ($p.ExitCode -eq 0 -and (Test-Path "$dir\Hofwerk.exe"))

    Copy-Item $CompatFolder "$dir\compat" -Recurse
    $compatExe = "$dir\compat\hofwerk.exe"
    $standardExe = "$dir\Hofwerk.exe"
    Check 'the compatibility version is beside the standard one' (Test-Path $compatExe)

    # ---- the compatibility version offers the way back ----
    $c = Start-Process $compatExe -PassThru
    for ($i = 0; $i -lt 30 -and $c.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 500; $c.Refresh() }
    Check 'the compatibility version starts' ($c.MainWindowHandle -ne 0)
    $root = $AE::FromHandle($c.MainWindowHandle)
    Check 'its App settings can be opened' (Press $root 'App settings')
    Check 'App settings offer "Use the standard Windows version"' (Wait-Named $root 'Use the standard Windows version')

    Check 'pressing it works' (Press $root 'Use the standard Windows version')
    $std = Wait-Process-From $standardExe
    Check 'the standard version starts' ($std.Count -gt 0)
    Start-Sleep -Seconds 2
    $c.Refresh()
    Check 'the compatibility version closes itself' ($c.HasExited)
    Stop-All-From $dir
    Start-Sleep -Seconds 1

    # ---- "prefer the compatibility version" in the settings ----
    New-Item -ItemType Directory -Force (Split-Path $settingsFile) | Out-Null
    [IO.File]::WriteAllText($settingsFile, '{ "PreferCompatibilityVersion": true }')
    $s = Start-Process $standardExe -PassThru
    $cv = Wait-Process-From $compatExe
    Check 'Hofwerk.exe starts the compatibility version when that is preferred' ($cv.Count -gt 0)
    Start-Sleep -Seconds 2
    $s.Refresh()
    Check 'and the standard one closes' ($s.HasExited)
    Stop-All-From $dir
    Start-Sleep -Seconds 1

    $s2 = Start-Process $standardExe -ArgumentList '--standard' -PassThru
    for ($i = 0; $i -lt 30 -and $s2.MainWindowHandle -eq 0 -and -not $s2.HasExited; $i++) { Start-Sleep -Milliseconds 500; $s2.Refresh() }
    Start-Sleep -Seconds 2
    $s2.Refresh()
    Check '"--standard" gives the standard version even when the other is preferred' (-not $s2.HasExited -and $s2.MainWindowHandle -ne 0)
    Check 'and starts no compatibility version' ((Running-From $compatExe).Count -eq 0)
    Stop-All-From $dir
}
catch {
    Write-Host "  FAIL  $($_.Exception.Message)"
    $failures.Add($_.Exception.Message)
}
finally {
    Stop-All-From $work
    Start-Sleep -Seconds 1
    # the settings of the person running the test, as they were
    if ($null -ne $savedSettings) { [IO.File]::WriteAllBytes($settingsFile, $savedSettings) } else { Remove-Item $settingsFile -ErrorAction SilentlyContinue }
    Restore-RealInstall
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) { Write-Host "`n$($failures.Count) check(s) failed."; exit 1 }
Write-Host "`nSwitching between the two Windows versions works."
