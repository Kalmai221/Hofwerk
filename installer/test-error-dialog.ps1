# Tests the error dialog of the Windows app on a temporary install (where all three buttons are offered):
#   - an error nobody expected (HOFWERK_SIMULATE_ERROR=1) opens the dialog with Restart, Try a different version and Report a bug
#   - "Try a different version" says why it could not go on when there is no compatibility version to download, and the app keeps running
#   - "Restart Hofwerk" starts Hofwerk again and closes this copy
# "Report a bug" is not pressed (it opens the browser); the address it opens is covered by unit tests.
# The crash log of the person running the test is saved first and put back afterwards (the simulated errors are written to it).
#
#   powershell -File installer/test-error-dialog.ps1 -Setup dist\Hofwerk-Setup-0.1.0-win-x64.exe
param([Parameter(Mandatory = $true)][string]$Setup)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\protect-install.ps1"; Save-RealInstall   # keep a real install on this machine intact
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$failures = New-Object System.Collections.Generic.List[string]
function Check([string]$what, [bool]$ok) {
    if ($ok) { Write-Host "  ok    $what" } else { Write-Host "  FAIL  $what"; $failures.Add($what) }
}

$Setup = (Resolve-Path $Setup).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ("hofwerk-errortest-" + [guid]::NewGuid().ToString('N'))
$dir = Join-Path $work 'installed'
$crash = Join-Path $env:LOCALAPPDATA 'Hofwerk\crash.log'
$crashBytes = if (Test-Path $crash) { [IO.File]::ReadAllBytes($crash) } else { $null }
New-Item -ItemType Directory -Force $work | Out-Null

$AE = [System.Windows.Automation.AutomationElement]
function Find-Any($pid_, [string]$name, [switch]$Like) {
    $wins = $AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $pid_)))
    foreach ($w in $wins) {
        $all = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($e in $all) { $n = $e.Current.Name; if (($Like -and $n -like $name) -or (-not $Like -and $n -eq $name)) { return $e } }
    }
    return $null
}
function Wait-Any($pid_, [string]$name, [switch]$Like, [int]$seconds = 20) {
    for ($i = 0; $i -lt $seconds * 4; $i++) { $e = if ($Like) { Find-Any $pid_ $name -Like } else { Find-Any $pid_ $name }; if ($e) { return $e }; Start-Sleep -Milliseconds 250 }
    return $null
}
function Press($el) {
    for ($d = 0; $el -and $d -lt 4; $d++) {
        $p = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return $true }
        $el = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($el)
    }
    return $false
}
function Hofwerk-Processes { @(Get-Process -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -ieq "$dir\Hofwerk.exe" } catch { $false } }) }

try {
    $p = Start-Process $Setup -ArgumentList '--silent', '--dir', "`"$dir`"" -Wait -PassThru
    Check 'a test install is made' ($p.ExitCode -eq 0)

    $env:HOFWERK_SIMULATE_ERROR = '1'
    $env:HOFWERK_NO_UPDATE_CHECK = '1'
    $app = Start-Process "$dir\Hofwerk.exe" -PassThru
    Remove-Item Env:\HOFWERK_SIMULATE_ERROR -ErrorAction SilentlyContinue

    Check 'the error dialog opens' ([bool](Wait-Any $app.Id 'Something went wrong'))
    Check 'it offers "Restart Hofwerk"' ([bool](Find-Any $app.Id 'Restart Hofwerk'))
    Check 'it offers "Try a different version" (this is an installed copy)' ([bool](Find-Any $app.Id 'Try a different version'))
    Check 'it offers "Report a bug"' ([bool](Find-Any $app.Id 'Report a bug'))
    Check 'it names the error' ([bool](Find-Any $app.Id 'InvalidOperationException: Simulated error*' -Like))

    # there is no release with a compatibility version for this build, so it must say so and carry on
    Check 'pressing "Try a different version" works' (Press (Find-Any $app.Id 'Try a different version'))
    $said = Wait-Any $app.Id 'The other version could not be downloaded*' -Like -seconds 30
    Check 'it says the other version could not be downloaded' ([bool]$said)
    if ($said) { Write-Host "          ($($said.Current.Name))" }
    $app.Refresh()
    Check 'Hofwerk keeps running after that' (-not $app.HasExited)
    Check 'the dialog is still there for another choice' ([bool](Find-Any $app.Id 'Restart Hofwerk'))
    Check 'nothing was switched: no compatibility folder appeared' (-not (Test-Path "$dir\compat\hofwerk.exe"))

    # Restart starts Hofwerk again (this one is told to simulate an error too, because it inherits nothing: the variable was removed)
    $oldId = $app.Id
    Check 'pressing "Restart Hofwerk" works' (Press (Find-Any $app.Id 'Restart Hofwerk'))
    $restarted = $null
    for ($i = 0; $i -lt 40 -and -not $restarted; $i++) { Start-Sleep -Milliseconds 500; $restarted = Hofwerk-Processes | Where-Object { $_.Id -ne $oldId } | Select-Object -First 1 }
    Check 'Hofwerk is started again' ([bool]$restarted)
    Start-Sleep -Seconds 2
    $app.Refresh()
    Check 'the first copy closes itself' ($app.HasExited)
    if ($restarted) {
        for ($i = 0; $i -lt 20 -and $restarted.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 500; $restarted.Refresh() }
        Check 'the new copy has its window' ($restarted.MainWindowHandle -ne 0)
    }
}
catch {
    Write-Host "  FAIL  $($_.Exception.Message)"
    $failures.Add($_.Exception.Message)
}
finally {
    Get-Process -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -like "$work*" } catch { $false } } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    # the crash log of the person running the test, as it was (the simulated errors are not real crashes)
    if ($null -ne $crashBytes) { [IO.File]::WriteAllBytes($crash, $crashBytes) } else { Remove-Item $crash -ErrorAction SilentlyContinue }
    Restore-RealInstall
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) { Write-Host "`n$($failures.Count) check(s) failed."; exit 1 }
Write-Host "`nThe error dialog works."
