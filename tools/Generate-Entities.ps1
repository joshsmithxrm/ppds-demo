<#
.SYNOPSIS
    Generates early-bound entity classes from Dataverse using pac modelbuilder.

.DESCRIPTION
    This script regenerates the early-bound entity classes in the PPDSDemo.Entities project.
    All team members should use this script to ensure consistent code generation.

    Prerequisites:
    - Power Platform CLI (pac) installed and in PATH
    - Authenticated to target Dataverse environment (pac auth create)
    - Active environment selected (pac org select)

.PARAMETER Entities
    Semicolon-separated list of entity logical names to generate.
    Default: account;contact
    Add custom entities as needed (e.g., "account;contact;ppds_demoentity")

.PARAMETER IncludeGlobalOptionSets
    Include all global option sets in generation. Default: true

.PARAMETER IncludeSdkMessages
    Include SDK message classes. Default: false (reduces output significantly)

.EXAMPLE
    .\Generate-Entities.ps1
    Generates default entities (account, contact)

.EXAMPLE
    .\Generate-Entities.ps1 -Entities "account;contact;ppds_demoentity;ppds_demochild"
    Generates specified entities including custom ones

.EXAMPLE
    .\Generate-Entities.ps1 -IncludeSdkMessages
    Generates entities with SDK message classes included
#>

param(
    [string]$Entities = "account;contact",
    [switch]$IncludeGlobalOptionSets = $true,
    [switch]$IncludeSdkMessages = $false
)

$ErrorActionPreference = "Stop"

# Configuration
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$OutputDir = Join-Path $RepoRoot "src\Shared\PPDSDemo.Entities"
$Namespace = "PPDSDemo.Entities"
$ServiceContextName = "PPDSDemoContext"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  PPDSDemo Entity Generator" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Verify pac is available
try {
    $pacVersion = pac help 2>&1 | Select-String "Version:"
    Write-Host "Using PAC CLI: $pacVersion" -ForegroundColor Gray
} catch {
    Write-Error "PAC CLI not found. Install it with: dotnet tool install --global Microsoft.PowerApps.CLI.Tool"
    exit 1
}

# Verify authentication
Write-Host ""
Write-Host "Checking authentication..." -ForegroundColor Yellow
$authList = pac auth list 2>&1
if ($authList -match "No profiles") {
    Write-Error "No PAC auth profiles found. Run: pac auth create --url https://yourorg.crm.dynamics.com"
    exit 1
}
Write-Host $authList

# Verify org selection
Write-Host ""
Write-Host "Checking active organization..." -ForegroundColor Yellow
$orgWho = pac org who 2>&1
if ($orgWho -match "No organization") {
    Write-Error "No active organization. Run: pac org select --environment <environment-id>"
    exit 1
}
Write-Host $orgWho

# Clean existing generated files
Write-Host ""
Write-Host "Cleaning existing generated files..." -ForegroundColor Yellow
$foldersToClean = @("Entities", "OptionSets", "Messages")
foreach ($folder in $foldersToClean) {
    $folderPath = Join-Path $OutputDir $folder
    if (Test-Path $folderPath) {
        Remove-Item -Path $folderPath -Recurse -Force
        Write-Host "  Removed: $folder" -ForegroundColor Gray
    }
}
# Remove root-level generated files
$filesToClean = @("EntityOptionSetEnum.cs", "PPDSDemoContext.cs")
foreach ($file in $filesToClean) {
    $filePath = Join-Path $OutputDir $file
    if (Test-Path $filePath) {
        Remove-Item -Path $filePath -Force
        Write-Host "  Removed: $file" -ForegroundColor Gray
    }
}

# Build pac modelbuilder command
Write-Host ""
Write-Host "Generating entity classes..." -ForegroundColor Yellow
Write-Host "  Entities: $Entities" -ForegroundColor Gray
Write-Host "  Namespace: $Namespace" -ForegroundColor Gray
Write-Host "  Output: $OutputDir" -ForegroundColor Gray

$pacArgs = @(
    "modelbuilder", "build",
    "--outdirectory", $OutputDir,
    "--entitynamesfilter", $Entities,
    "--namespace", $Namespace,
    "--serviceContextName", $ServiceContextName,
    "--emitfieldsclasses"
)

if ($IncludeGlobalOptionSets) {
    $pacArgs += "--generateGlobalOptionSets"
    Write-Host "  Global OptionSets: Included" -ForegroundColor Gray
}

if ($IncludeSdkMessages) {
    $pacArgs += "--generatesdkmessages"
    Write-Host "  SDK Messages: Included" -ForegroundColor Gray
} else {
    Write-Host "  SDK Messages: Excluded (use -IncludeSdkMessages to include)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Running: pac $($pacArgs -join ' ')" -ForegroundColor DarkGray
Write-Host ""

# Execute pac modelbuilder
& pac @pacArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "pac modelbuilder failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# Verify output
Write-Host ""
Write-Host "Verifying generated files..." -ForegroundColor Yellow
$entitiesDir = Join-Path $OutputDir "Entities"
if (Test-Path $entitiesDir) {
    $entityFiles = Get-ChildItem -Path $entitiesDir -Filter "*.cs"
    Write-Host "  Generated $($entityFiles.Count) entity file(s):" -ForegroundColor Green
    foreach ($file in $entityFiles) {
        Write-Host "    - $($file.Name)" -ForegroundColor Gray
    }
} else {
    Write-Warning "No Entities folder found - generation may have failed"
}

$optionSetsDir = Join-Path $OutputDir "OptionSets"
if (Test-Path $optionSetsDir) {
    $optionSetFiles = Get-ChildItem -Path $optionSetsDir -Filter "*.cs"
    Write-Host "  Generated $($optionSetFiles.Count) option set file(s)" -ForegroundColor Green
}

# Build to verify
Write-Host ""
Write-Host "Building PPDSDemo.Entities to verify..." -ForegroundColor Yellow
$csprojPath = Join-Path $OutputDir "PPDSDemo.Entities.csproj"
dotnet build $csprojPath --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed! Check the generated code for errors."
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Entity generation completed successfully!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Review the generated files in src/Shared/PPDSDemo.Entities/" -ForegroundColor Gray
Write-Host "  2. Build the solution to verify all references" -ForegroundColor Gray
Write-Host "  3. Commit the changes" -ForegroundColor Gray
Write-Host ""
