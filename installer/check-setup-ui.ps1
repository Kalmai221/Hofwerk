# Drives the installer's own window through Windows UI Automation:
#   Welcome -> "Where is OMSI 2?" -> install -> Done, then the uninstaller the same way.
# It runs the whole thing twice: once accepting the detected OMSI folder and once skipping the question.
# Fails if a screen does not appear, the app is not installed, the OMSI choice is not saved correctly,
# or anything is left behind.
#   powershell -File installer/check-setup-ui.ps1 -Setup dist/Hofwerk-Setup-0.1.0-win-x64.exe
# The Start menu entry and "Installed apps" entry it creates are removed again, and your real Hofwerk
# settings file is put back exactly as it was.
param(
    [Parameter(Mandatory = $true)][string]$Setup
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$Setup = (Resolve-Path $Setup).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ("hofwerk-setup-ui-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null

$AE = [System.Windows.Automation.AutomationElement]
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Hofwerk'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Hofwerk.lnk'
$settingsFile = Join-Path $env:APPDATA 'Hofwerk\settings.json'
$settingsBackup = if (Test-Path $settingsFile) { Get-Content $settingsFile -Raw } else { $null }
$failures = New-Object System.Collections.Generic.List[string]

function Check([string]$what, [bool]$ok) {
    if ($ok) { Write-Host "  ok    $what" } else { Write-Host "  FAIL  $what"; $failures.Add($what) }
}

function Find-Named($root, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Wait-Named($root, [string]$name, [int]$seconds = 10) {
    for ($i = 0; $i -lt $seconds * 4; $i++) {
        if (Find-Named $root $name) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Press($root, [string]$name) {
    $el = Find-Named $root $name
    if (-not $el) { return $false }
    $pattern = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke(); return $true }
    return $false
}

function Start-Window([string]$exe) {
    $p = Start-Process $exe -PassThru
    for ($i = 0; $i -lt 40 -and $p.MainWindowHandle -eq 0 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 500; $p.Refresh() }
    return $p
}

function Saved-OmsiFolder {
    if (-not (Test-Path $settingsFile)) { return $null }
    try { return (Get-Content $settingsFile -Raw | ConvertFrom-Json).OmsiFolder } catch { return $null }
}

function Run-Scenario([string]$answer) {
    $dir = Join-Path $work "Hofwerk-$answer"
    Write-Host "Scenario: answer the OMSI question with '$answer'"
    Remove-Item $settingsFile -ErrorAction SilentlyContinue

    Write-Host '  -- install through the window'
    $p = Start-Window $Setup
    Check 'the window opens' ($p.MainWindowHandle -ne 0)
    $win = $AE::FromHandle($p.MainWindowHandle)
    Check 'welcome screen: "Install Hofwerk"' (Wait-Named $win 'Install Hofwerk')

    # Point the install at a temporary folder through the text box.
    $edit = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
    $valuePattern = $null
    $canType = $edit -and $edit.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern)
    Check 'the install folder box can be filled in' $canType
    if ($canType) { $valuePattern.SetValue($dir) }
    Start-Sleep -Milliseconds 400

    Check 'the Next button can be pressed' (Press $win 'Next')
    Check 'the "Where is OMSI 2?" page appears' (Wait-Named $win 'Where is OMSI 2?')

    $found = [bool](Find-Named $win 'We found OMSI 2 here:')
    $notFound = [bool](Find-Named $win "We couldn't find OMSI 2 automatically.")
    Check 'the page says either "We found OMSI 2" or that it could not' ($found -or $notFound)
    Write-Host "     (OMSI 2 $(if ($found) { 'was found on this machine' } else { 'was not found on this machine' }))"

    if ($found) {
        Check 'it asks "Is this correct?"' ([bool](Find-Named $win 'Is this correct?'))
        Check 'it offers to pick another folder' ([bool](Find-Named $win "No, choose a folder$([char]0x2026)"))
    }
    else {
        Check 'it offers to select the folder' ([bool](Find-Named $win "Select the folder$([char]0x2026)"))
    }
    Check 'it offers to skip' ([bool](Find-Named $win 'Skip'))

    # Back returns to the first page and Next comes back here.
    Check 'Back returns to the first page' ((Press $win 'Back') -and (Wait-Named $win 'Install Hofwerk'))
    Check 'Next goes forward again' ((Press $win 'Next') -and (Wait-Named $win 'Where is OMSI 2?'))

    $expectSaved = $false
    if ($answer -eq 'Yes' -and $found) {
        Check '"Yes, that is correct" can be pressed' (Press $win 'Yes, that is correct')
        $expectSaved = $true
    }
    else {
        Check '"Skip" can be pressed' (Press $win 'Skip')
    }

    Check 'done screen: "Hofwerk is ready"' (Wait-Named $win 'Hofwerk is ready' 90)
    Check 'done screen offers "Launch Hofwerk"' ([bool](Find-Named $win 'Launch Hofwerk'))
    Check 'the app was installed' (Test-Path "$dir\Hofwerk.exe")
    Check 'the uninstaller was installed' (Test-Path "$dir\Uninstall.exe")
    Check 'Start menu entry exists' (Test-Path $startMenu)
    Check 'appears in Installed apps' ((Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue).DisplayName -eq 'Hofwerk')

    $saved = Saved-OmsiFolder
    if ($expectSaved) { Check "the OMSI folder was saved for the app ($saved)" ($saved -and (Test-Path $saved)) }
    else { Check 'nothing was saved about OMSI when the question was skipped' (-not $saved) }

    [void](Press $win 'Close')
    Start-Sleep -Seconds 1
    $p.Refresh(); if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }

    Write-Host '  -- uninstall through the window'
    $p = Start-Window "$dir\Uninstall.exe"
    Check 'the uninstaller window opens' ($p.MainWindowHandle -ne 0)
    $win = $AE::FromHandle($p.MainWindowHandle)
    Check 'uninstall screen: "Remove Hofwerk?"' (Wait-Named $win 'Remove Hofwerk?')
    Check 'the Uninstall button can be pressed' (Press $win 'Uninstall')
    Check 'done screen: "Hofwerk was removed"' (Wait-Named $win 'Hofwerk was removed' 60)
    [void](Press $win 'Close')
    Start-Sleep -Seconds 5   # the uninstaller deletes itself shortly after it exits
    $p.Refresh(); if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    Check 'the app is gone' (-not (Test-Path "$dir\Hofwerk.exe"))
    Check 'the install folder is gone' (-not (Test-Path $dir))
    Check 'Start menu entry is gone' (-not (Test-Path $startMenu))
    Check 'Installed apps entry is gone' (-not (Test-Path $uninstallKey))
}

try {
    Write-Host "Setup: $Setup ($([math]::Round((Get-Item $Setup).Length / 1MB, 1)) MB)"
    Run-Scenario 'Yes'
    Run-Scenario 'Skip'
}
catch {
    Write-Host "  FAIL  $($_.Exception.Message)"
    $failures.Add($_.Exception.Message)
}
finally {
    # Only processes this script started (the setup file itself, or anything run from its temporary install folders).
    # Another Hofwerk window the user has open is left alone.
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ($_.Path -eq $Setup -or $_.Path.StartsWith($work, [StringComparison]::OrdinalIgnoreCase)) } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-Item $startMenu -ErrorAction SilentlyContinue
    Remove-Item $uninstallKey -Recurse -ErrorAction SilentlyContinue
    Remove-Item 'HKCU:\Software\Classes\Hofwerk.hof' -Recurse -ErrorAction SilentlyContinue
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

    # Put the user's own settings back exactly as they were.
    if ($null -ne $settingsBackup) {
        New-Item -ItemType Directory -Force (Split-Path $settingsFile) | Out-Null
        Set-Content $settingsFile $settingsBackup -NoNewline
    }
    else { Remove-Item $settingsFile -ErrorAction SilentlyContinue }
}

if ($failures.Count -gt 0) {
    Write-Host "`n$($failures.Count) check(s) failed."
    exit 1
}
Write-Host "`nThe installer window works end to end."
