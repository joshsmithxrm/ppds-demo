<#
.SYNOPSIS
    Adds missing components to the PPDSDemo solution.

.DESCRIPTION
    Adds the following components that exist in the environment but are not in the solution:
    - Global Option Set: ppds_status
    - Environment Variable: ppds_ApiEndpoint
    - Environment Variable: ppds_EnableFeatureX

.PREREQUISITES
    - Install PowerShell module: Install-Module Microsoft.Xrm.Data.PowerShell -Scope CurrentUser

.EXAMPLE
    # Using environment file (recommended)
    .\tools\Add-MissingSolutionComponents.ps1 -EnvFile ".env.dev"

.EXAMPLE
    # Interactive auth for quick testing
    .\tools\Add-MissingSolutionComponents.ps1 -EnvironmentUrl "https://org.crm.dynamics.com" -Interactive

.EXAMPLE
    # Service principal auth
    .\tools\Add-MissingSolutionComponents.ps1 -TenantId "xxx" -ClientId "xxx" -ClientSecret "xxx" -EnvironmentUrl "https://org.crm.dynamics.com"
#>

param(
    [string]$ConnectionString,
    [string]$TenantId,
    [string]$ClientId,
    [string]$ClientSecret,
    [string]$EnvironmentUrl,
    [string]$EnvFile,
    [switch]$Interactive,

    [string]$SolutionName = "PPDSDemo"
)

$ErrorActionPreference = "Stop"
$ScriptRoot = $PSScriptRoot

# Import shared authentication module
. "$ScriptRoot\Common-Auth.ps1"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Add Missing Solution Components" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get connection using Common-Auth
$conn = Get-DataverseConnection `
    -ConnectionString $ConnectionString `
    -TenantId $TenantId `
    -ClientId $ClientId `
    -ClientSecret $ClientSecret `
    -EnvironmentUrl $EnvironmentUrl `
    -EnvFile $EnvFile `
    -Interactive:$Interactive

# Setup Web API
$baseUrl = Get-WebApiBaseUrl -Connection $conn
$headers = Get-AuthHeaders -Connection $conn

# =============================================================================
# Helper Function
# =============================================================================

function Add-SolutionComponent {
    param(
        [string]$ComponentId,
        [int]$ComponentType,
        [string]$ComponentName
    )

    Write-Host "Adding $ComponentName to solution..." -NoNewline

    $body = @{
        ComponentId = $ComponentId
        ComponentType = $ComponentType
        SolutionUniqueName = $SolutionName
        AddRequiredComponents = $false
    } | ConvertTo-Json

    try {
        Invoke-RestMethod -Uri "$baseUrl/AddSolutionComponent" -Method POST -Headers $headers -Body $body | Out-Null
        Write-Host " Done" -ForegroundColor Green
        return $true
    }
    catch {
        if ($_.Exception.Message -match "already exists") {
            Write-Host " Already in solution" -ForegroundColor Yellow
            return $true
        }
        Write-Host " Failed: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

# =============================================================================
# Get Component IDs
# =============================================================================

Write-Host ""
Write-Host "--- Finding Components ---" -ForegroundColor Magenta
Write-Host ""

# 1. Get Global Option Set ID (ppds_status)
Write-Host "Looking up ppds_status option set..."
$optionSet = Invoke-RestMethod -Uri "$baseUrl/GlobalOptionSetDefinitions(Name='ppds_status')" -Headers $headers
$optionSetId = $optionSet.MetadataId
Write-Host "  Found: $optionSetId" -ForegroundColor Gray

# 2. Get Environment Variable IDs
Write-Host "Looking up environment variables..."

$envVars = Invoke-RestMethod -Uri "$baseUrl/environmentvariabledefinitions?`$filter=startswith(schemaname,'ppds_')&`$select=environmentvariabledefinitionid,schemaname" -Headers $headers

$apiEndpointId = ($envVars.value | Where-Object { $_.schemaname -eq "ppds_ApiEndpoint" }).environmentvariabledefinitionid
$featureXId = ($envVars.value | Where-Object { $_.schemaname -eq "ppds_EnableFeatureX" }).environmentvariabledefinitionid

Write-Host "  ppds_ApiEndpoint: $apiEndpointId" -ForegroundColor Gray
Write-Host "  ppds_EnableFeatureX: $featureXId" -ForegroundColor Gray

# =============================================================================
# Add Components to Solution
# =============================================================================

Write-Host ""
Write-Host "--- Adding to Solution: $SolutionName ---" -ForegroundColor Magenta
Write-Host ""

# Component Type Reference:
# 9 = OptionSet
# 380 = EnvironmentVariableDefinition

$success = $true

# Add Global Option Set (type 9)
if ($optionSetId) {
    $result = Add-SolutionComponent -ComponentId $optionSetId -ComponentType 9 -ComponentName "ppds_status (Global Option Set)"
    $success = $success -and $result
}

# Add Environment Variables (type 380)
if ($apiEndpointId) {
    $result = Add-SolutionComponent -ComponentId $apiEndpointId -ComponentType 380 -ComponentName "ppds_ApiEndpoint (Environment Variable)"
    $success = $success -and $result
}

if ($featureXId) {
    $result = Add-SolutionComponent -ComponentId $featureXId -ComponentType 380 -ComponentName "ppds_EnableFeatureX (Environment Variable)"
    $success = $success -and $result
}

# =============================================================================
# Done
# =============================================================================

Write-Host ""
if ($success) {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " All components added successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
}
else {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Some components failed to add" -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "Next: Re-export the solution to verify:" -ForegroundColor Yellow
Write-Host "  pac solution export --name PPDSDemo --path solutions/exports/PPDSDemo.zip --overwrite" -ForegroundColor White
Write-Host ""
