# One place to run Hofwerk's tests.
#
#   ./test.ps1                          every unit test (about 20 seconds)
#   ./test.ps1 -Area Installer          only one area (several: -Area Installer,Updates)
#   ./test.ps1 -Filter MapImporter      only tests whose name contains this text
#   ./test.ps1 -Slowest 10              also list the ten slowest tests
#   ./test.ps1 -Ui                      also test the built installers and app (Windows; needs dist/ from installer/build-installer.ps1)
#   ./test.ps1 -Ui -UiOnly              only those
#
# Areas: Installer, Updates, Errors, HofFiles, Omsi, App. A test class is put in an area with [Trait("Area", "...")].
# Ends with a summary and a non-zero exit code when something failed. Under GitHub Actions the summary is also written to the job page.
[CmdletBinding()]
param(
    [ValidateSet('Installer', 'Updates', 'Errors', 'HofFiles', 'Omsi', 'App')][string[]]$Area,
    [string]$Filter,
    [int]$Slowest = 0,
    [string]$Configuration = 'Debug',
    [switch]$Ui,
    [switch]$UiOnly
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'tests/Hofwerk.Core.Tests/Hofwerk.Core.Tests.csproj'
$summary = New-Object System.Collections.Generic.List[string]
$failed = $false

function Add-Summary([string]$line) { $summary.Add($line); Write-Host $line }

# ------------------------------------------------------------------------------------------------------------------ unit tests
if (-not $UiOnly) {
    $results = Join-Path ([IO.Path]::GetTempPath()) ("hofwerk-test-" + [guid]::NewGuid().ToString('N'))
    $filters = @()
    if ($Area) { $filters += '(' + (($Area | ForEach-Object { "Area=$_" }) -join '|') + ')' }
    if ($Filter) { $filters += "FullyQualifiedName~$Filter" }

    $arguments = @('test', $project, '-c', $Configuration, '--nologo', '-v', 'q', '--logger', 'trx;LogFileName=results.trx', '--results-directory', $results)
    if ($filters.Count -gt 0) { $arguments += @('--filter', ($filters -join '&')) }

    Write-Host "Running unit tests$(if ($filters.Count) { ': ' + ($filters -join ' and ') })..."
    $watch = [Diagnostics.Stopwatch]::StartNew()
    & dotnet @arguments | Where-Object { $_ -match '(error|warning CS|Failed |Passed!|Failed!)' } | ForEach-Object { Write-Host $_ }
    $exit = $LASTEXITCODE
    $watch.Stop()

    $trx = Join-Path $results 'results.trx'
    if (-not (Test-Path $trx)) {
        Add-Summary "Unit tests: the run did not produce a result (build error?). dotnet exit code $exit."
        $failed = $true
    }
    else {
        [xml]$xml = Get-Content $trx -Raw
        $ns = New-Object Xml.XmlNamespaceManager($xml.NameTable)
        $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $all = @($xml.SelectNodes('//t:UnitTestResult', $ns))
        $bad = @($all | Where-Object { $_.outcome -eq 'Failed' })
        $skipped = @($all | Where-Object { $_.outcome -eq 'NotExecuted' })
        $passed = $all.Count - $bad.Count - $skipped.Count

        Add-Summary ("Unit tests: {0} passed, {1} failed, {2} skipped, in {3:N0} s" -f $passed, $bad.Count, $skipped.Count, $watch.Elapsed.TotalSeconds)
        foreach ($t in $bad) {
            $message = ($t.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns).InnerText -split "`n")[0].Trim()
            Add-Summary "  FAILED  $($t.testName)"
            Add-Summary "          $message"
        }
        if ($Slowest -gt 0) {
            Add-Summary "Slowest tests:"
            $all | Sort-Object { [TimeSpan]::Parse($_.duration) } -Descending | Select-Object -First $Slowest | ForEach-Object {
                Add-Summary ("  {0,6:N2} s  {1}" -f [TimeSpan]::Parse($_.duration).TotalSeconds, $_.testName)
            }
        }
        if ($bad.Count -gt 0 -or $all.Count -eq 0) { $failed = $true }
        if ($all.Count -eq 0) { Add-Summary "  No test ran: check the -Area / -Filter." }
    }
    Remove-Item $results -Recurse -Force -ErrorAction SilentlyContinue
}

# ------------------------------------------------------------------------------------------------------------------- UI tests
# Each script starts the finished installer or app the way a user would. They take a few minutes and use Windows PowerShell 5.1.
if ($Ui -or $UiOnly) {
    if (-not $IsWindows -and $PSVersionTable.PSVersion.Major -ge 6) { throw 'The UI tests are for Windows.' }
    function Newest([string]$pattern) { Get-ChildItem (Join-Path $root $pattern) -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName }
    $setup = Newest 'dist/Hofwerk-Setup-*.exe'
    $web = Newest 'dist/Hofwerk-WebSetup-*.exe'
    $pack = Newest 'dist/Hofwerk-App-*.hwpack'
    if (-not $setup) { throw 'No installer in dist/. Build one first: ./installer/build-installer.ps1 -Rid win-x64 -Version 0.1.0' }

    $suites = [ordered]@{
        'Installer: silent install, launch, update, uninstall' = @('test-installer.ps1', @('-Setup', $setup))
        'Installer window: every step'                         = @('check-setup-ui.ps1', @('-Setup', $setup))
        'Online installer'                                     = @('test-web-installer.ps1', @('-WebSetup', $web, '-Pack', $pack, '-AppFolder', (Join-Path $root 'publish/win-x64/app')))
        'Error dialog'                                         = @('test-error-dialog.ps1', @('-Setup', $setup))
        'Standard and compatibility versions'                  = @('test-compat.ps1', @('-Setup', $setup, '-CompatFolder', (Join-Path $root 'publish/win-x64/compat')))
    }
    $powershell = 'powershell'
    foreach ($name in $suites.Keys) {
        $script, $scriptArgs = $suites[$name]
        if ($Filter -and $name -notlike "*$Filter*" -and $script -notlike "*$Filter*") { continue }
        Write-Host "`n=== $name ==="
        $watch = [Diagnostics.Stopwatch]::StartNew()
        & $powershell -NoProfile -File (Join-Path $root "installer/$script") @scriptArgs
        $ok = $LASTEXITCODE -eq 0
        $watch.Stop()
        Add-Summary ("UI  {0,-7} {1} ({2:N0} s)" -f ($(if ($ok) { 'passed' } else { 'FAILED' })), $name, $watch.Elapsed.TotalSeconds)
        if (-not $ok) { $failed = $true }
    }
}

# ---------------------------------------------------------------------------------------------------------------------- result
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content $env:GITHUB_STEP_SUMMARY ("### Tests`n``````n" + ($summary -join "`n") + "`n``````")
}
Write-Host ''
if ($failed) { Write-Host 'Some tests failed.'; exit 1 }
Write-Host 'All tests passed.'
