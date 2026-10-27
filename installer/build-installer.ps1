# Builds the one-file Hofwerk installer for a platform:
#   publish the app -> publish the setup program -> pack the app into a copy of the setup program.
#
#   pwsh installer/build-installer.ps1 -Rid win-x64 -Version 0.1.0
#   pwsh installer/build-installer.ps1 -Rid linux-x64 -Version 0.1.0
#
# Writes two installers to dist/ (plus the packed app the small one downloads):
#   Hofwerk-Setup-<version>-<rid>      offline: the app is inside the file
#   Hofwerk-WebSetup-<version>-<rid>   online: small, downloads Hofwerk-App-<version>-<rid>.hwpack from the release
#   Hofwerk-App-<version>-<rid>.hwpack(.sha256)   the packed app; the in-app updater downloads just this
# Works in Windows PowerShell 5.1 and PowerShell 7.
param(
    # The setup program has to run on the machine that builds it (it packs the app into itself), so only x64 for now.
    [Parameter(Mandatory = $true)][ValidateSet('win-x64', 'linux-x64')][string]$Rid,
    [string]$Version = '0.0.0',
    [string]$Output = 'dist',
    # The release tag the online installer downloads the app from (a tag is what the release workflow pushes).
    [string]$Tag = '',
    [string]$Repository = 'Kalmai221/Hofwerk',
    # Windows only: by default the published app is started once and the build fails if no window appears.
    [switch]$SkipSmokeTest
)
$ErrorActionPreference = 'Stop'
if (-not $Tag) { $Tag = "v$Version" }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$targetWindows = $Rid -like 'win-*'
$appProject = if ($targetWindows) { 'src/Hofwerk.WinUI/Hofwerk.WinUI.csproj' } else { 'src/Hofwerk.Linux/Hofwerk.Linux.csproj' }
$ext = if ($targetWindows) { '.exe' } else { '' }

$work = Join-Path $root "publish/$Rid"
$dist = Join-Path $root $Output
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $work, $dist | Out-Null

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

Write-Host "==> Publishing the app ($Rid)"
Invoke-Dotnet publish (Join-Path $root $appProject) -c Release -r $Rid --self-contained true "-p:Version=$Version" "-p:DebugType=None" -o (Join-Path $work 'app')

if ($targetWindows -and -not $SkipSmokeTest) {
    # A published WinUI app can start as a process and still be broken (missing XAML resources), so use it for real:
    # open a file, visit every page and save. See installer/check-app.ps1.
    Write-Host '==> Checking the published app'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check-app.ps1') -Exe (Join-Path $work 'app/Hofwerk.exe')
    if ($LASTEXITCODE -ne 0) { throw 'The published app failed its checks.' }
}

Write-Host '==> Publishing the setup program'
Invoke-Dotnet publish (Join-Path $root 'src/Hofwerk.Installer/Hofwerk.Installer.csproj') -c Release -r $Rid --self-contained true `
    "-p:PublishSingleFile=true" "-p:IncludeNativeLibrariesForSelfExtract=true" "-p:EnableCompressionInSingleFile=true" `
    "-p:Version=$Version" "-p:DebugType=None" -o (Join-Path $work 'setup')

Write-Host '==> Packing the app into the setup program (solid Brotli, this takes a minute)'
$setup = Join-Path $work "setup/Hofwerk.Setup$ext"
$final = Join-Path $dist "Hofwerk-Setup-$Version-$Rid$ext"
$packed = Join-Path $dist "Hofwerk-App-$Version-$Rid.hwpack"
# The Windows build is a GUI program, so wait for it explicitly.
$p = Start-Process -FilePath $setup -ArgumentList '--attach', "`"$(Join-Path $work 'app')`"", "`"$final`"", "`"$packed`"" -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Packing the app failed with exit code $($p.ExitCode)" }

# The in-app updater downloads only the packed app and checks it against this checksum.
$hash = (Get-FileHash $packed -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$packed.sha256", "$hash  $(Split-Path $packed -Leaf)`n")

Write-Host '==> Making the online installer (no app inside, it downloads the packed app)'
$web = Join-Path $dist "Hofwerk-WebSetup-$Version-$Rid$ext"
$url = "https://github.com/$Repository/releases/download/$Tag/" + (Split-Path $packed -Leaf)
$p = Start-Process -FilePath $setup -ArgumentList '--web', "`"$packed`"", "`"$url`"", $Version, "`"$web`"" -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Making the online installer failed with exit code $($p.ExitCode)" }

$results = @($final, $web)
if (-not $targetWindows) {
    $results = foreach ($f in $results) {
        & chmod +x $f
        # A tarball keeps the executable bit, which a plain download from a release page would lose.
        & tar -czf "$f.tar.gz" -C $dist (Split-Path $f -Leaf)
        Remove-Item $f
        "$f.tar.gz"
    }
}

foreach ($f in $results) { Write-Host ("==> Done: {0} ({1:N1} MB)" -f $f, ((Get-Item $f).Length / 1MB)) }
Write-Host ("==> The app is {0:N1} MB packed; the setup program alone is {1:N1} MB" -f ((Get-Item $packed).Length / 1MB), ((Get-Item $setup).Length / 1MB))
$final = $results[0]
