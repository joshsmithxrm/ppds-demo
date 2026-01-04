# Test-PpdsDataLoad-WithMappings.ps1
# Tests PPDS CLI data load command with geo-data CSV (42K records)
# Uses committed mapping files from scripts/mappings/
# Uses sp1,sp2 profiles for connection pool load balancing
#
# Full geo-data workflow: states → cities → zipcodes
# Demonstrates composite keys with lookup fields and explicit column mappings.

param(
    [switch]$Force  # Force re-download and re-extract cached data
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$tmpDir = Join-Path $repoRoot "tmp"
$mappingsDir = Join-Path $scriptRoot "mappings"
$csvUrl = "https://raw.githubusercontent.com/midwire/free_zipcode_data/master/all_us_zipcodes.csv"

# Helper to run command and capture output for transcript
function Invoke-Logged {
    param([scriptblock]$Command)
    & $Command 2>&1 | ForEach-Object { Write-Host $_ }
}

# State name lookup (abbreviation -> full name)
$stateNames = @{
    "AL" = "Alabama"; "AK" = "Alaska"; "AZ" = "Arizona"; "AR" = "Arkansas"
    "CA" = "California"; "CO" = "Colorado"; "CT" = "Connecticut"; "DE" = "Delaware"
    "FL" = "Florida"; "GA" = "Georgia"; "HI" = "Hawaii"; "ID" = "Idaho"
    "IL" = "Illinois"; "IN" = "Indiana"; "IA" = "Iowa"; "KS" = "Kansas"
    "KY" = "Kentucky"; "LA" = "Louisiana"; "ME" = "Maine"; "MD" = "Maryland"
    "MA" = "Massachusetts"; "MI" = "Michigan"; "MN" = "Minnesota"; "MS" = "Mississippi"
    "MO" = "Missouri"; "MT" = "Montana"; "NE" = "Nebraska"; "NV" = "Nevada"
    "NH" = "New Hampshire"; "NJ" = "New Jersey"; "NM" = "New Mexico"; "NY" = "New York"
    "NC" = "North Carolina"; "ND" = "North Dakota"; "OH" = "Ohio"; "OK" = "Oklahoma"
    "OR" = "Oregon"; "PA" = "Pennsylvania"; "RI" = "Rhode Island"; "SC" = "South Carolina"
    "SD" = "South Dakota"; "TN" = "Tennessee"; "TX" = "Texas"; "UT" = "Utah"
    "VT" = "Vermont"; "VA" = "Virginia"; "WA" = "Washington"; "WV" = "West Virginia"
    "WI" = "Wisconsin"; "WY" = "Wyoming"; "DC" = "District of Columbia"
    "PR" = "Puerto Rico"; "VI" = "Virgin Islands"; "GU" = "Guam"
    "AS" = "American Samoa"; "MP" = "Northern Mariana Islands"
}

# Ensure tmp directory exists
if (-not (Test-Path $tmpDir)) {
    New-Item -ItemType Directory -Path $tmpDir | Out-Null
}

# Start logging
$logFile = Join-Path $tmpDir "ppds-data-load-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
Start-Transcript -Path $logFile

Push-Location $tmpDir
try {
    Write-Host "=== PPDS CLI Data Load Test ===" -ForegroundColor Cyan
    Write-Host "Log file: $logFile" -ForegroundColor DarkGray
    Write-Host "Working directory: $tmpDir" -ForegroundColor DarkGray
    Write-Host "Mappings: $mappingsDir" -ForegroundColor DarkGray
    Write-Host "Profiles: sp1,sp2 (dual service principal)" -ForegroundColor DarkGray
    Write-Host ""

    # Phase 1: Download geo-data CSV
    Write-Host "[1/6] Preparing geo-data CSV..." -ForegroundColor Yellow
    $zipCsvPath = Join-Path $tmpDir "all_us_zipcodes.csv"

    if ((Test-Path $zipCsvPath) -and -not $Force) {
        Write-Host "      Using cached: $zipCsvPath" -ForegroundColor DarkGray
    }
    else {
        if ($Force) { Write-Host "      -Force specified, re-downloading" -ForegroundColor DarkGray }
        Write-Host "      Downloading from: $csvUrl" -ForegroundColor DarkGray
        Invoke-WebRequest -Uri $csvUrl -OutFile $zipCsvPath
        Write-Host "      Downloaded: $zipCsvPath" -ForegroundColor Green
    }
    $zipData = Import-Csv $zipCsvPath
    Write-Host "      Records: $($zipData.Count)" -ForegroundColor Green
    Write-Host ""

    # Phase 2: Extract states CSV
    Write-Host "[2/6] Preparing states CSV..." -ForegroundColor Yellow
    $statesCsvPath = Join-Path $tmpDir "load-states.csv"

    if ((Test-Path $statesCsvPath) -and -not $Force) {
        $statesCount = (Import-Csv $statesCsvPath).Count
        Write-Host "      Using cached: $statesCount states" -ForegroundColor DarkGray
    }
    else {
        $uniqueStates = $zipData | Select-Object -ExpandProperty state -Unique | Sort-Object
        $statesData = $uniqueStates | ForEach-Object {
            $abbr = $_
            $name = if ($stateNames.ContainsKey($abbr)) { $stateNames[$abbr] } else { $abbr }
            [PSCustomObject]@{ ppds_abbreviation = $abbr; ppds_name = $name }
        }
        $statesData | Export-Csv -Path $statesCsvPath -NoTypeInformation
        Write-Host "      Extracted: $($statesData.Count) states" -ForegroundColor Green
    }
    Write-Host ""

    # Phase 3: Extract cities CSV
    Write-Host "[3/6] Preparing cities CSV..." -ForegroundColor Yellow
    $citiesCsvPath = Join-Path $tmpDir "load-cities.csv"

    if ((Test-Path $citiesCsvPath) -and -not $Force) {
        $citiesCount = (Import-Csv $citiesCsvPath).Count
        Write-Host "      Using cached: $citiesCount cities" -ForegroundColor DarkGray
    }
    else {
        $cityHash = @{}
        foreach ($row in $zipData) {
            $key = "$($row.city)|$($row.state)"
            if (-not $cityHash.ContainsKey($key)) {
                $cityHash[$key] = [PSCustomObject]@{
                    ppds_name = $row.city
                    ppds_stateid = $row.state
                }
            }
        }
        $citiesData = $cityHash.Values | Sort-Object ppds_stateid, ppds_name
        $citiesData | Export-Csv -Path $citiesCsvPath -NoTypeInformation
        Write-Host "      Extracted: $($citiesData.Count) cities" -ForegroundColor Green
    }
    Write-Host ""

    # Phase 4: Extract zipcodes CSV
    Write-Host "[4/6] Preparing zipcodes CSV..." -ForegroundColor Yellow
    $zipsMappedPath = Join-Path $tmpDir "load-zipcodes.csv"

    if ((Test-Path $zipsMappedPath) -and -not $Force) {
        Write-Host "      Using cached: $($zipData.Count) zipcodes" -ForegroundColor DarkGray
    }
    else {
        $zipsData = $zipData | ForEach-Object {
            [PSCustomObject]@{
                ppds_code = $_.code
                ppds_stateid = $_.state
                ppds_cityid = $_.city
                ppds_county = $_.county
                ppds_latitude = $_.lat
                ppds_longitude = $_.lon
            }
        }
        $zipsData | Export-Csv -Path $zipsMappedPath -NoTypeInformation
        Write-Host "      Created: $($zipsData.Count) zipcodes" -ForegroundColor Green
    }
    Write-Host ""

    # Phase 5: Load data to Dataverse
    Write-Host "[5/6] Loading data to Dataverse..." -ForegroundColor Yellow
    Write-Host ""

    # Load States
    Write-Host "      Loading states..." -ForegroundColor Cyan
    $statesStart = Get-Date
    Invoke-Logged { ppds data load -e ppds_state -f $statesCsvPath -k ppds_abbreviation -m "$mappingsDir\states.json" -p sp1,sp2 -v }
    if ($LASTEXITCODE -ne 0) { throw "Failed to load states (exit code: $LASTEXITCODE)" }
    $statesDuration = (Get-Date) - $statesStart
    Write-Host "      States: $($statesDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
    Write-Host ""

    # Load Cities
    Write-Host "      Loading cities..." -ForegroundColor Cyan
    $citiesStart = Get-Date
    Invoke-Logged { ppds data load -e ppds_city -f $citiesCsvPath -k ppds_name,ppds_stateid -m "$mappingsDir\cities.json" -p sp1,sp2 -v }
    if ($LASTEXITCODE -ne 0) { throw "Failed to load cities (exit code: $LASTEXITCODE)" }
    $citiesDuration = (Get-Date) - $citiesStart
    Write-Host "      Cities: $($citiesDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
    Write-Host ""

    # Load ZIP Codes
    Write-Host "      Loading zipcodes (bulk test)..." -ForegroundColor Cyan
    $zipsStart = Get-Date
    Invoke-Logged { ppds data load -e ppds_zipcode -f $zipsMappedPath -k ppds_code -m "$mappingsDir\zipcodes.json" -p sp1,sp2 -v }
    if ($LASTEXITCODE -ne 0) { throw "Failed to load zipcodes (exit code: $LASTEXITCODE)" }
    $zipsDuration = (Get-Date) - $zipsStart
    $throughput = $zipData.Count / $zipsDuration.TotalSeconds
    Write-Host "      Zipcodes: $($zipsDuration.TotalSeconds.ToString('F1'))s ($($throughput.ToString('F0')) rec/s)" -ForegroundColor Green
    Write-Host ""

    # Phase 6: Summary
    Write-Host "[6/6] Summary" -ForegroundColor Yellow
    Write-Host "      States:   loaded in $($statesDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
    Write-Host "      Cities:   loaded in $($citiesDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
    Write-Host "      Zipcodes: loaded in $($zipsDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
    Write-Host "      Throughput: $($throughput.ToString('F0')) records/sec" -ForegroundColor Cyan
    Write-Host ""

    Write-Host "=== Test Complete ===" -ForegroundColor Cyan
}
catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
    exit 1
}
finally {
    Pop-Location
    Stop-Transcript
}
