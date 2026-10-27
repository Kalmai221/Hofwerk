# Writes the part of CHANGELOG.md for one version to a file, to use as the text of a GitHub release.
# The app shows that text under "What's new" when it finds the update.
#
#   pwsh installer/changelog-section.ps1 -Version V0.1-dev2 -Output release-notes.md
#   -Strict  fails when CHANGELOG.md has nothing written for the version (a release should not go out without notes)
#   -Footer  a file added after the notes, behind a marker the app stops reading at (so it is only for people on GitHub)
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$Changelog = '',
    [string]$Footer = '',
    [switch]$Strict
)
$ErrorActionPreference = 'Stop'
# ($PSScriptRoot is empty inside param() defaults in Windows PowerShell, so the default is worked out here.)
if (-not $Changelog) { $Changelog = Join-Path (Join-Path $PSScriptRoot '..') 'CHANGELOG.md' }

$wanted = $Version -replace '^[vV]', ''
$lines = Get-Content -LiteralPath $Changelog -Encoding UTF8

$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s+\[([^\]]+)\]' -and $Matches[1].Trim() -ieq $wanted) { $start = $i + 1; break }
}

$section = @()
if ($start -ge 0) {
    for ($i = $start; $i -lt $lines.Count; $i++) {
        # the next version, or the list of links at the end of the file, ends this one
        if ($lines[$i] -match '^##\s+\[' -or $lines[$i] -match '^\[[^\]]+\]:\s') { break }
        $section += $lines[$i]
    }
}

$text = ($section -join "`n").Trim()
if (-not $text) {
    if ($Strict) { throw "CHANGELOG.md has nothing written for $wanted. Add a '## [$wanted]' section (move the entries from '## [Unreleased]') before tagging." }
    Write-Warning "CHANGELOG.md has nothing written for $wanted."
    $text = "No release notes were written for this version. The full history is in the changelog: https://github.com/Kalmai221/Hofwerk/blob/main/CHANGELOG.md"
}

if ($Footer) {
    # Everything after this marker is left out of "What's new" inside the app.
    $text += "`n`n<!-- end-of-notes -->`n---`n`n" + (Get-Content -LiteralPath $Footer -Raw -Encoding UTF8).Trim()
}

[IO.File]::WriteAllText($Output, $text + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Release notes for $wanted written to $Output ($($section.Count) lines)."
