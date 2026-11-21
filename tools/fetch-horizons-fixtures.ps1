<#
.SYNOPSIS
    Downloads the JPL Horizons golden fixtures used by the Core tests (spec §6).

.DESCRIPTION
    Developer script, run ONCE when a fixture is added or changed; the output is committed to
    tests/fixtures/horizons/. CI and the tests never call Horizons.

    Each fixture is a CSV of apparent azimuth/elevation (Horizons quantity 4) every minute,
    with the query in the header comments so that it can be reproduced.

.EXAMPLE
    powershell -File tools/fetch-horizons-fixtures.ps1   (Windows PowerShell 5.1 or pwsh 7)
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory,
    # Fetch only the small-body RA/Dec fixtures (M5), leaving the alt/az ones untouched.
    [switch] $SmallBodiesOnly
)

$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) {
    # Windows PowerShell 5.1 does not set $PSScriptRoot while binding parameter defaults.
    $OutputDirectory = Join-Path $PSScriptRoot '..\tests\fixtures\horizons'
}

# Name, Horizons target, site (lon, lat, km), UTC start/stop, refraction model.
$fixtures = @(
    @{ Name = 'sun_aguascalientes_2026-06-20'; Command = '10'; Site = '-102.29,21.88,1.888'
       Start = '2026-06-20 18:00'; Stop = '2026-06-21 18:00'; Apparent = 'AIRLESS' },
    @{ Name = 'sun_madrid_2023-12-15'; Command = '10'; Site = '-3.70,40.42,0.667'
       Start = '2023-12-15 11:00'; Stop = '2023-12-16 11:00'; Apparent = 'AIRLESS' },
    @{ Name = 'sun_oslo_2026-06-21'; Command = '10'; Site = '10.75,59.91,0.0'
       Start = '2026-06-21 10:00'; Stop = '2026-06-22 10:00'; Apparent = 'AIRLESS' },
    @{ Name = 'jupiter_aguascalientes_2026-01-10'; Command = '599'; Site = '-102.29,21.88,1.888'
       Start = '2026-01-10 18:00'; Stop = '2026-01-11 18:00'; Apparent = 'REFRACTED' },
    @{ Name = 'saturn_madrid_2025-09-21'; Command = '699'; Site = '-3.70,40.42,0.667'
       Start = '2025-09-21 11:00'; Stop = '2025-09-22 11:00'; Apparent = 'REFRACTED' }
)

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path

foreach ($fixture in $(if ($SmallBodiesOnly) { @() } else { $fixtures })) {
    $query = [ordered]@{
        format      = 'json'
        COMMAND     = "'$($fixture.Command)'"
        OBJ_DATA    = "'NO'"
        MAKE_EPHEM  = "'YES'"
        EPHEM_TYPE  = "'OBSERVER'"
        CENTER      = "'coord@399'"
        COORD_TYPE  = "'GEODETIC'"
        SITE_COORD  = "'$($fixture.Site)'"
        START_TIME  = "'$($fixture.Start)'"
        STOP_TIME   = "'$($fixture.Stop)'"
        STEP_SIZE   = "'1 m'"
        QUANTITIES  = "'4'"
        APPARENT    = "'$($fixture.Apparent)'"
        CSV_FORMAT  = "'YES'"
        ANG_FORMAT  = "'DEG'"
        EXTRA_PREC  = "'YES'"
    }
    $queryString = ($query.GetEnumerator() | ForEach-Object {
        "$($_.Key)=$([uri]::EscapeDataString($_.Value))"
    }) -join '&'

    $response = Invoke-RestMethod -Uri "https://ssd.jpl.nasa.gov/api/horizons.api?$queryString" `
        -UserAgent 'AstroPlannerPro-dev (+https://github.com/Yamato6/AstroPlanner-Pro)'
    $text = $response.result
    $start = $text.IndexOf('$$SOE')
    $end = $text.IndexOf('$$EOE')
    if ($start -lt 0 -or $end -lt 0) { throw "Unexpected Horizons output for $($fixture.Name):`n$text" }

    $lines = @(
        "# JPL Horizons $($response.signature.version), fetched $(Get-Date -Format 'yyyy-MM-dd') by tools/fetch-horizons-fixtures.ps1"
        "# target=$($fixture.Command) site(lon,lat,km)=$($fixture.Site) apparent=$($fixture.Apparent) quantity=4 step=1m"
        'utc,azimuth_deg,elevation_deg'
    )
    foreach ($row in $text.Substring($start + 5, $end - $start - 5).Trim() -split "`n") {
        $parts = $row.Split(',') | ForEach-Object { $_.Trim() }
        $utc = [datetime]::ParseExact($parts[0], 'yyyy-MMM-dd HH:mm', [cultureinfo]::InvariantCulture)
        $lines += '{0},{1},{2}' -f $utc.ToString('yyyy-MM-ddTHH:mm', [cultureinfo]::InvariantCulture), $parts[3], $parts[4]
    }

    $path = Join-Path $OutputDirectory "$($fixture.Name).csv"
    [IO.File]::WriteAllLines($path, [string[]]$lines, (New-Object Text.UTF8Encoding $false))
    Write-Host "$($fixture.Name): $($lines.Count - 3) rows"
}

# Small bodies (M5): geocentric astrometric RA/Dec (ICRF, quantity 1) and distance (quantity 20)
# every 12 hours, for the orbit tests. The elements the app uses for each one are recorded next
# to them (tests/fixtures/horizons/smallbodies-elements.txt, lines from the MPC files).
$smallBodies = @(
    @{ Name = 'comet_2P_2026-11'; Command = 'DES=2P;CAP;NOFRAG'; Start = '2026-11-05'; Stop = '2026-12-05' },
    @{ Name = 'comet_C2023A3_2026-11'; Command = 'DES=C/2023 A3;CAP;NOFRAG'; Start = '2026-11-05'; Stop = '2026-12-05' },
    @{ Name = 'comet_C2025A6_2026-11'; Command = 'DES=C/2025 A6;CAP;NOFRAG'; Start = '2026-11-05'; Stop = '2026-12-05' },
    @{ Name = 'asteroid_4_2025-11'; Command = '4;'; Start = '2025-11-06'; Stop = '2025-12-06' },
    @{ Name = 'asteroid_4_2026-11'; Command = '4;'; Start = '2026-11-05'; Stop = '2026-12-05' }
)

foreach ($fixture in $smallBodies) {
    $query = [ordered]@{
        format      = 'json'
        COMMAND     = "'$($fixture.Command)'"
        OBJ_DATA    = "'NO'"
        MAKE_EPHEM  = "'YES'"
        EPHEM_TYPE  = "'OBSERVER'"
        CENTER      = "'500@399'"
        START_TIME  = "'$($fixture.Start)'"
        STOP_TIME   = "'$($fixture.Stop)'"
        STEP_SIZE   = "'12 h'"
        QUANTITIES  = "'1,20'"
        CSV_FORMAT  = "'YES'"
        ANG_FORMAT  = "'DEG'"
        EXTRA_PREC  = "'YES'"
    }
    $queryString = ($query.GetEnumerator() | ForEach-Object {
        "$($_.Key)=$([uri]::EscapeDataString($_.Value))"
    }) -join '&'

    $response = Invoke-RestMethod -Uri "https://ssd.jpl.nasa.gov/api/horizons.api?$queryString" `
        -UserAgent 'AstroPlannerPro-dev (+https://github.com/Yamato6/AstroPlanner-Pro)'
    $text = $response.result
    $start = $text.IndexOf('$$SOE')
    $end = $text.IndexOf('$$EOE')
    if ($start -lt 0 -or $end -lt 0) { throw "Unexpected Horizons output for $($fixture.Name):`n$text" }

    $lines = @(
        "# JPL Horizons $($response.signature.version), fetched $(Get-Date -Format 'yyyy-MM-dd') by tools/fetch-horizons-fixtures.ps1"
        "# target=$($fixture.Command) center=500@399 (geocentric) quantities=1,20 (astrometric RA/Dec ICRF, delta) step=12h"
        'utc,ra_deg,dec_deg,delta_au'
    )
    foreach ($row in $text.Substring($start + 5, $end - $start - 5).Trim() -split "`n") {
        $parts = $row.Split(',') | ForEach-Object { $_.Trim() }
        $utc = [datetime]::ParseExact($parts[0], 'yyyy-MMM-dd HH:mm', [cultureinfo]::InvariantCulture)
        $lines += '{0},{1},{2},{3}' -f $utc.ToString('yyyy-MM-ddTHH:mm', [cultureinfo]::InvariantCulture), $parts[3], $parts[4], $parts[5]
    }

    $path = Join-Path $OutputDirectory "$($fixture.Name).csv"
    [IO.File]::WriteAllLines($path, [string[]]$lines, (New-Object Text.UTF8Encoding $false))
    Write-Host "$($fixture.Name): $($lines.Count - 3) rows"
}
