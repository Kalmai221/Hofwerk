# Starts the Windows app with a real .hof file and visits every page through Windows UI Automation.
# It fails if the app crashes or a page does not show its content, which catches problems that only appear
# in a published or trimmed build.
#   powershell -File installer/check-app.ps1 -Exe path\to\Hofwerk.exe
param(
    [Parameter(Mandatory = $true)][string]$Exe
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$Exe = (Resolve-Path $Exe).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ("hofwerk-check-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$sample = Join-Path $work 'check.hof'
# One destination, one bus stop and one route, so every editor page has something to show.
Set-Content $sample @"
[name]
Check Depot

[servicetrip]
Not in Service

[global_strings]
6
Cotterell
Cotterell v1.1
Cotterell v1.1
00
00
00

stringcount_terminus
2

stringcount_busstop
1

[addterminus]
001
Totnes
TOTNES
Totnes

[addbusstop]
Paignton
PAIGNTON

[addbusstop]
Totnes
TOTNES

[infosystem_trip]
3501
PAIGNTON >> TOTNES
001
35

[infosystem_busstop_list]
1
Paignton
"@

$AE = [System.Windows.Automation.AutomationElement]
$failures = New-Object System.Collections.Generic.List[string]

function Check([string]$what, [bool]$ok) {
    if ($ok) { Write-Host "  ok    $what" } else { Write-Host "  FAIL  $what"; $failures.Add($what) }
}

function Find-Named($root, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

# Several things can share a name (a caption and the box it labels); this returns the one you can type into.
function Find-Editable($root, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    foreach ($el in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        $vp = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) { return $el }
    }
    return $null
}

function Wait-Named($root, [string]$name, [int]$seconds = 8) {
    for ($i = 0; $i -lt $seconds * 4; $i++) {
        if (Find-Named $root $name) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Select-Item($root, [string]$name) {
    $el = Find-Named $root $name
    # The name can sit on a row's inner content while the row itself is what can be pressed, so look a few levels up.
    for ($depth = 0; $el -and $depth -lt 4; $depth++) {
        $pattern = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { $pattern.Select(); return $true }
        if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke(); return $true }
        $el = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($el)
    }
    return $false
}

$crashLog = Join-Path $env:LOCALAPPDATA 'Hofwerk\crash.log'
Remove-Item $crashLog -ErrorAction SilentlyContinue
$p = $null
try {
    Write-Host "Checking $Exe"
    $p = Start-Process $Exe -ArgumentList "`"$sample`"" -PassThru
    for ($i = 0; $i -lt 30 -and $p.MainWindowHandle -eq 0 -and -not $p.HasExited; $i++) { Start-Sleep -Seconds 1; $p.Refresh() }
    Check 'a window appears' ($p.MainWindowHandle -ne 0)
    if ($p.MainWindowHandle -eq 0) { throw 'no window' }

    $root = $AE::FromHandle($p.MainWindowHandle)

    # The file passed on the command line opens and the app jumps to Settings.
    Check 'opens the file given on the command line' (Wait-Named $root 'General settings')

    $pages = @(
        @{ Nav = 'Home';         Marker = 'Welcome to Hofwerk' },
        @{ Nav = 'String Presets'; Marker = 'String Presets' },
        @{ Nav = 'Settings';     Marker = 'General settings' },
        @{ Nav = 'Destinations'; Marker = 'Code (ident)' },
        @{ Nav = 'Bus stops';    Marker = 'Name' },
        @{ Nav = 'Routes';       Marker = 'Route code' }
    )
    foreach ($page in $pages) {
        $went = Select-Item $root $page.Nav
        Check "'$($page.Nav)' can be opened" $went
        if ($went) {
            Check "'$($page.Nav)' shows its content" (Wait-Named $root $page.Marker)
            $p.Refresh()
            Check "app still running after '$($page.Nav)'" (-not $p.HasExited)
        }
    }

    # Home offers a way to look for a newer version.
    if (Select-Item $root 'Home') {
        Check 'Home has "Check for updates"' (Wait-Named $root 'Check for updates')
    }

    # Route stops are picked from the defined bus stops, not typed.
    if (Select-Item $root 'Routes') {
        Check 'Routes show the "All stops" and "Route stops" lists' ((Wait-Named $root 'All stops') -and (Wait-Named $root 'Route stops'))
        Check 'a defined bus stop can be chosen from the list' (Select-Item $root 'Totnes')
        Check 'the chosen stop can be added to the route' (Select-Item $root 'Add stop to route')
        Check 'the route now has 2 stops' (Wait-Named $root '2 stops')
        Check 'the new stop can be moved up' (Select-Item $root 'Move stop up')
    }

    # The OMSI library behaves like a file explorer. Only checked on a machine that has OMSI installed.
    $vehicles = @("${env:ProgramFiles(x86)}\Steam\steamapps\common\OMSI 2\Vehicles", "$env:ProgramFiles\Steam\steamapps\common\OMSI 2\Vehicles") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($vehicles) {
        $folder = Get-ChildItem $vehicles -Directory | Where-Object { Get-ChildItem $_.FullName -Filter *.hof -File -ErrorAction SilentlyContinue } |
            Sort-Object Name | Select-Object -First 1
        if ($folder -and (Select-Item $root 'Home') -and (Select-Item $root 'OMSI library')) {
            Check 'library starts at "Vehicles"' (Wait-Named $root 'Vehicles')
            Check "library lists the folder '$($folder.Name)' that holds .hof files" (Wait-Named $root $folder.Name)
            if (Select-Item $root $folder.Name) {
                Check "stepping into '$($folder.Name)' updates the path" (Wait-Named $root "Vehicles $([char]0x203A) $($folder.Name)")
                $firstHof = (Get-ChildItem $folder.FullName -Filter *.hof -File | Sort-Object Name | Select-Object -First 1).Name
                Check "the folder's file '$firstHof' is listed" (Wait-Named $root $firstHof)
                Check 'Up goes back to "Vehicles"' ((Select-Item $root 'Up one folder') -and (Wait-Named $root 'Vehicles'))
            }
            else { Check "folder '$($folder.Name)' can be opened" $false }
        }
    }

    # Switching the string preset must work too (it re-labels every slot).
    if (Select-Item $root 'String Presets') {
        Check 'preset library lists the default preset' (Wait-Named $root 'HOF Suite Default')
    }

    # The Settings page shows what the file says, and an edited IBIS code reaches the file.
    function Get-Value($element) {
        $vp = $null
        if ($element -and $element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) { return $vp.Current.Value }
        return $null
    }
    if (Select-Item $root 'Settings') {
        Check "Settings show the file's depot name in 'Display name'" ((Wait-Named $root 'Display name') -and ((Get-Value (Find-Editable $root 'Display name')) -eq 'Check Depot'))
        Check 'Settings have a Folders section' ([bool](Find-Named $root 'Folders'))
        Check 'Settings have an IBIS codes section' ([bool](Find-Named $root 'IBIS codes'))
        $ibis = Find-Editable $root '9xx'
        $vp = $null
        if ($ibis -and $ibis.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) {
            $vp.SetValue('12')
            Start-Sleep -Milliseconds 500
            Check 'an IBIS code can be edited' ((Get-Value (Find-Editable $root '9xx')) -eq '12')
        }
        else { Check 'the 9xx IBIS code box can be found' $false }
    }

    # Saving rewrites the file in OMSI's format and must keep everything in it.
    $before = (Get-Item $sample).LastWriteTimeUtc
    Start-Sleep -Milliseconds 1100
    if (Select-Item $root 'Save') {
        $saved = $false
        for ($i = 0; $i -lt 20 -and -not $saved; $i++) { Start-Sleep -Milliseconds 250; $saved = (Get-Item $sample).LastWriteTimeUtc -gt $before }
        Check 'Save writes the file' $saved
        $text = Get-Content $sample -Raw
        Check 'the saved file keeps the depot, destination, bus stop and route' (($text -match 'Check Depot') -and ($text -match 'TOTNES') -and ($text -match 'PAIGNTON') -and ($text -match '3501'))
        Check 'the saved route has the picked stop, in the new order' ($text -match '\[infosystem_busstop_list\]\r?\n2\r?\nTotnes\r?\nPaignton')
        Check 'the saved file has the folders and the edited IBIS code' ($text -match '\[global_strings\]\r?\n6\r?\nCotterell\r?\nCotterell v1\.1\r?\nCotterell v1\.1\r?\n12\r?\n00\r?\n00')
    }
    else {
        Check "'Save' can be pressed" $false
    }

    $p.Refresh()
    Check 'app still running at the end' (-not $p.HasExited)
    Check 'no crash was logged' (-not (Test-Path $crashLog))
}
catch {
    Write-Host "  FAIL  $($_.Exception.Message)"
    $failures.Add($_.Exception.Message)
}
finally {
    if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    if (Test-Path $crashLog) { Write-Host "crash.log:`n$(Get-Content $crashLog -Raw)" }
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host "`n$($failures.Count) check(s) failed."
    exit 1
}
Write-Host "`nAll pages work."
