# Installs, updates and uninstalls a built Linux installer and fails if anything is off.
#   pwsh installer/test-installer-linux.ps1 -Archive dist/Hofwerk-Setup-0.1.0-linux-x64.tar.gz
# Meant for a CI runner or a throwaway machine: it writes to ~/.local (menu entry, icon, command link) and
# ~/.config/Hofwerk, and removes what it added. An existing settings file is put back as it was.
param(
    [Parameter(Mandatory = $true)][string]$Archive
)
$ErrorActionPreference = 'Stop'

if (-not $IsLinux) { throw 'This test is for Linux. On Windows use installer/test-installer.ps1.' }

$Archive = (Resolve-Path $Archive).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ("hofwerk-linux-test-" + [guid]::NewGuid().ToString('N'))
$dir = Join-Path $work 'hofwerk'
$fakeOmsi = Join-Path $work 'OMSI 2'
New-Item -ItemType Directory -Force $work | Out-Null

# Where the installer puts things. These mirror LinuxInstaller.
$dataHome = [Environment]::GetFolderPath('LocalApplicationData')
$menuEntry = Join-Path $dataHome 'applications/hofwerk.desktop'
$iconFile = Join-Path $dataHome 'icons/hicolor/256x256/apps/hofwerk.png'
$settingsFile = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Hofwerk/settings.json'
$settingsBackup = if (Test-Path $settingsFile) { Get-Content $settingsFile -Raw } else { $null }
$failures = New-Object System.Collections.Generic.List[string]

function Check([string]$what, [bool]$ok) {
    if ($ok) { Write-Host "  ok    $what" } else { Write-Host "  FAIL  $what"; $failures.Add($what) }
}

function Is-Executable([string]$path) {
    & test -x $path
    return $LASTEXITCODE -eq 0
}

try {
    Write-Host "Archive: $Archive ($([math]::Round((Get-Item $Archive).Length / 1MB, 1)) MB)"

    Write-Host '0. Unpack'
    & tar -xzf $Archive -C $work
    $setup = Get-ChildItem $work -File | Where-Object { $_.Name -like 'Hofwerk-Setup-*' } | Select-Object -First 1
    Check 'the archive holds one setup file' ($null -ne $setup)
    Check 'the setup file is executable (the tarball keeps the permission)' (Is-Executable $setup.FullName)

    Write-Host '1. Install'
    New-Item -ItemType Directory -Force (Join-Path $fakeOmsi 'Vehicles'), (Join-Path $fakeOmsi 'Maps') | Out-Null
    Remove-Item $settingsFile -ErrorAction SilentlyContinue
    & $setup.FullName --silent --dir $dir --omsi-folder $fakeOmsi --desktop-shortcut
    Check 'installer exits with 0' ($LASTEXITCODE -eq 0)
    Check 'the app is in the install folder' (Test-Path "$dir/hofwerk")
    Check 'the app is executable' (Is-Executable "$dir/hofwerk")
    Check 'the uninstaller is in the install folder' (Test-Path "$dir/uninstall")
    Check 'the uninstaller is executable' (Is-Executable "$dir/uninstall")
    Check 'the install manifest exists' (Test-Path "$dir/install.manifest")
    Check 'the icon is in the install folder' (Test-Path "$dir/hofwerk.png")

    Check 'the applications-menu entry exists' (Test-Path $menuEntry)
    if (Test-Path $menuEntry) {
        $entry = Get-Content $menuEntry -Raw
        Check 'the menu entry starts the installed app' ($entry -match [regex]::Escape("Exec=`"$dir/hofwerk`""))
        Check 'the menu entry has an Uninstall action' ($entry -match '\[Desktop Action Uninstall\]')
    }
    Check 'the icon was added to the icon theme' (Test-Path $iconFile)
    Check 'the OMSI folder was saved for the app' ((Test-Path $settingsFile) -and ((Get-Content $settingsFile -Raw) -match [regex]::Escape('OMSI 2')))

    Write-Host '2. Update over the existing install'
    New-Item -ItemType File -Force "$dir/not-from-the-installer.txt" | Out-Null
    & $setup.FullName --silent --dir $dir
    Check 'update exits with 0' ($LASTEXITCODE -eq 0)
    Check 'files the installer did not write are kept' (Test-Path "$dir/not-from-the-installer.txt")
    Check 'the app is still there' (Test-Path "$dir/hofwerk")

    Write-Host '3. A folder that is not empty and not ours is refused'
    $foreign = Join-Path $work 'documents'
    New-Item -ItemType Directory -Force $foreign | Out-Null
    Set-Content (Join-Path $foreign 'thesis.txt') 'precious'
    & $setup.FullName --silent --dir $foreign
    Check 'the installer refuses it (non-zero exit)' ($LASTEXITCODE -ne 0)
    Check 'the other files are untouched' ((Get-ChildItem $foreign).Count -eq 1)

    Write-Host '4. Uninstall'
    & "$dir/uninstall" --silent --uninstall
    Check 'uninstaller exits with 0' ($LASTEXITCODE -eq 0)
    Check 'the app is gone' (-not (Test-Path "$dir/hofwerk"))
    Check 'the uninstaller is gone' (-not (Test-Path "$dir/uninstall"))
    Check 'the menu entry is gone' (-not (Test-Path $menuEntry))
    Check 'the icon is gone from the icon theme' (-not (Test-Path $iconFile))
    Check 'files the installer did not write survive' (Test-Path "$dir/not-from-the-installer.txt")
    Check 'the settings file is kept (it holds the user''s choices)' (Test-Path $settingsFile)
}
finally {
    Remove-Item $menuEntry -ErrorAction SilentlyContinue
    Remove-Item $iconFile -ErrorAction SilentlyContinue
    Remove-Item (Join-Path ([Environment]::GetFolderPath('UserProfile')) '.local/bin/hofwerk') -ErrorAction SilentlyContinue
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

    if ($null -ne $settingsBackup) { Set-Content $settingsFile $settingsBackup -NoNewline }
    else { Remove-Item $settingsFile -ErrorAction SilentlyContinue }
}

if ($failures.Count -gt 0) {
    Write-Host "`n$($failures.Count) check(s) failed."
    exit 1
}
Write-Host "`nAll checks passed."
