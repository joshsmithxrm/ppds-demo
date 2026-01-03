# Test-PpdsDataMigration-ServicePrincipal.ps1
# Tests PPDS CLI data migration commands using multiple service principals
# Executes in tmp/ folder for isolated testing

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$tmpDir = Join-Path $repoRoot "tmp"

# Helper to run command and capture output for transcript
function Invoke-Logged {
    param([scriptblock]$Command)
    & $Command 2>&1 | ForEach-Object { Write-Host $_ }
}

# Ensure tmp directory exists
if (-not (Test-Path $tmpDir)) {
    New-Item -ItemType Directory -Path $tmpDir | Out-Null
}

# Start logging
$logFile = Join-Path $tmpDir "ppds-test-sp-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
Start-Transcript -Path $logFile

Push-Location $tmpDir
try {
    Write-Host "=== PPDS CLI Data Migration Test (Service Principal) ===" -ForegroundColor Cyan
    Write-Host "Log file: $logFile" -ForegroundColor DarkGray
    Write-Host "Working directory: $tmpDir" -ForegroundColor DarkGray
    Write-Host ""

    # 1. Generate schema for geographic entities
    Write-Host "[1/4] Generating schema for ppds_zipcode, ppds_city, ppds_state..." -ForegroundColor Yellow
    Invoke-Logged { ppds schema generate -e ppds_zipcode, ppds_city, ppds_state -o schema.xml }
    Write-Host "      Output: schema.xml" -ForegroundColor Green
    Write-Host ""

    # 2. Generate user mapping between Dev and QA
    Write-Host "[2/4] Generating user mapping (Dev -> QA)..." -ForegroundColor Yellow
    Invoke-Logged { ppds users generate -se Dev -te QA -o users.xml }
    Write-Host "      Output: users.xml" -ForegroundColor Green
    Write-Host ""

    # 3. Export data using the schema
    Write-Host "[3/4] Exporting data using schema..." -ForegroundColor Yellow
    Invoke-Logged { ppds data export -s schema.xml -o data.zip }
    Write-Host "      Output: data.zip" -ForegroundColor Green
    Write-Host ""

    # 4. Import data to target environment with user mapping
    Write-Host "[4/4] Importing data to QA environment..." -ForegroundColor Yellow
    Invoke-Logged { ppds data import -d data.zip -u users.xml -env "https://orge821e2a2.crm.dynamics.com/" -p sp1,sp2 --debug }
    Write-Host "      Import complete" -ForegroundColor Green
    Write-Host ""

    Write-Host "=== Test Complete ===" -ForegroundColor Cyan
}
finally {
    Pop-Location
    Stop-Transcript
}
