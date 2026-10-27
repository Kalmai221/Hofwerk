# Dot-sourced by the installer tests. The tests install and remove copies of Hofwerk, and Windows keeps one "Hofwerk"
# entry per user (Installed apps, Start menu, "Open with"). If Hofwerk is really installed on the machine running the tests,
# its entries are saved first and put back by Restore-RealInstall, which each script calls in its finally block.
#
#   . "$PSScriptRoot\protect-install.ps1"; Save-RealInstall      (at the start)
#   Restore-RealInstall                                            (in the finally block)
$script:HofwerkSaved = $null

$script:HofwerkKeys = @(
    @{ File = 'uninstall.reg'; Key = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Hofwerk' },
    @{ File = 'class.reg';     Key = 'HKCU\Software\Classes\Hofwerk.hof' },
    @{ File = 'openwith.reg';  Key = 'HKCU\Software\Classes\.hof\OpenWithProgids' }
)

function Restore-RealInstall([string]$Dir = $script:HofwerkSaved) {
    $dir = $Dir
    if (-not $dir -or -not (Test-Path $dir)) { return }
    $script:HofwerkSaved = $null
    foreach ($k in $script:HofwerkKeys) {
        $file = Join-Path $dir $k.File
        if (Test-Path $file) {
            if ($k.File -ne 'openwith.reg') { cmd /c "reg delete ""$($k.Key)"" /f >nul 2>&1" }
            cmd /c "reg import ""$file"" >nul 2>&1"
        }
    }
    $lnk = Join-Path $dir 'Hofwerk.lnk'
    if (Test-Path $lnk) { Copy-Item $lnk (Join-Path ([Environment]::GetFolderPath('Programs')) 'Hofwerk.lnk') -Force }
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Env:\HOFWERK_TEST_PROTECTED -ErrorAction SilentlyContinue
    $global:LASTEXITCODE = 0   # reg.exe exits with 1 when a key is missing; that must not become the script's exit code
}

function Save-RealInstall {
    if ($env:HOFWERK_TEST_PROTECTED) { return }   # an outer script already did
    $env:HOFWERK_TEST_PROTECTED = '1'
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('hofwerk-realinstall-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($k in $script:HofwerkKeys) {
        cmd /c "reg export ""$($k.Key)"" ""$(Join-Path $dir $k.File)"" /y >nul 2>&1"
    }
    $lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Hofwerk.lnk'
    if (Test-Path $lnk) { Copy-Item $lnk (Join-Path $dir 'Hofwerk.lnk') }
    $script:HofwerkSaved = $dir
    $global:LASTEXITCODE = 0   # reg.exe exits with 1 when there is nothing to export (a machine without Hofwerk)
}
