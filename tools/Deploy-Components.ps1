<#
.SYNOPSIS
    Deploys web resources and plugins to Dataverse.

.DESCRIPTION
    This script uses the Microsoft.Xrm.Data.PowerShell module to:
    1. Create/update web resources
    2. Register plugin assemblies
    3. Register plugin steps
    4. Add components to the PPDSDemo solution

.PREREQUISITES
    - Install PowerShell module: Install-Module Microsoft.Xrm.Data.PowerShell -Scope CurrentUser
    - Plugin assembly built: dotnet build src/Plugins/PPDSDemo.Plugins -c Release

.EXAMPLE
    # Using environment file (recommended)
    .\tools\Deploy-Components.ps1 -EnvFile ".env.dev"

.EXAMPLE
    # Interactive auth for quick testing
    .\tools\Deploy-Components.ps1 -EnvironmentUrl "https://org.crm.dynamics.com" -Interactive

.EXAMPLE
    # Service principal auth
    .\tools\Deploy-Components.ps1 -TenantId "xxx" -ClientId "xxx" -ClientSecret "xxx" -EnvironmentUrl "https://org.crm.dynamics.com"

.EXAMPLE
    # Direct connection string
    .\tools\Deploy-Components.ps1 -ConnectionString "AuthType=ClientSecret;Url=https://org.crm.dynamics.com;ClientId=xxx;ClientSecret=xxx"
#>

param(
    [string]$ConnectionString,
    [string]$TenantId,
    [string]$ClientId,
    [string]$ClientSecret,
    [string]$EnvironmentUrl,
    [string]$EnvFile,
    [switch]$Interactive,

    [string]$SolutionName = "PPDSDemo",
    [switch]$SkipPlugins,
    [switch]$SkipWebResources
)

$ErrorActionPreference = "Stop"
$ScriptRoot = $PSScriptRoot

# Import shared authentication module
. "$ScriptRoot\Common-Auth.ps1"

# =============================================================================
# Web Resource Deployment
# =============================================================================

function Deploy-WebResource {
    param(
        [Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection,
        [string]$Name,
        [string]$DisplayName,
        [string]$FilePath,
        [int]$WebResourceType
    )

    Write-Log "Deploying web resource: $Name"

    if (-not (Test-Path $FilePath)) {
        Write-Warning-Log "File not found: $FilePath"
        return $null
    }

    # Read file content and encode as base64
    $content = Get-Content -Path $FilePath -Raw -Encoding UTF8
    $base64Content = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($content))

    try {
        # Check if web resource already exists
        $existing = Get-CrmRecords -conn $Connection `
            -EntityLogicalName "webresource" `
            -FilterAttribute "name" `
            -FilterOperator "eq" `
            -FilterValue $Name `
            -Fields @("webresourceid", "name")

        if ($existing.CrmRecords.Count -gt 0) {
            # Update existing
            $webResourceId = $existing.CrmRecords[0].webresourceid
            Write-Log "  Updating existing web resource: $webResourceId"

            Set-CrmRecord -conn $Connection `
                -EntityLogicalName "webresource" `
                -Id $webResourceId `
                -Fields @{ "content" = $base64Content }

            Write-Success "  Updated successfully"
            return $webResourceId
        }
        else {
            # Create new
            Write-Log "  Creating new web resource..."

            $webResourceId = New-CrmRecord -conn $Connection `
                -EntityLogicalName "webresource" `
                -Fields @{
                    "name" = $Name
                    "displayname" = $DisplayName
                    "webresourcetype" = (New-CrmOptionSetValue -Value $WebResourceType)
                    "content" = $base64Content
                }

            Write-Success "  Created: $webResourceId"
            return $webResourceId
        }
    }
    catch {
        Write-Error-Log "Failed to deploy web resource: $($_.Exception.Message)"
        throw
    }
}

# =============================================================================
# Plugin Deployment
# =============================================================================

function Deploy-PluginAssembly {
    param(
        [Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection,
        [string]$AssemblyPath,
        [string]$AssemblyName
    )

    Write-Log "Deploying plugin assembly: $AssemblyName"

    if (-not (Test-Path $AssemblyPath)) {
        Write-Error-Log "Assembly not found: $AssemblyPath"
        Write-Log "Run: dotnet build src/Plugins/PPDSDemo.Plugins -c Release"
        return $null
    }

    # Read assembly and encode as base64
    $assemblyBytes = [System.IO.File]::ReadAllBytes($AssemblyPath)
    $base64Assembly = [Convert]::ToBase64String($assemblyBytes)

    try {
        # Check if assembly already exists
        $existing = Get-CrmRecords -conn $Connection `
            -EntityLogicalName "pluginassembly" `
            -FilterAttribute "name" `
            -FilterOperator "eq" `
            -FilterValue $AssemblyName `
            -Fields @("pluginassemblyid", "name")

        if ($existing.CrmRecords.Count -gt 0) {
            # Update existing
            $assemblyId = $existing.CrmRecords[0].pluginassemblyid
            Write-Log "  Updating existing assembly: $assemblyId"

            Set-CrmRecord -conn $Connection `
                -EntityLogicalName "pluginassembly" `
                -Id $assemblyId `
                -Fields @{ "content" = $base64Assembly }

            Write-Success "  Updated successfully"
            return $assemblyId
        }
        else {
            # Create new
            Write-Log "  Creating new assembly..."

            $assemblyId = New-CrmRecord -conn $Connection `
                -EntityLogicalName "pluginassembly" `
                -Fields @{
                    "name" = $AssemblyName
                    "content" = $base64Assembly
                    "isolationmode" = (New-CrmOptionSetValue -Value 2)  # Sandbox
                    "sourcetype" = (New-CrmOptionSetValue -Value 0)     # Database
                }

            Write-Success "  Created: $assemblyId"
            return $assemblyId
        }
    }
    catch {
        Write-Error-Log "Failed to deploy assembly: $($_.Exception.Message)"
        throw
    }
}

function Register-PluginType {
    param(
        [Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection,
        [guid]$AssemblyId,
        [string]$TypeName,
        [string]$FriendlyName
    )

    Write-Log "  Registering plugin type: $TypeName"

    try {
        # Check if type already exists
        $existing = Get-CrmRecords -conn $Connection `
            -EntityLogicalName "plugintype" `
            -FilterAttribute "typename" `
            -FilterOperator "eq" `
            -FilterValue $TypeName `
            -Fields @("plugintypeid", "typename")

        if ($existing.CrmRecords.Count -gt 0) {
            Write-Log "    Already registered: $($existing.CrmRecords[0].plugintypeid)"
            return $existing.CrmRecords[0].plugintypeid
        }

        # Create new plugin type
        $pluginTypeId = New-CrmRecord -conn $Connection `
            -EntityLogicalName "plugintype" `
            -Fields @{
                "typename" = $TypeName
                "friendlyname" = $FriendlyName
                "name" = $FriendlyName
                "pluginassemblyid" = (New-CrmEntityReference -EntityLogicalName "pluginassembly" -Id $AssemblyId)
            }

        Write-Success "    Registered: $pluginTypeId"
        return $pluginTypeId
    }
    catch {
        Write-Error-Log "Failed to register plugin type: $($_.Exception.Message)"
        throw
    }
}

function Register-PluginStep {
    param(
        [Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection,
        [guid]$PluginTypeId,
        [string]$MessageName,
        [string]$EntityName,
        [int]$Stage,
        [string]$StepName
    )

    Write-Log "  Registering step: $StepName"

    try {
        # Check if step already exists
        $existing = Get-CrmRecords -conn $Connection `
            -EntityLogicalName "sdkmessageprocessingstep" `
            -FilterAttribute "name" `
            -FilterOperator "eq" `
            -FilterValue $StepName `
            -Fields @("sdkmessageprocessingstepid", "name")

        if ($existing.CrmRecords.Count -gt 0) {
            Write-Log "    Already registered: $($existing.CrmRecords[0].sdkmessageprocessingstepid)"
            return $existing.CrmRecords[0].sdkmessageprocessingstepid
        }

        # Get SDK Message ID
        $message = Get-CrmRecords -conn $Connection `
            -EntityLogicalName "sdkmessage" `
            -FilterAttribute "name" `
            -FilterOperator "eq" `
            -FilterValue $MessageName `
            -Fields @("sdkmessageid")

        if ($message.CrmRecords.Count -eq 0) {
            throw "SDK Message not found: $MessageName"
        }
        $messageId = $message.CrmRecords[0].sdkmessageid

        # Get SDK Message Filter ID by dynamically looking up the entity's ObjectTypeCode
        $filterId = $null
        $typeCode = $null

        # Query EntityDefinitions to get the ObjectTypeCode for the entity
        try {
            $entityDefFetch = @"
<fetch top='1'>
  <entity name='entity'>
    <attribute name='objecttypecode' />
    <filter>
      <condition attribute='logicalname' operator='eq' value='$EntityName' />
    </filter>
  </entity>
</fetch>
"@
            $entityDefResult = Get-CrmRecordsByFetch -conn $Connection -Fetch $entityDefFetch
            if ($entityDefResult.CrmRecords.Count -gt 0) {
                $typeCode = $entityDefResult.CrmRecords[0].objecttypecode
            }
        }
        catch {
            Write-Log "    Could not look up entity type code for '$EntityName'"
        }

        if ($typeCode) {
            $filterFetch = @"
<fetch top='1'>
  <entity name='sdkmessagefilter'>
    <attribute name='sdkmessagefilterid' />
    <filter>
      <condition attribute='sdkmessageid' operator='eq' value='$messageId' />
      <condition attribute='primaryobjecttypecode' operator='eq' value='$typeCode' />
    </filter>
  </entity>
</fetch>
"@

            try {
                $filterResult = Get-CrmRecordsByFetch -conn $Connection -Fetch $filterFetch
                if ($filterResult.CrmRecords.Count -gt 0) {
                    $filterId = $filterResult.CrmRecords[0].sdkmessagefilterid
                }
            }
            catch {
                Write-Log "    Could not find message filter, will register without filter"
            }
        }
        else {
            Write-Log "    Could not determine entity type code for '$EntityName', registering without filter"
        }

        # Create step
        $stepFields = @{
            "name" = $StepName
            "mode" = (New-CrmOptionSetValue -Value 0)  # Synchronous
            "rank" = 1
            "stage" = (New-CrmOptionSetValue -Value $Stage)
            "supporteddeployment" = (New-CrmOptionSetValue -Value 0)  # Server only
            "sdkmessageid" = (New-CrmEntityReference -EntityLogicalName "sdkmessage" -Id $messageId)
            "plugintypeid" = (New-CrmEntityReference -EntityLogicalName "plugintype" -Id $PluginTypeId)
        }

        if ($filterId) {
            $stepFields["sdkmessagefilterid"] = (New-CrmEntityReference -EntityLogicalName "sdkmessagefilter" -Id $filterId)
        }

        $stepId = New-CrmRecord -conn $Connection `
            -EntityLogicalName "sdkmessageprocessingstep" `
            -Fields $stepFields

        Write-Success "    Registered: $stepId"
        return $stepId
    }
    catch {
        Write-Error-Log "Failed to register step: $($_.Exception.Message)"
        throw
    }
}

# =============================================================================
# Solution Component Addition
# =============================================================================

function Add-ToSolution {
    param(
        [Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection,
        [guid]$ComponentId,
        [int]$ComponentType,
        [string]$SolutionName
    )

    Write-Log "  Adding to solution: $SolutionName (Type: $ComponentType)"

    try {
        $parameters = @{
            "ComponentId" = $ComponentId
            "ComponentType" = $ComponentType
            "SolutionUniqueName" = $SolutionName
            "AddRequiredComponents" = $false
        }

        Invoke-CrmAction -conn $Connection `
            -Name "AddSolutionComponent" `
            -Parameters $parameters | Out-Null

        Write-Success "    Added to solution"
    }
    catch {
        if ($_.Exception.Message -match "already exists") {
            Write-Log "    Already in solution"
        }
        else {
            Write-Warning-Log "Failed to add to solution: $($_.Exception.Message)"
        }
    }
}

# =============================================================================
# Main Execution
# =============================================================================

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Power Platform Component Deployment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Component Type IDs for AddSolutionComponent
$ComponentTypes = @{
    WebResource = 61
    PluginAssembly = 91
    SDKMessageProcessingStep = 92
}

# Get connection using Common-Auth
$conn = Get-DataverseConnection `
    -ConnectionString $ConnectionString `
    -TenantId $TenantId `
    -ClientId $ClientId `
    -ClientSecret $ClientSecret `
    -EnvironmentUrl $EnvironmentUrl `
    -EnvFile $EnvFile `
    -Interactive:$Interactive

# -----------------------------------------------------------------------------
# Deploy Web Resources
# -----------------------------------------------------------------------------
if (-not $SkipWebResources) {
    Write-Host ""
    Write-Host "--- Web Resources ---" -ForegroundColor Magenta
    Write-Host ""

    $webResourcePath = Join-Path $ScriptRoot "..\src\WebResources\ppds_\scripts\account.form.js"

    if (Test-Path $webResourcePath) {
        $webResourceId = Deploy-WebResource `
            -Connection $conn `
            -Name "ppds_/scripts/account.form.js" `
            -DisplayName "Account Form Script" `
            -FilePath $webResourcePath `
            -WebResourceType 3  # JavaScript

        if ($webResourceId) {
            Add-ToSolution -Connection $conn -ComponentId $webResourceId -ComponentType $ComponentTypes.WebResource -SolutionName $SolutionName
        }
    }
    else {
        Write-Warning-Log "Web resource file not found: $webResourcePath"
    }
}

# -----------------------------------------------------------------------------
# Deploy Plugins
# -----------------------------------------------------------------------------
if (-not $SkipPlugins) {
    Write-Host ""
    Write-Host "--- Plugin Assembly ---" -ForegroundColor Magenta
    Write-Host ""

    $assemblyPath = Join-Path $ScriptRoot "..\src\Plugins\PPDSDemo.Plugins\bin\Release\net462\PPDSDemo.Plugins.dll"

    if (Test-Path $assemblyPath) {
        $assemblyId = Deploy-PluginAssembly `
            -Connection $conn `
            -AssemblyPath $assemblyPath `
            -AssemblyName "PPDSDemo.Plugins"

        if ($assemblyId) {
            Add-ToSolution -Connection $conn -ComponentId $assemblyId -ComponentType $ComponentTypes.PluginAssembly -SolutionName $SolutionName

            Write-Host ""
            Write-Host "--- Plugin Types & Steps ---" -ForegroundColor Magenta
            Write-Host ""

            # AccountPreCreatePlugin
            $accountPluginId = Register-PluginType `
                -Connection $conn `
                -AssemblyId $assemblyId `
                -TypeName "PPDSDemo.Plugins.Plugins.AccountPreCreatePlugin" `
                -FriendlyName "Account Pre-Create Validation"

            if ($accountPluginId) {
                $stepId = Register-PluginStep `
                    -Connection $conn `
                    -PluginTypeId $accountPluginId `
                    -MessageName "Create" `
                    -EntityName "account" `
                    -Stage 20 `
                    -StepName "PPDSDemo.Plugins: Account Pre-Create Validation"

                if ($stepId) {
                    Add-ToSolution -Connection $conn -ComponentId $stepId -ComponentType $ComponentTypes.SDKMessageProcessingStep -SolutionName $SolutionName
                }
            }

            # ContactPostUpdatePlugin
            $contactPluginId = Register-PluginType `
                -Connection $conn `
                -AssemblyId $assemblyId `
                -TypeName "PPDSDemo.Plugins.Plugins.ContactPostUpdatePlugin" `
                -FriendlyName "Contact Post-Update Handler"

            if ($contactPluginId) {
                $stepId = Register-PluginStep `
                    -Connection $conn `
                    -PluginTypeId $contactPluginId `
                    -MessageName "Update" `
                    -EntityName "contact" `
                    -Stage 40 `
                    -StepName "PPDSDemo.Plugins: Contact Post-Update Handler"

                if ($stepId) {
                    Add-ToSolution -Connection $conn -ComponentId $stepId -ComponentType $ComponentTypes.SDKMessageProcessingStep -SolutionName $SolutionName
                }
            }
        }
    }
    else {
        Write-Warning-Log "Plugin assembly not found: $assemblyPath"
        Write-Log "Run: dotnet build src/Plugins/PPDSDemo.Plugins -c Release"
    }
}

# -----------------------------------------------------------------------------
# Publish Customizations
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "--- Publishing ---" -ForegroundColor Magenta
Write-Host ""

Write-Log "Publishing all customizations..."
try {
    Publish-CrmAllCustomization -conn $conn
    Write-Success "Published successfully"
}
catch {
    Write-Warning-Log "Publish failed: $($_.Exception.Message)"
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Deployment Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
