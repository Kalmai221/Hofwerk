# Tests the online installer end to end without needing a published release:
# a copy of it is pointed at the packed app on disk (the same file the release would serve), then
#   - installed silently, and every installed file is compared byte for byte with the published app
#   - put through the normal installer checks (launch, update, uninstall) and window checks
#
#   powershell -File installer/test-web-installer.ps1 -WebSetup dist\Hofwerk-WebSetup-0.1.0-win-x64.exe `
#       -Pack dist\Hofwerk-App-0.1.0-win-x64.hwpack -AppFolder publish\win-x64\app
param(
    [Parameter(Mandatory = $true)][string]$WebSetup,
    [Parameter(Mandatory = $true)][string]$Pack,
    [Parameter(Mandatory = $true)][string]$AppFolder
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\protect-install.ps1"; Save-RealInstall   # keep a real install on this machine intact
$failures = New-Object System.Collections.Generic.List[string]
function Check([string]$what, [bool]$ok) {
    if ($ok) { Write-Host "  ok    $what" } else { Write-Host "  FAIL  $what"; $failures.Add($what) }
}

$WebSetup = (Resolve-Path $WebSetup).Path
$Pack = (Resolve-Path $Pack).Path
$AppFolder = (Resolve-Path $AppFolder).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ("hofwerk-webtest-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null

try {
    # The real online installer would download from GitHub; this copy reads the same bytes from disk.
    $local = Join-Path $work 'Hofwerk-WebSetup-test.exe'
    $url = ([Uri]$Pack).AbsoluteUri
    $p = Start-Process $WebSetup -ArgumentList '--web', "`"$Pack`"", "`"$url`"", '0.0.0-test', "`"$local`"" -Wait -PassThru
    Check 'a test copy of the online installer can be made' ($p.ExitCode -eq 0 -and (Test-Path $local))
    Check 'the online installer does not contain the app' ((Get-Item $local).Length -lt ((Get-Item $Pack).Length / 2 + 1MB + (Get-Item $WebSetup).Length))

    # Silent install, then compare every file with the published app.
    $dir = Join-Path $work 'installed'
    $p = Start-Process $local -ArgumentList '--silent', '--dir', "`"$dir`"" -Wait -PassThru
    Check 'silent install from the online installer succeeds' ($p.ExitCode -eq 0)

    $bad = 0; $count = 0
    foreach ($f in Get-ChildItem $AppFolder -Recurse -File) {
        $rel = $f.FullName.Substring($AppFolder.Length + 1)
        $t = Join-Path $dir $rel
        $count++
        if (-not (Test-Path $t) -or (Get-FileHash $f.FullName).Hash -ne (Get-FileHash $t).Hash) { Write-Host "    differs: $rel"; $bad++ }
    }
    Check "all $count files match the published app" ($bad -eq 0 -and $count -gt 0)
    Check 'the uninstaller is the program without the download address' ((Get-Item (Join-Path $dir 'Uninstall.exe')).Length -lt (Get-Item $local).Length)

    $p = Start-Process (Join-Path $dir 'Uninstall.exe') -ArgumentList '--silent', '--uninstall', '--dir', "`"$dir`"" -Wait -PassThru
    Check 'silent uninstall succeeds' ($p.ExitCode -eq 0 -and -not (Test-Path $dir))

    # A download that does not match must not install anything.
    $bytes = [IO.File]::ReadAllBytes($Pack); $bytes[100] = $bytes[100] -bxor 0xFF
    $broken = Join-Path $work 'broken.hwpack'; [IO.File]::WriteAllBytes($broken, $bytes)
    $badSetup = Join-Path $work 'Hofwerk-WebSetup-broken.exe'
    # The manifest is made for the real pack, then the file it points at is swapped for the damaged copy.
    $swap = Join-Path $work 'swap.hwpack'; Copy-Item $Pack $swap
    $p = Start-Process $WebSetup -ArgumentList '--web', "`"$swap`"", "`"$(([Uri]$swap).AbsoluteUri)`"", '0.0.0-test', "`"$badSetup`"" -Wait -PassThru
    Copy-Item $broken $swap -Force
    $dir2 = Join-Path $work 'installed2'
    $p = Start-Process $badSetup -ArgumentList '--silent', '--dir', "`"$dir2`"" -Wait -PassThru
    Check 'a damaged download is refused' ($p.ExitCode -ne 0 -and -not (Test-Path (Join-Path $dir2 'Hofwerk.exe')))

    # The normal installer checks, on the online installer.
    $env:HOFWERK_NO_UPDATE_CHECK = '1'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'test-installer.ps1') -Setup $local
    Check 'the standard installer test passes on the online installer' ($LASTEXITCODE -eq 0)
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check-setup-ui.ps1') -Setup $local
    Check 'the installer window checks pass on the online installer' ($LASTEXITCODE -eq 0)
}
catch {
    Write-Host "  FAIL  $($_.Exception.Message)"
    $failures.Add($_.Exception.Message)
}
finally {
    Restore-RealInstall
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) { Write-Host "`n$($failures.Count) check(s) failed."; exit 1 }
Write-Host "`nThe online installer works."
