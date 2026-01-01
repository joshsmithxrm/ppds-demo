#Requires -Version 7.0
<#
.SYNOPSIS
    Deploy plugins to Dataverse using the PPDS CLI.

.DESCRIPTION
    This script deploys plugin assemblies and packages to Dataverse
    based on the registrations.json configuration files.

.PARAMETER Profile
    Authentication profile name to use.

.PARAMETER Environment
    Override the environment URL. Takes precedence over profile's bound environment.

.PARAMETER Clean
    Also remove orphaned registrations not in config.

.PARAMETER WhatIf
    Preview changes without applying.

.EXAMPLE
    .\Deploy-Plugins.ps1
    Deploys all plugins using the default profile.

.EXAMPLE
    .\Deploy-Plugins.ps1 -Profile "dev"
    Deploys all plugins using the "dev" profile.

.EXAMPLE
    .\Deploy-Plugins.ps1 -WhatIf
    Shows what would be deployed without making changes.

.EXAMPLE
    .\Deploy-Plugins.ps1 -Clean
    Deploys plugins and removes orphaned registrations.
#>

[CmdletBinding()]
param(
    [string]$Profile,
    [string]$Environment,
    [switch]$Clean,
    [switch]$WhatIf
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
            Config = "src/Plugins/PPDSDemo.Plugins/registrations.json"
        },
        @{
            Name = "PPDSDemo.PluginPackage"
            Config = "src/PluginPackages/PPDSDemo.PluginPackage/registrations.json"
        }
    )

    Write-Host "`n=== Deploying Plugins ===" -ForegroundColor Cyan

    if ($WhatIf) {
        Write-Host "(What-If mode - no changes will be made)" -ForegroundColor Yellow
    }

    # Build common arguments
    $commonArgs = @()
    if ($Profile) {
        $commonArgs += "-p", $Profile
    }
    if ($Environment) {
        $commonArgs += "-env", $Environment
    }
    if ($Clean) {
        $commonArgs += "--clean"
    }
    if ($WhatIf) {
        $commonArgs += "--what-if"
    }

    foreach ($plugin in $plugins) {
        Write-Host "`n$($plugin.Name):" -ForegroundColor Yellow

        if (-not (Test-Path $plugin.Config)) {
            Write-Warning "  Config not found: $($plugin.Config)"
            Write-Warning "  Run Update-PluginRegistrations.ps1 -Extract first."
            continue
        }

        $cmd = "ppds plugins deploy -c `"$($plugin.Config)`""
        if ($commonArgs.Count -gt 0) {
            $cmd += " " + ($commonArgs -join " ")
        }

        Write-Host "  $cmd" -ForegroundColor DarkGray
        Invoke-Expression $cmd

        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Completed successfully" -ForegroundColor Green
        } else {
            Write-Error "  Deployment failed"
        }
    }

    Write-Host "`n=== Complete ===" -ForegroundColor Cyan

} finally {
    Pop-Location
}
