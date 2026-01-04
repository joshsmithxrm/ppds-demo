# Test-PpdsDataLoad-StatesOnly.ps1
# Tests PPDS CLI data load command with states data only
# Uses sp1,sp2 profiles for connection pool load balancing
#
# This is a simplified test that only loads states (no lookups required).
# For full geo-data test (states → cities → zipcodes), see Test-PpdsDataLoad-WithMappings.ps1

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
$logFile = Join-Path $tmpDir "ppds-data-load-states-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
Start-Transcript -Path $logFile

Push-Location $tmpDir
try {
    Write-Host "=== PPDS CLI Data Load Test (States Only) ===" -ForegroundColor Cyan
    Write-Host "Log file: $logFile" -ForegroundColor DarkGray
    Write-Host "Working directory: $tmpDir" -ForegroundColor DarkGray
    Write-Host "Mappings: $mappingsDir" -ForegroundColor DarkGray
    Write-Host "Profiles: sp1,sp2 (dual service principal)" -ForegroundColor DarkGray
    Write-Host ""

    # Phase 1: Download geo-data CSV
    Write-Host "[1/3] Preparing geo-data CSV..." -ForegroundColor Yellow
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
    Write-Host "      Source records: $($zipData.Count)" -ForegroundColor Green
    Write-Host ""

    # Phase 2: Extract states CSV
    Write-Host "[2/3] Preparing states CSV..." -ForegroundColor Yellow
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
        $statesCount = $statesData.Count
        Write-Host "      Extracted: $statesCount states" -ForegroundColor Green
    }
    Write-Host ""

    # Phase 3: Load States
    Write-Host "[3/3] Loading states to Dataverse..." -ForegroundColor Yellow
    $statesStart = Get-Date
    Invoke-Logged { ppds data load -e ppds_state -f $statesCsvPath -k ppds_abbreviation -m "$mappingsDir\states.json" -p sp1,sp2 -v }
    if ($LASTEXITCODE -ne 0) { throw "Failed to load states (exit code: $LASTEXITCODE)" }
    $statesDuration = (Get-Date) - $statesStart
    Write-Host ""
    Write-Host "      States loaded in $($statesDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
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
