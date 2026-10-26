# Installs, launches, updates and uninstalls a built Windows installer, and fails if anything is off.
#   powershell -File installer/test-installer.ps1 -Setup dist/Hofwerk-Setup-0.1.0-win-x64.exe
# It touches the current user's Start menu and "Installed apps" list for a moment and removes both again.
param(
    [Parameter(Mandatory = $true)][string]$Setup
)
$ErrorActionPreference = 'Stop'

$Setup = (Resolve-Path $Setup).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ("hofwerk-installer-test-" + [guid]::NewGuid().ToString('N'))
$dir = Join-Path $work 'Hofwerk'
New-Item -ItemType Directory -Force $work | Out-Null

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Hofwerk'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Hofwerk.lnk'
$failures = New-Object System.Collections.Generic.List[string]

function Check([string]$what, [bool]$ok) {
    if ($ok) { Write-Host "  ok    $what" } else { Write-Host "  FAIL  $what"; $failures.Add($what) }
}

# Windows GUI programs return at once from & and Start-Process without -Wait, so always wait.
function Run([string]$exe, [string[]]$arguments) {
    (Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru).ExitCode
}

# Starts the app and waits for a window title matching $expect; returns the last title seen (empty if no window).
function Launch-App([string]$exe, [string]$file, [string]$expect) {
    $p = if ($file) { Start-Process $exe -ArgumentList "`"$file`"" -PassThru } else { Start-Process $exe -PassThru }
    $title = ''
    for ($i = 0; $i -lt 25 -and ($title -notlike $expect); $i++) {
        Start-Sleep -Seconds 1
        $p.Refresh()
        if ($p.HasExited) { break }
        $title = $p.MainWindowTitle
    }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    return $title
}

try {
    $sample = Join-Path $work 'sample.hof'
    Set-Content $sample "[name]`nTest`n`nstringcount_terminus`n1`n`nstringcount_busstop`n0`n`n[addterminus]`n001`nTotnes`nTotnes`n"

    Write-Host "Installer: $Setup ($([math]::Round((Get-Item $Setup).Length / 1MB, 1)) MB)"

    Write-Host '1. Install'
    Check 'installer exits with 0' ((Run $Setup @('--silent', '--dir', "`"$dir`"", '--file-association')) -eq 0)
    Check 'app is in the install folder' (Test-Path "$dir\Hofwerk.exe")
    Check 'uninstaller is in the install folder' (Test-Path "$dir\Uninstall.exe")
    Check 'Start menu shortcut exists' (Test-Path $startMenu)
    Check 'appears in Installed apps' ((Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue).DisplayName -eq 'Hofwerk')
    Check 'Open with is registered' (Test-Path 'HKCU:\Software\Classes\Hofwerk.hof\shell\open\command')

    Write-Host '2. The installed app really starts'
    $title = Launch-App "$dir\Hofwerk.exe" $null '*Hofwerk*'
    Check "window appears (title '$title')" ($title -like '*Hofwerk*')
    $title = Launch-App "$dir\Hofwerk.exe" $sample 'sample.hof*'
    Check "opens a .hof from the command line (title '$title')" ($title -like 'sample.hof*')

    Write-Host '3. Update over the existing install'
    Set-Content "$dir\not-from-the-installer.txt" 'keep me'
    Check 'update exits with 0' ((Run $Setup @('--silent', '--dir', "`"$dir`"")) -eq 0)
    Check 'files the installer did not write are kept' (Test-Path "$dir\not-from-the-installer.txt")
    $title = Launch-App "$dir\Hofwerk.exe" $null '*Hofwerk*'
    Check "updated app still starts (title '$title')" ($title -like '*Hofwerk*')

    Write-Host '4. Uninstall'
    Check 'uninstaller exits with 0' ((Run "$dir\Uninstall.exe" @('--silent', '--uninstall')) -eq 0)
    Start-Sleep -Seconds 5   # the uninstaller removes itself shortly after it exits
    Check 'app is gone' (-not (Test-Path "$dir\Hofwerk.exe"))
    Check 'uninstaller is gone' (-not (Test-Path "$dir\Uninstall.exe"))
    Check 'Start menu shortcut is gone' (-not (Test-Path $startMenu))
    Check 'Installed apps entry is gone' (-not (Test-Path $uninstallKey))
    Check 'Open with registration is gone' (-not (Test-Path 'HKCU:\Software\Classes\Hofwerk.hof'))
    Check 'files the installer did not write survive' (Test-Path "$dir\not-from-the-installer.txt")
    $left = @(Get-ChildItem $dir -ErrorAction SilentlyContinue | ForEach-Object Name)
    Check "nothing else is left behind ($($left -join ', '))" ($left.Count -eq 1)
}
finally {
    # Remove anything a failed run could have left registered.
    Remove-Item $startMenu -ErrorAction SilentlyContinue
    Remove-Item $uninstallKey -Recurse -ErrorAction SilentlyContinue
    Remove-Item 'HKCU:\Software\Classes\Hofwerk.hof' -Recurse -ErrorAction SilentlyContinue
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host "`n$($failures.Count) check(s) failed."
    exit 1
}
Write-Host "`nAll checks passed."
