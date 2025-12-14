<#
.SYNOPSIS
    Extracts plugin registrations from compiled assemblies.

.DESCRIPTION
    Loads compiled plugin DLLs via .NET reflection, finds all classes with
    [PluginStep] attributes, extracts step and image configurations, and
    generates registrations.json files for each plugin project.

.PARAMETER Project
    Specific project name to extract. If not specified, extracts all.

.PARAMETER Configuration
    Build configuration to use (Release or Debug). Default: Release.

.PARAMETER Build
    Build projects before extraction.

.PARAMETER OutputPath
    Custom output path for registrations.json. Default: project directory.

.EXAMPLE
    .\Extract-PluginRegistrations.ps1
    Extracts registrations from all plugin projects.

.EXAMPLE
    .\Extract-PluginRegistrations.ps1 -Project PPDSDemo.Plugins
    Extracts registrations from a specific project.

.EXAMPLE
    .\Extract-PluginRegistrations.ps1 -Build
    Builds projects first, then extracts registrations.

.NOTES
    The generated registrations.json files should be committed to source control.
    They are used by Deploy-Plugins.ps1 for deployment.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Project,

    [Parameter()]
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [Parameter()]
    [switch]$Build,

    [Parameter()]
    [string]$OutputPath
)

# =============================================================================
# Script Initialization
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
$repoRoot = Split-Path $scriptRoot -Parent

# Import shared module
$modulePath = Join-Path $scriptRoot "lib\PluginDeployment.psm1"
if (-not (Test-Path $modulePath)) {
    Write-Error "Module not found: $modulePath"
    exit 1
}
Import-Module $modulePath -Force

Write-PluginLog "Plugin Registration Extractor"
Write-PluginLog "Repository: $repoRoot"
Write-PluginLog "Configuration: $Configuration"
Write-PluginLog ""

# =============================================================================
# Main Logic
# =============================================================================

# Change to repo root for relative paths
Push-Location $repoRoot
try {
    # Discover plugin projects
    Write-PluginLog "Discovering plugin projects..."
    $allProjects = Get-PluginProjects -RepositoryRoot $repoRoot

    if ($allProjects.Count -eq 0) {
        Write-PluginWarning "No plugin projects found"
        exit 0
    }

    Write-PluginLog "Found $($allProjects.Count) plugin project(s)"

    # Filter to specific project if requested
    $projects = if ($Project) {
        $filtered = $allProjects | Where-Object { $_.Name -eq $Project }
        if (-not $filtered) {
            Write-PluginError "Project not found: $Project"
            Write-PluginLog "Available projects:"
            $allProjects | ForEach-Object { Write-PluginLog "  - $($_.Name) ($($_.Type))" }
            exit 1
        }
        $filtered
    } else {
        $allProjects
    }

    # Build if requested
    if ($Build) {
        Write-PluginLog ""
        Write-PluginLog "Building projects..."
        foreach ($proj in $projects) {
            Write-PluginLog "Building: $($proj.Name)"
            $buildResult = & dotnet build $proj.ProjectPath -c $Configuration --nologo -v q 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-PluginError "Build failed for $($proj.Name)"
                Write-PluginLog $buildResult
                exit 1
            }
            Write-PluginSuccess "  Built successfully"

            # Update DLL path after build
            $dllPath = Join-Path $proj.ProjectDir "bin/$Configuration/net462/$($proj.Name).dll"
            if (Test-Path $dllPath) {
                $proj.DllPath = $dllPath
                $proj.RelativeDllPath = (Resolve-Path -Path $dllPath -Relative) -replace '\\','/'
            }
        }
    }

    # Extract registrations
    Write-PluginLog ""
    Write-PluginLog "Extracting plugin registrations..."

    $allResults = @()
    $successCount = 0
    $errorCount = 0

    foreach ($proj in $projects) {
        Write-PluginLog ""
        Write-PluginLog "Processing: $($proj.Name) ($($proj.Type))"

        # Check if DLL exists
        if (-not $proj.DllPath -or -not (Test-Path $proj.DllPath)) {
            Write-PluginWarning "  DLL not found. Run with -Build or build manually first."
            Write-PluginWarning "  Expected: bin/$Configuration/net462/$($proj.Name).dll"
            $errorCount++
            continue
        }

        Write-PluginLog "  DLL: $($proj.RelativeDllPath)"

        try {
            # Extract ALL plugin/workflow type names (for orphan detection during deployment)
            $allTypeNames = Get-AllPluginTypeNames -DllPath $proj.DllPath
            Write-PluginDebug "  Found $($allTypeNames.Count) total plugin/workflow type(s)"

            # Extract registrations via reflection (only plugins with [PluginStep] attributes)
            $plugins = Get-PluginRegistrations -DllPath $proj.DllPath

            if ($plugins.Count -eq 0) {
                Write-PluginWarning "  No plugins with [PluginStep] attributes found"
                # Still continue to generate registrations.json with allTypeNames
            }

            Write-PluginLog "  Found $($plugins.Count) plugin class(es) with step registrations"

            # Count total steps and images
            $totalSteps = ($plugins | ForEach-Object { $_.steps.Count } | Measure-Object -Sum).Sum
            $totalImages = ($plugins | ForEach-Object { $_.steps | ForEach-Object { $_.images.Count } } | Measure-Object -Sum).Sum
            Write-PluginLog "  Total steps: $totalSteps, Total images: $totalImages"

            # Check for existing registrations.json to preserve solution property
            $existingJsonPath = Join-Path $proj.ProjectDir "registrations.json"
            $existingSolution = $null
            $existingPackagePath = $null
            if (Test-Path $existingJsonPath) {
                try {
                    $existingReg = Read-RegistrationJson -Path $existingJsonPath
                    if ($existingReg -and $existingReg.assemblies) {
                        $existingAsm = $existingReg.assemblies | Where-Object { $_.name -eq $proj.Name } | Select-Object -First 1
                        if ($existingAsm) {
                            $existingSolution = $existingAsm.solution
                            $existingPackagePath = $existingAsm.packagePath
                        }
                    }
                } catch {
                    Write-PluginDebug "  Could not read existing registrations.json: $($_.Exception.Message)"
                }
            }

            # Create assembly registration object
            $assemblyReg = [PSCustomObject]@{
                name = $proj.Name
                type = $proj.Type
                solution = $existingSolution
                path = $proj.RelativeDllPath
                allTypeNames = $allTypeNames
                plugins = $plugins
            }

            # Add NuGet package path if applicable (preserve existing or use discovered)
            $packagePath = if ($proj.RelativeNupkgPath) { $proj.RelativeNupkgPath } else { $existingPackagePath }
            if ($proj.Type -eq "Nuget" -and $packagePath) {
                $assemblyReg | Add-Member -MemberType NoteProperty -Name "packagePath" -Value $packagePath
            }

            # Generate JSON
            $registrationJson = ConvertTo-RegistrationJson -Assemblies @($assemblyReg)

            # Determine output path
            $jsonOutputPath = if ($OutputPath) {
                $OutputPath
            } else {
                Join-Path $proj.ProjectDir "registrations.json"
            }

            # Write JSON file (UTF8 without BOM)
            [System.IO.File]::WriteAllText($jsonOutputPath, $registrationJson, [System.Text.UTF8Encoding]::new($false))
            Write-PluginSuccess "  Generated: $jsonOutputPath"

            # List extracted plugins
            foreach ($plugin in $plugins) {
                Write-PluginDebug "    - $($plugin.typeName)"
                foreach ($step in $plugin.steps) {
                    Write-PluginDebug "      Step: $($step.message) of $($step.entity) ($($step.stage), $($step.mode))"
                    foreach ($image in $step.images) {
                        Write-PluginDebug "        Image: $($image.name) ($($image.imageType))"
                    }
                }
            }

            $allResults += $assemblyReg
            $successCount++
        }
        catch {
            Write-PluginError "  Failed to extract: $($_.Exception.Message)"
            $errorCount++
        }
    }

    # Summary
    Write-PluginLog ""
    Write-PluginLog ("=" * 60)
    Write-PluginLog "Extraction Summary"
    Write-PluginLog ("=" * 60)
    Write-PluginLog "Projects processed: $($projects.Count)"
    Write-PluginSuccess "Successful: $successCount"
    if ($errorCount -gt 0) {
        Write-PluginError "Failed: $errorCount"
    }

    if ($allResults.Count -gt 0) {
        $totalPlugins = ($allResults | ForEach-Object { $_.plugins.Count } | Measure-Object -Sum).Sum
        $totalSteps = ($allResults | ForEach-Object { $_.plugins | ForEach-Object { $_.steps.Count } } | Measure-Object -Sum).Sum
        Write-PluginLog ""
        Write-PluginLog "Total plugins: $totalPlugins"
        Write-PluginLog "Total steps: $totalSteps"
    }

    Write-PluginLog ""
    Write-PluginLog "Generated files should be committed to source control."

    if ($errorCount -gt 0) {
        exit 1
    }
}
finally {
    Pop-Location
}
