<#
.SYNOPSIS
    Shared module for plugin deployment tooling.

.DESCRIPTION
    Contains shared functions for extracting plugin registrations from compiled
    assemblies and deploying them to Dataverse environments.

.NOTES
    This module is imported by Extract-PluginRegistrations.ps1 and Deploy-Plugins.ps1.
#>

# =============================================================================
# Constants
# =============================================================================

$script:PluginStageMap = @{
    10 = "PreValidation"
    20 = "PreOperation"
    40 = "PostOperation"
}

$script:PluginModeMap = @{
    0 = "Synchronous"
    1 = "Asynchronous"
}

$script:PluginImageTypeMap = @{
    0 = "PreImage"
    1 = "PostImage"
    2 = "Both"
}

# Dataverse stage values
$script:DataverseStageValues = @{
    "PreValidation" = 10
    "PreOperation" = 20
    "PostOperation" = 40
}

$script:DataverseModeValues = @{
    "Synchronous" = 0
    "Asynchronous" = 1
}

$script:DataverseImageTypeValues = @{
    "PreImage" = 0
    "PostImage" = 1
    "Both" = 2
}

# =============================================================================
# Logging Functions
# =============================================================================

function Write-PluginLog {
    <#
    .SYNOPSIS
        Writes a log message with timestamp and level.
    #>
    param(
        [string]$Message,
        [ValidateSet("INFO", "SUCCESS", "WARNING", "ERROR", "DEBUG")]
        [string]$Level = "INFO"
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "INFO"    { "White" }
        "SUCCESS" { "Green" }
        "WARNING" { "Yellow" }
        "ERROR"   { "Red" }
        "DEBUG"   { "Gray" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

function Write-PluginSuccess { param([string]$Message) Write-PluginLog $Message "SUCCESS" }
function Write-PluginWarning { param([string]$Message) Write-PluginLog $Message "WARNING" }
function Write-PluginError { param([string]$Message) Write-PluginLog $Message "ERROR" }
function Write-PluginDebug { param([string]$Message) Write-PluginLog $Message "DEBUG" }

# =============================================================================
# Project Discovery Functions
# =============================================================================

function Get-PluginProjects {
    <#
    .SYNOPSIS
        Discovers plugin projects in the repository.
    .PARAMETER RepositoryRoot
        Root of the repository.
    .OUTPUTS
        Array of plugin project objects with Name, Path, Type, and DllPath.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $projects = @()

    # Find classic plugin assemblies
    $classicPath = Join-Path $RepositoryRoot "src/Plugins"
    if (Test-Path $classicPath) {
        Get-ChildItem -Path $classicPath -Filter "*.csproj" -Recurse | ForEach-Object {
            $projectDir = $_.DirectoryName
            $projectName = $_.BaseName

            # Look for compiled DLL
            $dllPath = Join-Path $projectDir "bin/Release/net462/$projectName.dll"
            $debugDllPath = Join-Path $projectDir "bin/Debug/net462/$projectName.dll"

            $actualDllPath = if (Test-Path $dllPath) { $dllPath }
                             elseif (Test-Path $debugDllPath) { $debugDllPath }
                             else { $null }

            $projects += [PSCustomObject]@{
                Name = $projectName
                ProjectPath = $_.FullName
                ProjectDir = $projectDir
                Type = "Assembly"
                DllPath = $actualDllPath
                RelativeDllPath = if ($actualDllPath) {
                    (Resolve-Path -Path $actualDllPath -Relative -ErrorAction SilentlyContinue) -replace '\\','/'
                } else { $null }
            }
        }
    }

    # Find plugin packages (NuGet)
    $packagePath = Join-Path $RepositoryRoot "src/PluginPackages"
    if (Test-Path $packagePath) {
        Get-ChildItem -Path $packagePath -Filter "*.csproj" -Recurse | ForEach-Object {
            $projectDir = $_.DirectoryName
            $projectName = $_.BaseName

            # Look for compiled DLL (packages also produce DLLs for reflection)
            $dllPath = Join-Path $projectDir "bin/Release/net462/$projectName.dll"
            $debugDllPath = Join-Path $projectDir "bin/Debug/net462/$projectName.dll"

            $actualDllPath = if (Test-Path $dllPath) { $dllPath }
                             elseif (Test-Path $debugDllPath) { $debugDllPath }
                             else { $null }

            # Look for NuGet package
            $nupkgPattern = Join-Path $projectDir "bin/Release/*.nupkg"
            $nupkgFile = Get-ChildItem -Path $nupkgPattern -ErrorAction SilentlyContinue |
                         Sort-Object LastWriteTime -Descending |
                         Select-Object -First 1

            $projects += [PSCustomObject]@{
                Name = $projectName
                ProjectPath = $_.FullName
                ProjectDir = $projectDir
                Type = "Nuget"
                DllPath = $actualDllPath
                NupkgPath = $nupkgFile.FullName
                RelativeDllPath = if ($actualDllPath) {
                    (Resolve-Path -Path $actualDllPath -Relative -ErrorAction SilentlyContinue) -replace '\\','/'
                } else { $null }
                RelativeNupkgPath = if ($nupkgFile) {
                    (Resolve-Path -Path $nupkgFile.FullName -Relative -ErrorAction SilentlyContinue) -replace '\\','/'
                } else { $null }
            }
        }
    }

    return $projects
}

# =============================================================================
# Reflection Functions
# =============================================================================

function Get-PluginRegistrations {
    <#
    .SYNOPSIS
        Extracts plugin registrations from a compiled assembly using reflection.
    .PARAMETER DllPath
        Path to the compiled plugin DLL.
    .OUTPUTS
        Array of plugin registration objects.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$DllPath
    )

    if (-not (Test-Path $DllPath)) {
        throw "DLL not found: $DllPath"
    }

    # Load the assembly
    $dllFullPath = (Resolve-Path $DllPath).Path
    $dllDir = Split-Path $dllFullPath -Parent

    # Create a custom assembly resolver to load dependencies from the same folder
    $resolveHandler = [System.ResolveEventHandler]{
        param($sender, $args)
        $assemblyName = (New-Object System.Reflection.AssemblyName($args.Name)).Name
        $dllPath = Join-Path $dllDir "$assemblyName.dll"
        if (Test-Path $dllPath) {
            return [System.Reflection.Assembly]::LoadFrom($dllPath)
        }
        return $null
    }

    [System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolveHandler)

    try {
        $assembly = [System.Reflection.Assembly]::LoadFrom($dllFullPath)
        $plugins = @()

        foreach ($type in $assembly.GetExportedTypes()) {
            # Skip abstract classes and interfaces
            if ($type.IsAbstract -or $type.IsInterface) {
                continue
            }

            # Look for PluginStepAttribute
            $stepAttributes = $type.GetCustomAttributes($true) | Where-Object {
                $_.GetType().Name -eq "PluginStepAttribute"
            }

            if ($stepAttributes.Count -eq 0) {
                continue
            }

            # Get image attributes
            $imageAttributes = $type.GetCustomAttributes($true) | Where-Object {
                $_.GetType().Name -eq "PluginImageAttribute"
            }

            $steps = @()
            foreach ($stepAttr in $stepAttributes) {
                $stepId = $stepAttr.StepId
                $stepName = if ($stepAttr.Name) {
                    $stepAttr.Name
                } else {
                    "$($type.FullName): $($stepAttr.Message) of $($stepAttr.EntityLogicalName)"
                }

                # Map enum values to strings
                $stageValue = [int]$stepAttr.Stage
                $modeValue = [int]$stepAttr.Mode
                $stageName = $script:PluginStageMap[$stageValue]
                $modeName = $script:PluginModeMap[$modeValue]

                # Find images for this step
                $stepImages = @()
                foreach ($imageAttr in $imageAttributes) {
                    # If step has StepId, match by StepId; otherwise, associate with all steps
                    $shouldInclude = if ($stepId -and $imageAttr.StepId) {
                        $stepId -eq $imageAttr.StepId
                    } elseif (-not $stepId -and -not $imageAttr.StepId) {
                        $true  # Both have no StepId, associate
                    } elseif ($stepAttributes.Count -eq 1 -and -not $imageAttr.StepId) {
                        $true  # Only one step, associate unlinked images
                    } else {
                        $false
                    }

                    if ($shouldInclude) {
                        $imageTypeValue = [int]$imageAttr.ImageType
                        $imageTypeName = $script:PluginImageTypeMap[$imageTypeValue]

                        $stepImages += [PSCustomObject]@{
                            name = $imageAttr.Name
                            imageType = $imageTypeName
                            attributes = $imageAttr.Attributes
                            entityAlias = if ($imageAttr.EntityAlias) { $imageAttr.EntityAlias } else { $imageAttr.Name }
                        }
                    }
                }

                $steps += [PSCustomObject]@{
                    name = $stepName
                    message = $stepAttr.Message
                    entity = $stepAttr.EntityLogicalName
                    stage = $stageName
                    mode = $modeName
                    executionOrder = $stepAttr.ExecutionOrder
                    filteringAttributes = $stepAttr.FilteringAttributes
                    configuration = $stepAttr.UnsecureConfiguration
                    stepId = $stepId
                    images = $stepImages
                }
            }

            $plugins += [PSCustomObject]@{
                typeName = $type.FullName
                steps = $steps
            }
        }

        return $plugins
    }
    finally {
        [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($resolveHandler)
    }
}

# =============================================================================
# JSON Functions
# =============================================================================

function ConvertTo-RegistrationJson {
    <#
    .SYNOPSIS
        Converts plugin registration objects to JSON format.
    .PARAMETER Assemblies
        Array of assembly registration objects.
    .PARAMETER SchemaRelativePath
        Relative path from the output file to the schema file.
        Default: "../../../tools/schemas/plugin-registration.schema.json"
    .OUTPUTS
        JSON string.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [array]$Assemblies,

        [Parameter()]
        [string]$SchemaRelativePath = "../../../tools/schemas/plugin-registration.schema.json"
    )

    # Ensure plugins arrays are properly formatted even with single items
    $formattedAssemblies = @()
    foreach ($asm in $Assemblies) {
        $formattedPlugins = @()
        foreach ($plugin in $asm.plugins) {
            $formattedSteps = @()
            foreach ($step in $plugin.steps) {
                $formattedImages = @()
                foreach ($image in $step.images) {
                    $formattedImages += $image
                }
                $stepCopy = [PSCustomObject]@{
                    name = $step.name
                    message = $step.message
                    entity = $step.entity
                    stage = $step.stage
                    mode = $step.mode
                    executionOrder = $step.executionOrder
                    filteringAttributes = $step.filteringAttributes
                    configuration = $step.configuration
                    images = [array]$formattedImages
                }
                $formattedSteps += $stepCopy
            }
            $pluginCopy = [PSCustomObject]@{
                typeName = $plugin.typeName
                steps = [array]$formattedSteps
            }
            $formattedPlugins += $pluginCopy
        }

        $asmCopy = [PSCustomObject]@{
            name = $asm.name
            type = $asm.type
            solution = $asm.solution
            path = $asm.path
            plugins = [array]$formattedPlugins
        }

        # Add packagePath for NuGet packages
        if ($asm.type -eq "Nuget" -and $asm.packagePath) {
            $asmCopy | Add-Member -MemberType NoteProperty -Name "packagePath" -Value $asm.packagePath
        }

        $formattedAssemblies += $asmCopy
    }

    # Build JSON manually with proper formatting
    # This ensures consistent 2-space indentation and proper property ordering
    function Format-JsonValue {
        param($value, [int]$indent = 0)
        $indentStr = "  " * $indent

        if ($null -eq $value) {
            return "null"
        }
        elseif ($value -is [bool]) {
            return $value.ToString().ToLower()
        }
        elseif ($value -is [int] -or $value -is [long] -or $value -is [double]) {
            return $value.ToString()
        }
        elseif ($value -is [string]) {
            # Escape special characters
            $escaped = $value -replace '\\', '\\' -replace '"', '\"' -replace "`n", '\n' -replace "`r", '\r' -replace "`t", '\t'
            return "`"$escaped`""
        }
        elseif ($value -is [array] -or ($value -is [System.Collections.IEnumerable] -and $value -isnot [string] -and $value -isnot [hashtable] -and $value -isnot [PSCustomObject])) {
            $items = @($value)
            if ($items.Count -eq 0) {
                return "[]"
            }
            $innerIndent = "  " * ($indent + 1)
            $elements = @()
            foreach ($item in $items) {
                $elements += "$innerIndent$(Format-JsonValue $item ($indent + 1))"
            }
            return "[$([Environment]::NewLine)$($elements -join ",$([Environment]::NewLine)")$([Environment]::NewLine)$indentStr]"
        }
        elseif ($value -is [PSCustomObject]) {
            $props = $value.PSObject.Properties
            if ($props.Count -eq 0) {
                return "{}"
            }
            $innerIndent = "  " * ($indent + 1)
            $members = @()
            foreach ($prop in $props) {
                $members += "$innerIndent`"$($prop.Name)`": $(Format-JsonValue $prop.Value ($indent + 1))"
            }
            return "{$([Environment]::NewLine)$($members -join ",$([Environment]::NewLine)")$([Environment]::NewLine)$indentStr}"
        }
        elseif ($value -is [hashtable]) {
            if ($value.Count -eq 0) {
                return "{}"
            }
            $innerIndent = "  " * ($indent + 1)
            $members = @()
            foreach ($key in $value.Keys) {
                $members += "$innerIndent`"$key`": $(Format-JsonValue $value[$key] ($indent + 1))"
            }
            return "{$([Environment]::NewLine)$($members -join ",$([Environment]::NewLine)")$([Environment]::NewLine)$indentStr}"
        }
        else {
            return "`"$($value.ToString())`""
        }
    }

    # Build the registration object with explicit property ordering using PSCustomObject
    $registration = [PSCustomObject][ordered]@{
        '$schema' = $SchemaRelativePath
        version = "1.0"
        generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        assemblies = @($formattedAssemblies | ForEach-Object {
            $asm = $_
            $asmObj = [PSCustomObject][ordered]@{
                name = $asm.name
                type = $asm.type
                solution = $asm.solution
                path = $asm.path
                plugins = @($asm.plugins | ForEach-Object {
                    $plugin = $_
                    [PSCustomObject][ordered]@{
                        typeName = $plugin.typeName
                        steps = @($plugin.steps | ForEach-Object {
                            $step = $_
                            [PSCustomObject][ordered]@{
                                name = $step.name
                                message = $step.message
                                entity = $step.entity
                                stage = $step.stage
                                mode = $step.mode
                                executionOrder = $step.executionOrder
                                filteringAttributes = $step.filteringAttributes
                                configuration = $step.configuration
                                images = @($step.images | ForEach-Object {
                                    $img = $_
                                    [PSCustomObject][ordered]@{
                                        name = $img.name
                                        imageType = $img.imageType
                                        attributes = $img.attributes
                                        entityAlias = $img.entityAlias
                                    }
                                })
                            }
                        })
                    }
                })
            }
            # Add packagePath only for NuGet packages
            if ($asm.type -eq "Nuget" -and $asm.packagePath) {
                $asmObj | Add-Member -MemberType NoteProperty -Name "packagePath" -Value $asm.packagePath
            }
            $asmObj
        })
    }

    return Format-JsonValue $registration 0
}

function Read-RegistrationJson {
    <#
    .SYNOPSIS
        Reads a registrations.json file.
    .PARAMETER Path
        Path to the JSON file.
    .OUTPUTS
        Registration object.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path $Path)) {
        return $null
    }

    $content = Get-Content $Path -Raw
    return $content | ConvertFrom-Json
}

# =============================================================================
# Step Name Utilities
# =============================================================================

function Get-StepUniqueName {
    <#
    .SYNOPSIS
        Generates a unique name for a plugin step.
    .DESCRIPTION
        Creates a consistent naming convention for plugin steps to enable
        matching between config and Dataverse.
    #>
    param(
        [string]$AssemblyName,
        [string]$TypeName,
        [string]$Message,
        [string]$Entity,
        [string]$Stage
    )

    return "$TypeName`: $Message of $Entity ($Stage)"
}

# =============================================================================
# Web API Helper Functions
# =============================================================================

function Get-DataverseApiUrl {
    <#
    .SYNOPSIS
        Gets the Dataverse Web API URL from the current PAC auth context.
    #>
    param(
        [Parameter(Mandatory = $false)]
        [string]$EnvironmentUrl
    )

    if ($EnvironmentUrl) {
        return "$($EnvironmentUrl.TrimEnd('/'))/api/data/v9.2"
    }

    # Get URL from PAC CLI
    $whoOutput = pac org who --json 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Not authenticated to any environment. Run 'pac auth create' first."
    }

    $whoData = $whoOutput | ConvertFrom-Json
    # PAC CLI JSON uses OrgUrl property
    $envUrl = if ($whoData.OrgUrl) { $whoData.OrgUrl } elseif ($whoData.EnvironmentUrl) { $whoData.EnvironmentUrl } else { $whoData.OrganizationUrl }
    if (-not $envUrl) {
        throw "Could not determine environment URL from PAC CLI"
    }

    return "$($envUrl.TrimEnd('/'))/api/data/v9.2"
}

function Get-DataverseAuthToken {
    <#
    .SYNOPSIS
        Gets an authentication token from the PAC CLI context.
    #>
    param(
        [Parameter(Mandatory = $false)]
        [string]$EnvironmentUrl
    )

    # Use pac org who to get token
    $whoOutput = pac org who --json 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Not authenticated. Run 'pac auth create' first."
    }

    $whoData = $whoOutput | ConvertFrom-Json

    # PAC CLI doesn't expose token directly, so we need to use Azure CLI or MSAL
    # For now, we'll use the dataverse-webapi-client approach with pac auth
    return $null  # Token will be handled via PAC CLI internally
}

function Invoke-DataverseApi {
    <#
    .SYNOPSIS
        Makes a Web API call to Dataverse.
    .PARAMETER ApiUrl
        The base Web API URL.
    .PARAMETER Endpoint
        The API endpoint (relative to ApiUrl).
    .PARAMETER AuthHeaders
        Authentication headers including Authorization bearer token.
        Get these from Get-AuthHeaders in Common-Auth.ps1.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [string]$Endpoint,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter()]
        [ValidateSet("GET", "POST", "PATCH", "DELETE")]
        [string]$Method = "GET",

        [Parameter()]
        [object]$Body,

        [Parameter()]
        [switch]$WhatIf
    )

    $fullUrl = "$ApiUrl/$Endpoint"

    # Start with auth headers and add OData headers
    $headers = @{}
    foreach ($key in $AuthHeaders.Keys) {
        $headers[$key] = $AuthHeaders[$key]
    }
    $headers["OData-MaxVersion"] = "4.0"
    $headers["OData-Version"] = "4.0"
    $headers["Accept"] = "application/json"
    $headers["Prefer"] = "return=representation"

    if ($WhatIf) {
        Write-PluginLog "[WhatIf] $Method $fullUrl"
        if ($Body) {
            Write-PluginDebug ($Body | ConvertTo-Json -Depth 5 -Compress)
        }
        return $null
    }

    $params = @{
        Uri = $fullUrl
        Method = $Method
        Headers = $headers
        ContentType = "application/json; charset=utf-8"
    }

    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 10)
    }

    try {
        $response = Invoke-RestMethod @params -UseBasicParsing
        return $response
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $errorBody = $_.ErrorDetails.Message
        Write-PluginError "API Error ($statusCode): $errorBody"
        throw
    }
}

# =============================================================================
# Dataverse Query Functions
# =============================================================================

function Get-PluginAssembly {
    <#
    .SYNOPSIS
        Gets a plugin assembly record by name.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $filter = "`$filter=name eq '$Name'"
    $select = "`$select=pluginassemblyid,name,version,publickeytoken"
    $result = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "pluginassemblies?$filter&$select" -Method GET
    return $result.value | Select-Object -First 1
}

function Get-PluginPackage {
    <#
    .SYNOPSIS
        Gets a plugin package record by name or uniquename (for NuGet-based plugins).
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter()]
        [string]$UniqueName
    )

    $select = "`$select=pluginpackageid,name,uniquename,version"
    try {
        # Try by uniquename first if provided (more specific)
        if ($UniqueName) {
            $filter = "`$filter=uniquename eq '$UniqueName'"
            $result = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "pluginpackages?$filter&$select" -Method GET
            $package = $result.value | Select-Object -First 1
            if ($package) { return $package }
        }

        # Fall back to name search
        $filter = "`$filter=name eq '$Name'"
        $result = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "pluginpackages?$filter&$select" -Method GET
        return $result.value | Select-Object -First 1
    }
    catch {
        # Plugin packages may not exist in older environments
        Write-PluginDebug "Could not query plugin packages: $($_.Exception.Message)"
        return $null
    }
}

function Get-PluginType {
    <#
    .SYNOPSIS
        Gets a plugin type record by assembly ID and type name.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$AssemblyId,

        [Parameter(Mandatory = $true)]
        [string]$TypeName
    )

    $filter = "`$filter=_pluginassemblyid_value eq '$AssemblyId' and typename eq '$TypeName'"
    $select = "`$select=plugintypeid,typename,friendlyname"
    $result = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "plugintypes?$filter&$select" -Method GET
    return $result.value | Select-Object -First 1
}

function Get-SdkMessage {
    <#
    .SYNOPSIS
        Gets an SDK message by name.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$MessageName
    )

    $filter = "`$filter=name eq '$MessageName'"
    $select = "`$select=sdkmessageid,name"
    $result = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "sdkmessages?$filter&$select" -Method GET
    return $result.value | Select-Object -First 1
}

function Get-SdkMessageFilter {
    <#
    .SYNOPSIS
        Gets an SDK message filter for a specific message and entity.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$MessageId,

        [Parameter(Mandatory = $true)]
        [string]$EntityLogicalName
    )

    $filter = "`$filter=_sdkmessageid_value eq '$MessageId' and primaryobjecttypecode eq '$EntityLogicalName'"
    $select = "`$select=sdkmessagefilterid"
    $result = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "sdkmessagefilters?$filter&$select" -Method GET
    return $result.value | Select-Object -First 1
}

function Get-ProcessingStep {
    <#
    .SYNOPSIS
        Gets a processing step by name.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$StepName
    )

    $filter = "`$filter=name eq '$StepName'"
    $select = "`$select=sdkmessageprocessingstepid,name,stage,mode,rank,filteringattributes,configuration"
    $result = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "sdkmessageprocessingsteps?$filter&$select" -Method GET
    return $result.value | Select-Object -First 1
}

function Get-ProcessingStepsForAssembly {
    <#
    .SYNOPSIS
        Gets all processing steps for a plugin assembly.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$AssemblyId
    )

    # Get plugin types for assembly
    $typesFilter = "`$filter=_pluginassemblyid_value eq '$AssemblyId'"
    $types = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "plugintypes?$typesFilter" -Method GET

    $steps = @()
    foreach ($type in $types.value) {
        $stepsFilter = "`$filter=_plugintypeid_value eq '$($type.plugintypeid)'"
        $select = "`$select=sdkmessageprocessingstepid,name,stage,mode"
        $typeSteps = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "sdkmessageprocessingsteps?$stepsFilter&$select" -Method GET
        $steps += $typeSteps.value
    }

    return $steps
}

function Get-StepImages {
    <#
    .SYNOPSIS
        Gets images for a processing step.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$StepId
    )

    $filter = "`$filter=_sdkmessageprocessingstepid_value eq '$StepId'"
    $select = "`$select=sdkmessageprocessingstepimageid,name,entityalias,imagetype,attributes"
    $result = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "sdkmessageprocessingstepimages?$filter&$select" -Method GET
    return $result.value
}

# =============================================================================
# Solution Management Functions
# =============================================================================

function Get-Solution {
    <#
    .SYNOPSIS
        Gets a solution by unique name, including publisher prefix.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$UniqueName
    )

    $filter = "`$filter=uniquename eq '$UniqueName'"
    $select = "`$select=solutionid,uniquename,friendlyname,version"
    $expand = "`$expand=publisherid(`$select=customizationprefix,uniquename)"
    try {
        $result = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "solutions?$filter&$select&$expand" -Method GET
        $solution = $result.value | Select-Object -First 1

        # Add publisher prefix as a top-level property for easy access
        if ($solution -and $solution.publisherid) {
            $solution | Add-Member -MemberType NoteProperty -Name "publisherprefix" -Value $solution.publisherid.customizationprefix -Force
        }

        return $solution
    }
    catch {
        Write-PluginDebug "Could not query solution: $($_.Exception.Message)"
        return $null
    }
}

function Add-SolutionComponent {
    <#
    .SYNOPSIS
        Adds a component to a solution.
    .DESCRIPTION
        Uses the AddSolutionComponent action to add plugin assemblies, steps, or images to a solution.
    .PARAMETER ComponentType
        The type of component:
        - 91 = Plugin Assembly
        - 90 = Plugin Type
        - 92 = SDK Message Processing Step
        - 93 = SDK Message Processing Step Image
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$SolutionUniqueName,

        [Parameter(Mandatory = $true)]
        [string]$ComponentId,

        [Parameter(Mandatory = $true)]
        [ValidateSet(90, 91, 92, 93)]
        [int]$ComponentType,

        [Parameter()]
        [switch]$WhatIf
    )

    $componentTypeName = switch ($ComponentType) {
        91 { "Plugin Assembly" }
        90 { "Plugin Type" }
        92 { "SDK Message Processing Step" }
        93 { "SDK Message Processing Step Image" }
    }

    if ($WhatIf) {
        Write-PluginLog "[WhatIf] Would add $componentTypeName ($ComponentId) to solution '$SolutionUniqueName'"
        return $true
    }

    $body = @{
        ComponentId = $ComponentId
        ComponentType = $ComponentType
        SolutionUniqueName = $SolutionUniqueName
        AddRequiredComponents = $false
    }

    try {
        $result = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "AddSolutionComponent" -Method POST -Body $body
        Write-PluginDebug "Added $componentTypeName to solution '$SolutionUniqueName'"
        return $true
    }
    catch {
        # Component may already be in solution - check error
        $errorMsg = $_.Exception.Message
        if ($errorMsg -match "already exists" -or $errorMsg -match "duplicate") {
            Write-PluginDebug "$componentTypeName already in solution"
            return $true
        }
        Write-PluginWarning "Failed to add $componentTypeName to solution: $errorMsg"
        return $false
    }
}

# Component type constants for readability
$script:ComponentType = @{
    PluginType = 90
    PluginAssembly = 91
    SdkMessageProcessingStep = 92
    SdkMessageProcessingStepImage = 93
}

# =============================================================================
# Dataverse Create/Update Functions
# =============================================================================

function New-ProcessingStep {
    <#
    .SYNOPSIS
        Creates a new SDK message processing step.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [hashtable]$StepData,

        [Parameter()]
        [switch]$WhatIf
    )

    $body = @{
        name = $StepData.Name
        stage = $StepData.Stage
        mode = $StepData.Mode
        rank = $StepData.ExecutionOrder
        "plugintypeid@odata.bind" = "/plugintypes($($StepData.PluginTypeId))"
        "sdkmessageid@odata.bind" = "/sdkmessages($($StepData.MessageId))"
        "sdkmessagefilterid@odata.bind" = "/sdkmessagefilters($($StepData.FilterId))"
    }

    if ($StepData.FilteringAttributes) {
        $body.filteringattributes = $StepData.FilteringAttributes
    }

    if ($StepData.Configuration) {
        $body.configuration = $StepData.Configuration
    }

    return Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "sdkmessageprocessingsteps" -Method POST -Body $body -WhatIf:$WhatIf
}

function Update-ProcessingStep {
    <#
    .SYNOPSIS
        Updates an existing SDK message processing step.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$StepId,

        [Parameter(Mandatory = $true)]
        [hashtable]$StepData,

        [Parameter()]
        [switch]$WhatIf
    )

    $body = @{
        stage = $StepData.Stage
        mode = $StepData.Mode
        rank = $StepData.ExecutionOrder
    }

    if ($StepData.FilteringAttributes) {
        $body.filteringattributes = $StepData.FilteringAttributes
    } else {
        $body.filteringattributes = $null
    }

    if ($StepData.Configuration) {
        $body.configuration = $StepData.Configuration
    }

    return Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "sdkmessageprocessingsteps($StepId)" -Method PATCH -Body $body -WhatIf:$WhatIf
}

function Remove-ProcessingStep {
    <#
    .SYNOPSIS
        Deletes an SDK message processing step.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$StepId,

        [Parameter()]
        [switch]$WhatIf
    )

    return Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "sdkmessageprocessingsteps($StepId)" -Method DELETE -WhatIf:$WhatIf
}

function New-StepImage {
    <#
    .SYNOPSIS
        Creates a new step image.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [hashtable]$ImageData,

        [Parameter()]
        [switch]$WhatIf
    )

    $body = @{
        name = $ImageData.Name
        entityalias = $ImageData.EntityAlias
        imagetype = $ImageData.ImageType
        messagepropertyname = "Target"  # Required - specifies which message parameter to capture
        "sdkmessageprocessingstepid@odata.bind" = "/sdkmessageprocessingsteps($($ImageData.StepId))"
    }

    if ($ImageData.Attributes) {
        $body.attributes = $ImageData.Attributes
    }

    return Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "sdkmessageprocessingstepimages" -Method POST -Body $body -WhatIf:$WhatIf
}

function Update-StepImage {
    <#
    .SYNOPSIS
        Updates an existing step image.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$ImageId,

        [Parameter(Mandatory = $true)]
        [hashtable]$ImageData,

        [Parameter()]
        [switch]$WhatIf
    )

    $body = @{
        entityalias = $ImageData.EntityAlias
        imagetype = $ImageData.ImageType
    }

    if ($ImageData.Attributes) {
        $body.attributes = $ImageData.Attributes
    }

    return Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "sdkmessageprocessingstepimages($ImageId)" -Method PATCH -Body $body -WhatIf:$WhatIf
}

function Remove-StepImage {
    <#
    .SYNOPSIS
        Deletes a step image.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$ImageId,

        [Parameter()]
        [switch]$WhatIf
    )

    return Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "sdkmessageprocessingstepimages($ImageId)" -Method DELETE -WhatIf:$WhatIf
}

# =============================================================================
# Deployment Helper Functions
# =============================================================================

function Deploy-PluginAssembly {
    <#
    .SYNOPSIS
        Deploys a plugin assembly or package using Web API (for new) or PAC CLI (for updates).
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,

        [Parameter(Mandatory = $true)]
        [hashtable]$AuthHeaders,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$AssemblyName,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Assembly", "Nuget")]
        [string]$Type,

        [Parameter()]
        [string]$SolutionUniqueName,

        [Parameter()]
        [string]$PublisherPrefix,

        [Parameter()]
        [switch]$WhatIf
    )

    if (-not (Test-Path $Path)) {
        Write-PluginError "File not found: $Path"
        return $null
    }

    # Handle NuGet packages differently from assemblies
    if ($Type -eq "Nuget") {
        # Build the uniquename with publisher prefix (if provided)
        $packageUniqueName = if ($PublisherPrefix) {
            "${PublisherPrefix}_$AssemblyName"
        } else {
            $AssemblyName
        }

        # Check if plugin package already exists
        $existingPackage = Get-PluginPackage -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Name $AssemblyName -UniqueName $packageUniqueName

        if ($existingPackage) {
            # Update existing package using PAC CLI
            Write-PluginLog "Updating existing plugin package: $AssemblyName"
            $packageId = $existingPackage.pluginpackageid

            if ($WhatIf) {
                Write-PluginLog "[WhatIf] pac plugin push --pluginId $packageId --pluginFile $Path"
                return $existingPackage
            }

            $result = pac plugin push --pluginId $packageId --pluginFile $Path 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-PluginError "Failed to update plugin package: $result"
                return $null
            }
            Write-PluginSuccess "Plugin package updated successfully"

            # Return the assembly record for step registration
            # (plugin packages have assemblies inside them that we need for steps)
            return Get-PluginAssembly -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Name $AssemblyName
        }
        else {
            # Register new plugin package using Web API
            Write-PluginLog "Registering new plugin package: $AssemblyName"

            if (-not $SolutionUniqueName) {
                Write-PluginError "Solution is required for registering new plugin packages"
                return $null
            }

            if ($WhatIf) {
                Write-PluginLog "[WhatIf] Would register new plugin package via Web API to solution '$SolutionUniqueName'"
                return $null
            }

            # Read NuGet package bytes and convert to base64
            $bytes = [System.IO.File]::ReadAllBytes($Path)
            $content = [System.Convert]::ToBase64String($bytes)

            # Use the uniquename built earlier (includes publisher prefix)
            $body = @{
                name = $AssemblyName
                uniquename = $packageUniqueName
                content = $content
                version = "1.0.0"
            }

            Write-PluginDebug "Package unique name: $packageUniqueName"

            try {
                $response = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "pluginpackages" -Method POST -Body $body
                Write-PluginSuccess "Plugin package registered successfully"

                # Return the assembly record for step registration
                # (plugin packages have assemblies inside them that we need for steps)
                return Get-PluginAssembly -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Name $AssemblyName
            }
            catch {
                Write-PluginError "Failed to register plugin package: $($_.Exception.Message)"
                return $null
            }
        }
    }
    else {
        # Handle classic assemblies
        $existingAssembly = Get-PluginAssembly -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Name $AssemblyName

        if ($existingAssembly) {
            # Update existing assembly using PAC CLI
            Write-PluginLog "Updating existing assembly: $AssemblyName"
            $pluginId = $existingAssembly.pluginassemblyid

            if ($WhatIf) {
                Write-PluginLog "[WhatIf] pac plugin push --pluginId $pluginId --pluginFile $Path"
                return $existingAssembly
            }

            $result = pac plugin push --pluginId $pluginId --pluginFile $Path 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-PluginError "Failed to update assembly: $result"
                return $null
            }
            Write-PluginSuccess "Assembly updated successfully"
            return $existingAssembly
        }
        else {
            # Register new assembly using Web API
            Write-PluginLog "Registering new assembly: $AssemblyName"

            if ($WhatIf) {
                Write-PluginLog "[WhatIf] Would register new assembly via Web API"
                return $null
            }

            # Read assembly bytes and convert to base64
            $bytes = [System.IO.File]::ReadAllBytes($Path)
            $content = [System.Convert]::ToBase64String($bytes)

            # Get assembly metadata via reflection
            try {
                $assembly = [System.Reflection.Assembly]::LoadFrom($Path)
                $assemblyName = $assembly.GetName()
                $version = $assemblyName.Version.ToString()
                $culture = if ($assemblyName.CultureInfo.Name) { $assemblyName.CultureInfo.Name } else { "neutral" }
                $publicKeyToken = [System.BitConverter]::ToString($assemblyName.GetPublicKeyToken()).Replace("-", "").ToLower()
                if (-not $publicKeyToken) { $publicKeyToken = "null" }
            }
            catch {
                Write-PluginWarning "Could not read assembly metadata: $($_.Exception.Message)"
                $version = "1.0.0.0"
                $culture = "neutral"
                $publicKeyToken = "null"
            }

            $body = @{
                name = $AssemblyName
                content = $content
                isolationmode = 2  # Sandbox
                sourcetype = 0     # Database
                version = $version
                culture = $culture
                publickeytoken = $publicKeyToken
            }

            try {
                $response = Invoke-DataverseApi -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Endpoint "pluginassemblies" -Method POST -Body $body
                Write-PluginSuccess "Assembly registered successfully"

                # Return the newly created assembly
                return Get-PluginAssembly -ApiUrl $ApiUrl -AuthHeaders $AuthHeaders -Name $AssemblyName
            }
            catch {
                Write-PluginError "Failed to register assembly: $($_.Exception.Message)"
                return $null
            }
        }
    }
}

# =============================================================================
# Export Module Members
# =============================================================================

Export-ModuleMember -Function @(
    'Write-PluginLog'
    'Write-PluginSuccess'
    'Write-PluginWarning'
    'Write-PluginError'
    'Write-PluginDebug'
    'Get-PluginProjects'
    'Get-PluginRegistrations'
    'ConvertTo-RegistrationJson'
    'Read-RegistrationJson'
    'Get-StepUniqueName'
    'Get-DataverseApiUrl'
    'Get-DataverseAuthToken'
    'Invoke-DataverseApi'
    'Get-PluginAssembly'
    'Get-PluginPackage'
    'Get-PluginType'
    'Get-SdkMessage'
    'Get-SdkMessageFilter'
    'Get-ProcessingStep'
    'Get-ProcessingStepsForAssembly'
    'Get-StepImages'
    'Get-Solution'
    'Add-SolutionComponent'
    'New-ProcessingStep'
    'Update-ProcessingStep'
    'Remove-ProcessingStep'
    'New-StepImage'
    'Update-StepImage'
    'Remove-StepImage'
    'Deploy-PluginAssembly'
) -Variable @(
    'PluginStageMap'
    'PluginModeMap'
    'PluginImageTypeMap'
    'DataverseStageValues'
    'DataverseModeValues'
    'DataverseImageTypeValues'
    'ComponentType'
)
