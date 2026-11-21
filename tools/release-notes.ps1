<#
.SYNOPSIS
    Writes the CHANGELOG.md section of a version as the GitHub release notes (spec §12).

.DESCRIPTION
    Takes everything between "## [<Version>]" and the next "## [" heading. Fails when the section
    is missing or empty, so that a release is never published without notes. A pre-release
    (2.0.0-rc.1) uses the section of its final version (2.0.0) when it has none of its own.

.EXAMPLE
    pwsh tools/release-notes.ps1 -Version 2.0.0 -OutFile dist/release-notes.md
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $OutFile,
    [string] $Changelog = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'

function Get-Section([string[]] $Lines, [string] $Name) {
    $heading = '## [' + $Name + ']'
    $start = -1
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i].StartsWith($heading)) { $start = $i + 1; break }
    }
    if ($start -lt 0) { return $null }

    $body = foreach ($line in $Lines[$start..($Lines.Count - 1)]) {
        if ($line.StartsWith('## [')) { break }
        $line
    }
    return (($body -join "`n").Trim())
}

$lines = Get-Content -LiteralPath $Changelog -Encoding utf8
$notes = Get-Section $lines $Version
if ([string]::IsNullOrWhiteSpace($notes) -and $Version.Contains('-')) {
    $notes = Get-Section $lines ($Version.Split('-')[0])
}
if ([string]::IsNullOrWhiteSpace($notes)) {
    throw "CHANGELOG.md has no section '## [$Version]' with content."
}

$footer = "`n`n---`nDownloads: the installer (``AstroPlannerPro_${Version}_Setup.exe``) or the portable zip; " +
    "check them against ``SHA256SUMS.txt``. Windows 10 (1809+) or 11, x64."
New-Item -ItemType Directory -Force (Split-Path -Parent $OutFile) | Out-Null
Set-Content -LiteralPath $OutFile -Value ($notes + $footer) -Encoding utf8
Write-Host "Release notes for $Version written to $OutFile ($($notes.Length) characters)."
