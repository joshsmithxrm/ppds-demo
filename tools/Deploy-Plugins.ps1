<#
.SYNOPSIS
    Deploys plugin assemblies and registers steps to Dataverse.

.DESCRIPTION
    Deploys plugin assemblies using Web API and registers/updates SDK message
    processing steps and images using the Dataverse Web API.

    Supports both classic plugin assemblies and plugin packages (NuGet).

    Authentication hierarchy:
    1. Explicit parameters (-ClientId, -ClientSecret, -TenantId, -EnvironmentUrl)
    2. Environment variables (from .env.dev or .env file)
    3. Interactive OAuth browser login (-Interactive)

.PARAMETER Environment
    Target environment label: Dev (default), QA, Prod.

.PARAMETER Project
    Specific project to deploy. If not specified, deploys all.

.PARAMETER Force
    Remove orphaned steps that exist in Dataverse but not in configuration.

.PARAMETER WhatIf
    Show what would be deployed without making changes.

.PARAMETER SkipAssembly
    Skip deploying the assembly, only register/update steps.

.PARAMETER Build
    Build projects before deployment.

.PARAMETER EnvironmentUrl
    Dataverse environment URL (e.g., https://myorg.crm.dynamics.com).

.PARAMETER ClientId
    Service principal application (client) ID.

.PARAMETER ClientSecret
    Service principal client secret.

.PARAMETER TenantId
    Azure AD tenant ID.

.PARAMETER EnvFile
    Path to .env file with credentials.

.PARAMETER Interactive
    Use interactive browser-based OAuth login.

.EXAMPLE
    .\Deploy-Plugins.ps1
    Deploys all plugins using .env.dev credentials.

.EXAMPLE
    .\Deploy-Plugins.ps1 -Interactive
    Deploys using browser-based login.

.EXAMPLE
    .\Deploy-Plugins.ps1 -EnvironmentUrl "https://myorg.crm.dynamics.com" -ClientId "..." -ClientSecret "..." -TenantId "..."
    Deploys using explicit service principal credentials.

.EXAMPLE
    .\Deploy-Plugins.ps1 -Environment QA -Project PPDSDemo.Plugins
    Deploys specific plugin assembly.

.EXAMPLE
    .\Deploy-Plugins.ps1 -Force
    Deploys and removes orphaned steps.

.EXAMPLE
    .\Deploy-Plugins.ps1 -WhatIf
    Shows what would be deployed without making changes.

.NOTES
    Requires Microsoft.Xrm.Data.PowerShell module.
    Install with: Install-Module Microsoft.Xrm.Data.PowerShell -Scope CurrentUser
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter()]
    [ValidateSet("Dev", "QA", "Prod")]
    [string]$Environment = "Dev",

    [Parameter()]
    [string]$Project,

    [Parameter()]
    [switch]$Force,

    [Parameter()]
    [switch]$SkipAssembly,

    [Parameter()]
    [switch]$Build,

    # Authentication options
    [Parameter()]
    [string]$EnvironmentUrl,

    [Parameter()]
    [string]$ClientId,

    [Parameter()]
    [string]$ClientSecret,

    [Parameter()]
    [string]$TenantId,

    [Parameter()]
    [string]$EnvFile,

    [Parameter()]
    [switch]$Interactive
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

# Import authentication module
$authPath = Join-Path $scriptRoot "Common-Auth.ps1"
if (-not (Test-Path $authPath)) {
    Write-Error "Authentication module not found: $authPath"
    exit 1
}
. $authPath

# Get WhatIf from common parameters
$isWhatIf = $WhatIfPreference -or $PSCmdlet.MyInvocation.BoundParameters["WhatIf"]

Write-PluginLog "Plugin Deployment Tool"
Write-PluginLog "Repository: $repoRoot"
Write-PluginLog "Environment: $Environment"
if ($isWhatIf) {
    Write-PluginWarning "Running in WhatIf mode - no changes will be made"
}
Write-PluginLog ""

# =============================================================================
# Environment Setup
# =============================================================================

# Get Dataverse connection using Common-Auth
Write-PluginLog "Authenticating to Dataverse..."

$connectionParams = @{}
if ($EnvironmentUrl) { $connectionParams["EnvironmentUrl"] = $EnvironmentUrl }
if ($ClientId) { $connectionParams["ClientId"] = $ClientId }
if ($ClientSecret) { $connectionParams["ClientSecret"] = $ClientSecret }
if ($TenantId) { $connectionParams["TenantId"] = $TenantId }
if ($EnvFile) { $connectionParams["EnvFile"] = $EnvFile }
if ($Interactive) { $connectionParams["Interactive"] = $true }

try {
    $connection = Get-DataverseConnection @connectionParams
    if (-not $connection -or -not $connection.IsReady) {
        Write-PluginError "Failed to connect to Dataverse"
        exit 1
    }
}
catch {
    Write-PluginError "Authentication failed: $($_.Exception.Message)"
    Write-PluginLog ""
    Write-PluginLog "Authentication options:"
    Write-PluginLog "  1. Create .env.dev file with SP_APPLICATION_ID, SP_CLIENT_SECRET, DATAVERSE_URL"
    Write-PluginLog "  2. Use -ClientId, -ClientSecret, -TenantId, -EnvironmentUrl parameters"
    Write-PluginLog "  3. Use -Interactive flag for browser-based login"
    exit 1
}

# Get API URL and Auth Headers from connection
$connectedEnv = $connection.ConnectedOrgFriendlyName
$connectedUrl = $connection.ConnectedOrgPublishedEndpoints["WebApplication"]
Write-PluginSuccess "Connected to: $connectedEnv"
Write-PluginLog "URL: $connectedUrl"

try {
    $apiUrl = Get-WebApiBaseUrl -Connection $connection
    $authHeaders = Get-AuthHeaders -Connection $connection
    Write-PluginDebug "API URL: $apiUrl"
}
catch {
    Write-PluginError "Failed to get API URL or auth headers: $($_.Exception.Message)"
    exit 1
}

# =============================================================================
# Project Discovery
# =============================================================================

Write-PluginLog ""
Write-PluginLog "Discovering plugin projects..."

Push-Location $repoRoot
try {
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
        @($filtered)
    } else {
        $allProjects
    }

    # Build if requested
    if ($Build) {
        Write-PluginLog ""
        Write-PluginLog "Building projects..."
        foreach ($proj in $projects) {
            Write-PluginLog "Building: $($proj.Name)"
            $buildResult = & dotnet build $proj.ProjectPath -c Release --nologo -v q 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-PluginError "Build failed for $($proj.Name)"
                Write-PluginLog $buildResult
                exit 1
            }
            Write-PluginSuccess "  Built successfully"

            # Update DLL path after build
            $dllPath = Join-Path $proj.ProjectDir "bin/Release/net462/$($proj.Name).dll"
            if (Test-Path $dllPath) {
                $proj.DllPath = $dllPath
            }
        }
    }

    # =============================================================================
    # Deployment Loop
    # =============================================================================

    Write-PluginLog ""
    Write-PluginLog "Starting deployment..."

    $totalStepsCreated = 0
    $totalStepsUpdated = 0
    $totalImagesCreated = 0
    $totalImagesUpdated = 0
    $totalOrphansWarned = 0
    $totalOrphansDeleted = 0

    foreach ($proj in $projects) {
        Write-PluginLog ""
        Write-PluginLog ("=" * 60)
        Write-PluginLog "Deploying: $($proj.Name) ($($proj.Type))"
        Write-PluginLog ("=" * 60)

        # Check for registrations.json
        $registrationsPath = Join-Path $proj.ProjectDir "registrations.json"
        if (-not (Test-Path $registrationsPath)) {
            Write-PluginWarning "No registrations.json found. Run Extract-PluginRegistrations.ps1 first."
            continue
        }

        # Load registrations
        $registrations = Read-RegistrationJson -Path $registrationsPath
        if (-not $registrations -or -not $registrations.assemblies) {
            Write-PluginWarning "Invalid or empty registrations.json"
            continue
        }

        $asmReg = $registrations.assemblies | Select-Object -First 1

        # Validate solution requirement
        $solutionUniqueName = $asmReg.solution
        $solution = $null

        if ($asmReg.type -eq "Nuget" -and -not $solutionUniqueName) {
            Write-PluginError "Solution is required for plugin packages (Nuget type)"
            Write-PluginLog "Add 'solution' property to registrations.json for this assembly"
            continue
        }

        # Look up solution if specified
        if ($solutionUniqueName -and -not $isWhatIf) {
            Write-PluginLog "Looking up solution: $solutionUniqueName"
            $solution = Get-Solution -ApiUrl $apiUrl -AuthHeaders $authHeaders -UniqueName $solutionUniqueName
            if (-not $solution) {
                Write-PluginError "Solution not found: $solutionUniqueName"
                Write-PluginLog "Ensure the solution exists in the target environment"
                continue
            }
            Write-PluginSuccess "Solution found: $($solution.friendlyname) (v$($solution.version))"
        }
        elseif ($solutionUniqueName -and $isWhatIf) {
            Write-PluginLog "[WhatIf] Would use solution: $solutionUniqueName"
        }

        # Deploy assembly
        if (-not $SkipAssembly) {
            $deployPath = if ($asmReg.type -eq "Nuget" -and $asmReg.packagePath) {
                Join-Path $repoRoot $asmReg.packagePath
            } else {
                $proj.DllPath
            }

            if (-not $deployPath -or -not (Test-Path $deployPath)) {
                Write-PluginWarning "Assembly/package not found: $deployPath"
                Write-PluginWarning "Build the project first, or use -Build parameter"
                continue
            }

            $deploySuccess = Deploy-PluginAssembly -ApiUrl $apiUrl -AuthHeaders $authHeaders -Path $deployPath -AssemblyName $asmReg.name -Type $asmReg.type -WhatIf:$isWhatIf
            if (-not $deploySuccess -and -not $isWhatIf) {
                Write-PluginError "Failed to deploy assembly, skipping step registration"
                continue
            }
        }

        # Get assembly record from Dataverse
        Write-PluginLog "Looking up assembly in Dataverse..."
        $assembly = $null
        if (-not $isWhatIf) {
            try {
                $assembly = Get-PluginAssembly -ApiUrl $apiUrl -AuthHeaders $authHeaders -Name $asmReg.name
                if (-not $assembly) {
                    Write-PluginError "Assembly not found in Dataverse after deployment: $($asmReg.name)"
                    Write-PluginLog "This may indicate the pac plugin push failed silently."
                    continue
                }
                Write-PluginLog "Assembly ID: $($assembly.pluginassemblyid)"

                # Add assembly to solution if specified
                if ($solutionUniqueName) {
                    Add-SolutionComponent -ApiUrl $apiUrl -AuthHeaders $authHeaders `
                        -SolutionUniqueName $solutionUniqueName `
                        -ComponentId $assembly.pluginassemblyid `
                        -ComponentType $ComponentType.PluginAssembly | Out-Null
                }
            }
            catch {
                Write-PluginWarning "Could not query assembly: $($_.Exception.Message)"
                Write-PluginLog "Step registration will be skipped. The assembly may need manual registration."
                continue
            }
        }

        # Track configured step names for orphan detection
        $configuredStepNames = @()

        # Process each plugin
        foreach ($plugin in $asmReg.plugins) {
            Write-PluginLog ""
            Write-PluginLog "Plugin: $($plugin.typeName)"

            # Get plugin type record
            $pluginType = $null
            if (-not $isWhatIf -and $assembly) {
                try {
                    $pluginType = Get-PluginType -ApiUrl $apiUrl -AuthHeaders $authHeaders -AssemblyId $assembly.pluginassemblyid -TypeName $plugin.typeName
                    if (-not $pluginType) {
                        Write-PluginWarning "  Plugin type not found: $($plugin.typeName)"
                        Write-PluginLog "  This may happen if the assembly was just deployed. Try running again."
                        continue
                    }
                    Write-PluginDebug "  Plugin Type ID: $($pluginType.plugintypeid)"
                }
                catch {
                    Write-PluginWarning "  Could not query plugin type: $($_.Exception.Message)"
                    continue
                }
            }

            # Process each step
            foreach ($step in $plugin.steps) {
                Write-PluginLog "  Step: $($step.name)"
                $configuredStepNames += $step.name

                # Get SDK message and filter
                $message = $null
                $filter = $null

                if (-not $isWhatIf) {
                    try {
                        $message = Get-SdkMessage -ApiUrl $apiUrl -AuthHeaders $authHeaders -MessageName $step.message
                        if (-not $message) {
                            Write-PluginError "    SDK Message not found: $($step.message)"
                            continue
                        }

                        $filter = Get-SdkMessageFilter -ApiUrl $apiUrl -AuthHeaders $authHeaders -MessageId $message.sdkmessageid -EntityLogicalName $step.entity
                        if (-not $filter) {
                            Write-PluginError "    SDK Message Filter not found for: $($step.message) / $($step.entity)"
                            continue
                        }
                    }
                    catch {
                        Write-PluginWarning "    Could not query message/filter: $($_.Exception.Message)"
                        continue
                    }
                }

                # Convert stage/mode to Dataverse values
                $stageValue = $DataverseStageValues[$step.stage]
                $modeValue = $DataverseModeValues[$step.mode]

                # Check if step exists
                $existingStep = $null
                if (-not $isWhatIf) {
                    try {
                        $existingStep = Get-ProcessingStep -ApiUrl $apiUrl -AuthHeaders $authHeaders -StepName $step.name
                    }
                    catch {
                        # Step doesn't exist, will create
                    }
                }

                $stepData = @{
                    Name = $step.name
                    Stage = $stageValue
                    Mode = $modeValue
                    ExecutionOrder = $step.executionOrder
                    FilteringAttributes = $step.filteringAttributes
                    Configuration = $step.configuration
                    PluginTypeId = $pluginType.plugintypeid
                    MessageId = $message.sdkmessageid
                    FilterId = $filter.sdkmessagefilterid
                }

                $stepId = $null
                if ($existingStep) {
                    Write-PluginLog "    Updating existing step..."
                    if (-not $isWhatIf) {
                        try {
                            Update-ProcessingStep -ApiUrl $apiUrl -AuthHeaders $authHeaders -StepId $existingStep.sdkmessageprocessingstepid -StepData $stepData
                            $stepId = $existingStep.sdkmessageprocessingstepid
                            $totalStepsUpdated++
                            Write-PluginSuccess "    Step updated"
                        }
                        catch {
                            Write-PluginError "    Failed to update step: $($_.Exception.Message)"
                            continue
                        }
                    } else {
                        Write-PluginLog "    [WhatIf] Would update step"
                        $totalStepsUpdated++
                    }
                } else {
                    Write-PluginLog "    Creating new step..."
                    if (-not $isWhatIf) {
                        try {
                            $newStep = New-ProcessingStep -ApiUrl $apiUrl -AuthHeaders $authHeaders -StepData $stepData
                            $stepId = $newStep.sdkmessageprocessingstepid
                            $totalStepsCreated++
                            Write-PluginSuccess "    Step created: $stepId"

                            # Add step to solution if specified
                            if ($solutionUniqueName) {
                                Add-SolutionComponent -ApiUrl $apiUrl -AuthHeaders $authHeaders `
                                    -SolutionUniqueName $solutionUniqueName `
                                    -ComponentId $stepId `
                                    -ComponentType $ComponentType.SdkMessageProcessingStep | Out-Null
                            }
                        }
                        catch {
                            Write-PluginError "    Failed to create step: $($_.Exception.Message)"
                            continue
                        }
                    } else {
                        Write-PluginLog "    [WhatIf] Would create step"
                        $totalStepsCreated++
                    }
                }

                # Process images
                foreach ($image in $step.images) {
                    Write-PluginLog "    Image: $($image.name) ($($image.imageType))"

                    $imageTypeValue = $DataverseImageTypeValues[$image.imageType]

                    # Check if image exists
                    $existingImages = @()
                    if (-not $isWhatIf -and $stepId) {
                        try {
                            $existingImages = Get-StepImages -ApiUrl $apiUrl -AuthHeaders $authHeaders -StepId $stepId
                        }
                        catch {
                            # No images exist
                        }
                    }

                    $existingImage = $existingImages | Where-Object { $_.name -eq $image.name } | Select-Object -First 1

                    $imageData = @{
                        Name = $image.name
                        EntityAlias = if ($image.entityAlias) { $image.entityAlias } else { $image.name }
                        ImageType = $imageTypeValue
                        Attributes = $image.attributes
                        StepId = $stepId
                    }

                    if ($existingImage) {
                        Write-PluginLog "      Updating existing image..."
                        if (-not $isWhatIf) {
                            try {
                                Update-StepImage -ApiUrl $apiUrl -AuthHeaders $authHeaders -ImageId $existingImage.sdkmessageprocessingstepimageid -ImageData $imageData
                                $totalImagesUpdated++
                                Write-PluginSuccess "      Image updated"
                            }
                            catch {
                                Write-PluginError "      Failed to update image: $($_.Exception.Message)"
                            }
                        } else {
                            Write-PluginLog "      [WhatIf] Would update image"
                            $totalImagesUpdated++
                        }
                    } else {
                        Write-PluginLog "      Creating new image..."
                        if (-not $isWhatIf) {
                            try {
                                $newImage = New-StepImage -ApiUrl $apiUrl -AuthHeaders $authHeaders -ImageData $imageData
                                $totalImagesCreated++
                                Write-PluginSuccess "      Image created"

                                # Add image to solution if specified
                                if ($solutionUniqueName -and $newImage.sdkmessageprocessingstepimageid) {
                                    Add-SolutionComponent -ApiUrl $apiUrl -AuthHeaders $authHeaders `
                                        -SolutionUniqueName $solutionUniqueName `
                                        -ComponentId $newImage.sdkmessageprocessingstepimageid `
                                        -ComponentType $ComponentType.SdkMessageProcessingStepImage | Out-Null
                                }
                            }
                            catch {
                                Write-PluginError "      Failed to create image: $($_.Exception.Message)"
                            }
                        } else {
                            Write-PluginLog "      [WhatIf] Would create image"
                            $totalImagesCreated++
                        }
                    }
                }
            }
        }

        # Check for orphaned steps
        if (-not $isWhatIf -and $assembly) {
            Write-PluginLog ""
            Write-PluginLog "Checking for orphaned steps..."

            try {
                $existingSteps = Get-ProcessingStepsForAssembly -ApiUrl $apiUrl -AuthHeaders $authHeaders -AssemblyId $assembly.pluginassemblyid

                foreach ($existingStep in $existingSteps) {
                    if ($configuredStepNames -notcontains $existingStep.name) {
                        if ($Force) {
                            Write-PluginWarning "Deleting orphaned step: $($existingStep.name)"
                            try {
                                Remove-ProcessingStep -ApiUrl $apiUrl -AuthHeaders $authHeaders -StepId $existingStep.sdkmessageprocessingstepid
                                $totalOrphansDeleted++
                                Write-PluginSuccess "  Deleted"
                            }
                            catch {
                                Write-PluginError "  Failed to delete: $($_.Exception.Message)"
                            }
                        } else {
                            Write-PluginWarning "Orphaned step found: $($existingStep.name)"
                            Write-PluginLog "  Use -Force to delete orphaned steps"
                            $totalOrphansWarned++
                        }
                    }
                }
            }
            catch {
                Write-PluginWarning "Could not check for orphaned steps: $($_.Exception.Message)"
            }
        }
    }

    # =============================================================================
    # Summary
    # =============================================================================

    Write-PluginLog ""
    Write-PluginLog ("=" * 60)
    Write-PluginLog "Deployment Summary"
    Write-PluginLog ("=" * 60)
    Write-PluginLog "Environment: $Environment ($connectedUrl)"
    Write-PluginLog "Projects deployed: $($projects.Count)"
    Write-PluginLog ""
    Write-PluginSuccess "Steps created: $totalStepsCreated"
    Write-PluginSuccess "Steps updated: $totalStepsUpdated"
    Write-PluginSuccess "Images created: $totalImagesCreated"
    Write-PluginSuccess "Images updated: $totalImagesUpdated"

    if ($totalOrphansWarned -gt 0) {
        Write-PluginWarning "Orphaned steps (not deleted): $totalOrphansWarned"
    }
    if ($totalOrphansDeleted -gt 0) {
        Write-PluginWarning "Orphaned steps deleted: $totalOrphansDeleted"
    }

    if ($isWhatIf) {
        Write-PluginLog ""
        Write-PluginWarning "WhatIf mode: No actual changes were made"
    }
}
finally {
    Pop-Location
}
