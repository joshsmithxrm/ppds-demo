# Test-PpdsDataLoad.ps1
# Tests PPDS CLI data load command with geo-data CSV (42K records)
# Uses sp1,sp2 profiles for connection pool load balancing and throughput verification
#
# This script uses prefix-aware auto-mapping - CSV columns like "abbreviation" and "name"
# automatically match to ppds_abbreviation and ppds_name without requiring a mapping file.

param(
    [switch]$Force  # Force re-download of CSV even if cached
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$tmpDir = Join-Path $repoRoot "tmp"
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
    Write-Host "Profiles: sp1,sp2 (dual service principal for load balancing)" -ForegroundColor DarkGray
    Write-Host ""

    # Phase 1: Download/Prepare CSV
    Write-Host "[1/7] Preparing geo-data CSV..." -ForegroundColor Yellow
    $zipCsvPath = Join-Path $tmpDir "all_us_zipcodes.csv"
    $needsDownload = -not (Test-Path $zipCsvPath) -or $Force

    if ($needsDownload) {
        if ($Force -and (Test-Path $zipCsvPath)) {
            Write-Host "      -Force specified, re-downloading CSV" -ForegroundColor DarkGray
        }
        Write-Host "      Downloading from: $csvUrl" -ForegroundColor DarkGray
        Invoke-WebRequest -Uri $csvUrl -OutFile $zipCsvPath
        Write-Host "      Downloaded: $zipCsvPath" -ForegroundColor Green
    }
    else {
        Write-Host "      Using cached CSV: $zipCsvPath" -ForegroundColor DarkGray
        Write-Host "      (use -Force to re-download)" -ForegroundColor DarkGray
    }

    # Count records
    $zipData = Import-Csv $zipCsvPath
    $totalRecords = $zipData.Count
    Write-Host "      Total ZIP code records: $totalRecords" -ForegroundColor Green
    Write-Host ""

    # Phase 2: Extract unique states
    Write-Host "[2/7] Extracting unique states..." -ForegroundColor Yellow
    $statesCsvPath = Join-Path $tmpDir "load-states.csv"

    if ((Test-Path $statesCsvPath) -and -not $Force) {
        $statesData = Import-Csv $statesCsvPath
        Write-Host "      Using cached: $($statesData.Count) states" -ForegroundColor DarkGray
    }
    else {
        $uniqueStates = $zipData | Select-Object -ExpandProperty state -Unique | Sort-Object
        $statesData = $uniqueStates | ForEach-Object {
            $abbr = $_
            $name = if ($stateNames.ContainsKey($abbr)) { $stateNames[$abbr] } else { $abbr }
            [PSCustomObject]@{
                abbreviation = $abbr
                name = $name
            }
        }
        $statesData | Export-Csv -Path $statesCsvPath -NoTypeInformation
        Write-Host "      Extracted: $($statesData.Count) states" -ForegroundColor Green
    }
    Write-Host ""

    # Phase 3: Extract unique cities (using hashtable for O(n) deduplication)
    Write-Host "[3/7] Extracting unique cities..." -ForegroundColor Yellow
    $citiesCsvPath = Join-Path $tmpDir "load-cities.csv"

    if ((Test-Path $citiesCsvPath) -and -not $Force) {
        $citiesData = Import-Csv $citiesCsvPath
        Write-Host "      Using cached: $($citiesData.Count) cities" -ForegroundColor DarkGray
    }
    else {
        $cityHash = @{}
        foreach ($row in $zipData) {
            $key = "$($row.city)|$($row.state)"
            if (-not $cityHash.ContainsKey($key)) {
                $cityHash[$key] = [PSCustomObject]@{
                    name = $row.city
                    state = $row.state
                }
            }
        }
        $citiesData = $cityHash.Values | Sort-Object state, name
        $citiesData | Export-Csv -Path $citiesCsvPath -NoTypeInformation
        Write-Host "      Extracted: $($citiesData.Count) cities" -ForegroundColor Green
    }
    Write-Host ""

    # Phase 4: Load States
    Write-Host "[4/7] Loading states to Dataverse..." -ForegroundColor Yellow
    $statesStart = Get-Date
    Invoke-Logged { ppds data load -e ppds_state -f $statesCsvPath -k ppds_abbreviation -p sp1,sp2 -v }
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to load states (exit code: $LASTEXITCODE)"
    }
    $statesDuration = (Get-Date) - $statesStart
    Write-Host "      States loaded in $($statesDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
    Write-Host ""

    # Phase 5: Load Cities (with state lookup)
    Write-Host "[5/7] Loading cities to Dataverse (with state lookup)..." -ForegroundColor Yellow
    Write-Host "      This may take a moment - resolving state lookups for $($citiesData.Count) cities" -ForegroundColor DarkGray
    $citiesStart = Get-Date
    Invoke-Logged { ppds data load -e ppds_city -f $citiesCsvPath -k ppds_name,ppds_stateid -p sp1,sp2 -v }
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to load cities (exit code: $LASTEXITCODE)"
    }
    $citiesDuration = (Get-Date) - $citiesStart
    Write-Host "      Cities loaded in $($citiesDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
    Write-Host ""

    # Phase 6: Load ZIP Codes (with state and city lookups)
    Write-Host "[6/7] Loading ZIP codes to Dataverse (42K records)..." -ForegroundColor Yellow
    Write-Host "      This is the bulk throughput test - using dual service principals" -ForegroundColor DarkGray
    $zipsStart = Get-Date
    Invoke-Logged { ppds data load -e ppds_zipcode -f $zipCsvPath -k ppds_code -p sp1,sp2 -v }
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to load ZIP codes (exit code: $LASTEXITCODE)"
    }
    $zipsDuration = (Get-Date) - $zipsStart
    $throughput = $totalRecords / $zipsDuration.TotalSeconds
    Write-Host "      ZIP codes loaded in $($zipsDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
    Write-Host "      Throughput: $($throughput.ToString('F0')) records/sec" -ForegroundColor Green
    Write-Host ""

    # Phase 7: Summary
    Write-Host "[7/7] Test Summary" -ForegroundColor Yellow
    Write-Host "      States:    $($statesData.Count) records in $($statesDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
    Write-Host "      Cities:    $($citiesData.Count) records in $($citiesDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
    Write-Host "      ZIP Codes: $totalRecords records in $($zipsDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
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
