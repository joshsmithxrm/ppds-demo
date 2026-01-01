#Requires -Version 7.0
<#
.SYNOPSIS
    Extract and compare plugin registrations using the PPDS CLI.

.DESCRIPTION
    This script extracts plugin registration metadata from assemblies/packages
    and optionally compares them against the Dataverse environment.

.PARAMETER Extract
    Extract registrations from plugin assemblies/packages.

.PARAMETER Diff
    Compare registrations against the environment.

.PARAMETER All
    Run both extract and diff operations.

.EXAMPLE
    .\Update-PluginRegistrations.ps1 -Extract
    Extracts registrations from all plugin projects.

.EXAMPLE
    .\Update-PluginRegistrations.ps1 -Diff
    Compares all registrations against the environment.

.EXAMPLE
    .\Update-PluginRegistrations.ps1 -All
    Extracts and then diffs all registrations.
#>

[CmdletBinding()]
param(
    [switch]$Extract,
    [switch]$Diff,
    [switch]$All
)

$ErrorActionPreference = 'Stop'

# Navigate to repo root
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    # Define plugin projects
    $plugins = @(
        @{
            Name = "PPDSDemo.Plugins"
            Type = "Assembly"
            Input = "src/Plugins/PPDSDemo.Plugins/bin/Debug/net462/PPDSDemo.Plugins.dll"
            Output = "src/Plugins/PPDSDemo.Plugins/registrations.json"
            Solution = "PPDSDemo"
        },
        @{
            Name = "PPDSDemo.PluginPackage"
            Type = "NuGet"
            Input = "src/PluginPackages/PPDSDemo.PluginPackage/bin/Debug/ppds_PPDSDemo.PluginPackage.1.0.0.nupkg"
            Output = "src/PluginPackages/PPDSDemo.PluginPackage/registrations.json"
            Solution = "PPDSDemo"
        }
    )

    # Default to All if no flags specified
    if (-not $Extract -and -not $Diff -and -not $All) {
        $All = $true
    }

    if ($All) {
        $Extract = $true
        $Diff = $true
    }

    # Extract registrations
    if ($Extract) {
        Write-Host "`n=== Extracting Plugin Registrations ===" -ForegroundColor Cyan

        foreach ($plugin in $plugins) {
            Write-Host "`n$($plugin.Name):" -ForegroundColor Yellow

            if (-not (Test-Path $plugin.Input)) {
                Write-Warning "  Input not found: $($plugin.Input)"
                Write-Warning "  Run 'dotnet build' first."
                continue
            }

            $extractArgs = @("plugins", "extract", "-i", $plugin.Input, "-o", $plugin.Output, "-s", $plugin.Solution)
            Write-Host "  ppds $($extractArgs -join ' ')" -ForegroundColor DarkGray
            & ppds @extractArgs

            if ($LASTEXITCODE -eq 0) {
                Write-Host "  Extracted to $($plugin.Output)" -ForegroundColor Green
            } else {
                Write-Error "  Failed to extract registrations"
            }
        }
    }

    # Diff registrations
    if ($Diff) {
        Write-Host "`n=== Comparing Registrations Against Environment ===" -ForegroundColor Cyan

        foreach ($plugin in $plugins) {
            Write-Host "`n$($plugin.Name):" -ForegroundColor Yellow

            if (-not (Test-Path $plugin.Output)) {
                Write-Warning "  Registrations file not found: $($plugin.Output)"
                Write-Warning "  Run with -Extract first."
                continue
            }

            $diffArgs = @("plugins", "diff", "-c", $plugin.Output)
            Write-Host "  ppds $($diffArgs -join ' ')" -ForegroundColor DarkGray
            & ppds @diffArgs

            if ($LASTEXITCODE -ne 0) {
                Write-Error "  Diff failed for $($plugin.Name)"
            }
        }
    }

    Write-Host "`n=== Complete ===" -ForegroundColor Cyan

} finally {
    Pop-Location
}
