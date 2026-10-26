# Builds the one-file Hofwerk installer for a platform:
#   publish the app -> publish the setup program -> pack the app into a copy of the setup program.
#
#   pwsh installer/build-installer.ps1 -Rid win-x64 -Version 0.1.0
#   pwsh installer/build-installer.ps1 -Rid linux-x64 -Version 0.1.0
#
# Works in Windows PowerShell 5.1 and PowerShell 7. The result is written to dist/.
param(
    # The setup program has to run on the machine that builds it (it packs the app into itself), so only x64 for now.
    [Parameter(Mandatory = $true)][ValidateSet('win-x64', 'linux-x64')][string]$Rid,
    [string]$Version = '0.0.0',
    [string]$Output = 'dist',
    # Windows only: by default the published app is started once and the build fails if no window appears.
    [switch]$SkipSmokeTest
)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$isWindows = $Rid -like 'win-*'
$appProject = if ($isWindows) { 'src/Hofwerk.WinUI/Hofwerk.WinUI.csproj' } else { 'src/Hofwerk.Linux/Hofwerk.Linux.csproj' }
$ext = if ($isWindows) { '.exe' } else { '' }

$work = Join-Path $root "publish/$Rid"
$dist = Join-Path $root $Output
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $work, $dist | Out-Null

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

Write-Host "==> Publishing the app ($Rid)"
Invoke-Dotnet publish (Join-Path $root $appProject) -c Release -r $Rid --self-contained true "-p:Version=$Version" -p:DebugType=None -o (Join-Path $work 'app')

if ($isWindows -and -not $SkipSmokeTest) {
    # A published WinUI app can start as a process and still be broken (missing XAML resources), so use it for real:
    # open a file, visit every page and save. See installer/check-app.ps1.
    Write-Host '==> Checking the published app'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check-app.ps1') -Exe (Join-Path $work 'app/Hofwerk.exe')
    if ($LASTEXITCODE -ne 0) { throw 'The published app failed its checks.' }
}

Write-Host '==> Publishing the setup program'
Invoke-Dotnet publish (Join-Path $root 'src/Hofwerk.Installer/Hofwerk.Installer.csproj') -c Release -r $Rid --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    "-p:Version=$Version" -p:DebugType=None -o (Join-Path $work 'setup')

Write-Host '==> Packing the app into the setup program (solid Brotli, this takes a minute)'
$setup = Join-Path $work "setup/Hofwerk.Setup$ext"
$final = Join-Path $dist "Hofwerk-Setup-$Version-$Rid$ext"
# The Windows build is a GUI program, so wait for it explicitly.
$p = Start-Process -FilePath $setup -ArgumentList '--attach', "`"$(Join-Path $work 'app')`"", "`"$final`"" -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Packing the app failed with exit code $($p.ExitCode)" }

if (-not $isWindows) {
    & chmod +x $final
    # A tarball keeps the executable bit, which a plain download from a release page would lose.
    & tar -czf "$final.tar.gz" -C $dist (Split-Path $final -Leaf)
    Remove-Item $final
    $final = "$final.tar.gz"
}

Write-Host ("==> Done: {0} ({1:N1} MB; setup program alone {2:N1} MB)" -f $final, ((Get-Item $final).Length / 1MB), ((Get-Item $setup).Length / 1MB))
