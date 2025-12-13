<#
.SYNOPSIS
    Creates schema components (tables, option sets, environment variables) in Dataverse.

.DESCRIPTION
    This script uses the Dataverse Web API to create:
    1. Global Option Set: ppds_Status
    2. Custom Table: ppds_DemoRecord with fields
    3. Environment Variables: ppds_ApiEndpoint, ppds_EnableFeatureX

    All components are added to the PPDSDemo solution.

.PREREQUISITES
    - Install PowerShell module: Install-Module Microsoft.Xrm.Data.PowerShell -Scope CurrentUser

.EXAMPLE
    # Using environment file (recommended)
    .\tools\Create-SchemaComponents.ps1 -EnvFile ".env.dev"

.EXAMPLE
    # Interactive auth for quick testing
    .\tools\Create-SchemaComponents.ps1 -EnvironmentUrl "https://org.crm.dynamics.com" -Interactive

.EXAMPLE
    # Service principal auth
    .\tools\Create-SchemaComponents.ps1 -TenantId "xxx" -ClientId "xxx" -ClientSecret "xxx" -EnvironmentUrl "https://org.crm.dynamics.com"

.EXAMPLE
    # Direct connection string
    .\tools\Create-SchemaComponents.ps1 -ConnectionString "AuthType=ClientSecret;Url=https://org.crm.dynamics.com;ClientId=xxx;ClientSecret=xxx"
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
    [switch]$SkipOptionSet,
    [switch]$SkipTable,
    [switch]$SkipEnvironmentVariables
)

$ErrorActionPreference = "Stop"
$ScriptRoot = $PSScriptRoot

# Import shared authentication module
. "$ScriptRoot\Common-Auth.ps1"

# =============================================================================
# Configuration
# =============================================================================

$Config = @{
    PublisherPrefix = "ppds"
    OptionValuePrefix = 37411
    LanguageCode = 1033
}

# =============================================================================
# Web API Helper Functions
# =============================================================================

function Invoke-DataverseApi {
    param(
        [Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection,
        [string]$Method,
        [string]$Endpoint,
        [object]$Body = $null,
        [hashtable]$Headers = @{}
    )

    # Get access token from connection
    $token = $Connection.CurrentAccessToken

    $uri = "$script:ApiBaseUrl$Endpoint"

    $defaultHeaders = @{
        "Authorization" = "Bearer $token"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
        "Accept" = "application/json"
        "Content-Type" = "application/json; charset=utf-8"
        "MSCRM.SolutionName" = $SolutionName
    }

    # Merge custom headers
    foreach ($key in $Headers.Keys) {
        $defaultHeaders[$key] = $Headers[$key]
    }

    $params = @{
        Uri = $uri
        Method = $Method
        Headers = $defaultHeaders
        ContentType = "application/json; charset=utf-8"
    }

    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
        Write-Debug-Log "Request body: $($params.Body)"
    }

    try {
        Write-Debug-Log "$Method $uri"
        $response = Invoke-RestMethod @params
        return $response
    }
    catch {
        $errorMessage = $_.Exception.Message
        if ($_.Exception.Response) {
            try {
                $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
                $errorBody = $reader.ReadToEnd()
                $reader.Close()
                Write-Debug-Log "Error response: $errorBody"
                $errorMessage = "$errorMessage - $errorBody"
            }
            catch { }
        }
        throw "API call failed: $errorMessage"
    }
}

function Get-PublisherId {
    param([Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection)

    Write-Log "Looking up publisher..."

    $result = Invoke-DataverseApi -Connection $Connection -Method "GET" `
        -Endpoint "/publishers?`$filter=uniquename eq 'PPDSDemoPublisher'&`$select=publisherid"

    if ($result.value.Count -eq 0) {
        throw "Publisher 'PPDSDemoPublisher' not found"
    }

    $publisherId = $result.value[0].publisherid
    Write-Success "Publisher found: $publisherId"
    return $publisherId
}

function Test-ComponentExists {
    param(
        [Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection,
        [string]$EntitySet,
        [string]$Filter,
        [string]$Select = "createdon"
    )

    try {
        $result = Invoke-DataverseApi -Connection $Connection -Method "GET" `
            -Endpoint "/$EntitySet`?`$filter=$Filter&`$select=$Select"
        return $result.value.Count -gt 0
    }
    catch {
        return $false
    }
}

# =============================================================================
# Global Option Set Creation
# =============================================================================

function New-GlobalOptionSet {
    param([Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection)

    Write-Host ""
    Write-Host "--- Creating Global Option Set ---" -ForegroundColor Magenta
    Write-Host ""

    $optionSetName = "ppds_status"
    $displayName = "Status"

    # Check if already exists
    if (Test-ComponentExists -Connection $Connection -EntitySet "GlobalOptionSetDefinitions" -Filter "Name eq '$optionSetName'") {
        Write-Warning-Log "Global option set '$optionSetName' already exists, skipping..."
        return
    }

    Write-Log "Creating global option set: $optionSetName"

    $optionSetDefinition = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        Name = $optionSetName
        DisplayName = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            LocalizedLabels = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    Label = $displayName
                    LanguageCode = $Config.LanguageCode
                }
            )
        }
        Description = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            LocalizedLabels = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    Label = "Status option set for demo records"
                    LanguageCode = $Config.LanguageCode
                }
            )
        }
        IsGlobal = $true
        OptionSetType = "Picklist"
        Options = @(
            @{
                Value = 374110000
                Label = @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.Label"
                    LocalizedLabels = @(
                        @{
                            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                            Label = "Draft"
                            LanguageCode = $Config.LanguageCode
                        }
                    )
                }
            },
            @{
                Value = 374110001
                Label = @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.Label"
                    LocalizedLabels = @(
                        @{
                            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                            Label = "Active"
                            LanguageCode = $Config.LanguageCode
                        }
                    )
                }
            },
            @{
                Value = 374110002
                Label = @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.Label"
                    LocalizedLabels = @(
                        @{
                            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                            Label = "Completed"
                            LanguageCode = $Config.LanguageCode
                        }
                    )
                }
            },
            @{
                Value = 374110003
                Label = @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.Label"
                    LocalizedLabels = @(
                        @{
                            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                            Label = "Cancelled"
                            LanguageCode = $Config.LanguageCode
                        }
                    )
                }
            }
        )
    }

    try {
        Invoke-DataverseApi -Connection $Connection -Method "POST" -Endpoint "/GlobalOptionSetDefinitions" -Body $optionSetDefinition
        Write-Success "Global option set '$optionSetName' created successfully"
    }
    catch {
        Write-Error-Log "Failed to create global option set: $($_.Exception.Message)"
        throw
    }
}

# =============================================================================
# Custom Table Creation
# =============================================================================

function New-CustomTable {
    param([Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection)

    Write-Host ""
    Write-Host "--- Creating Custom Table ---" -ForegroundColor Magenta
    Write-Host ""

    $tableName = "ppds_demorecord"
    $displayName = "Demo Record"
    $pluralName = "Demo Records"

    # Check if already exists
    if (Test-ComponentExists -Connection $Connection -EntitySet "EntityDefinitions" -Filter "LogicalName eq '$tableName'") {
        Write-Warning-Log "Table '$tableName' already exists, skipping..."
        return
    }

    Write-Log "Creating custom table: $tableName"

    # Create entity - Dataverse will auto-create the primary name field
    $entityDefinition = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
        SchemaName = "ppds_DemoRecord"
        DisplayName = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            LocalizedLabels = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    Label = $displayName
                    LanguageCode = $Config.LanguageCode
                }
            )
        }
        DisplayCollectionName = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            LocalizedLabels = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    Label = $pluralName
                    LanguageCode = $Config.LanguageCode
                }
            )
        }
        Description = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            LocalizedLabels = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    Label = "Demo table for showcasing the Power Platform Developer Suite extension"
                    LanguageCode = $Config.LanguageCode
                }
            )
        }
        OwnershipType = "UserOwned"
        HasActivities = $false
        HasNotes = $true
    }

    try {
        Invoke-DataverseApi -Connection $Connection -Method "POST" -Endpoint "/EntityDefinitions" -Body $entityDefinition
        Write-Success "Table '$tableName' created successfully"

        # Add additional attributes
        Add-TableAttributes -Connection $Connection -TableName $tableName
    }
    catch {
        Write-Error-Log "Failed to create table: $($_.Exception.Message)"
        throw
    }
}

function Add-TableAttributes {
    param(
        [Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection,
        [string]$TableName
    )

    Write-Log "Adding attributes to table: $TableName"

    # Wait a moment for table to be fully created
    Start-Sleep -Seconds 2

    # Get the entity metadata to find the MetadataId
    $entityResult = Invoke-DataverseApi -Connection $Connection -Method "GET" `
        -Endpoint "/EntityDefinitions?`$filter=LogicalName eq '$TableName'&`$select=MetadataId"

    if ($entityResult.value.Count -eq 0) {
        throw "Could not find table '$TableName'"
    }

    $metadataId = $entityResult.value[0].MetadataId

    # Add Description attribute (memo/multiline text)
    Write-Log "  Adding ppds_description attribute..."
    $descriptionAttr = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        SchemaName = "ppds_Description"
        RequiredLevel = @{
            Value = "None"
            CanBeChanged = $true
        }
        MaxLength = 4000
        Format = "TextArea"
        DisplayName = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            LocalizedLabels = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    Label = "Description"
                    LanguageCode = $Config.LanguageCode
                }
            )
        }
        Description = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            LocalizedLabels = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    Label = "Description of the demo record"
                    LanguageCode = $Config.LanguageCode
                }
            )
        }
    }

    try {
        Invoke-DataverseApi -Connection $Connection -Method "POST" `
            -Endpoint "/EntityDefinitions($metadataId)/Attributes" `
            -Body $descriptionAttr
        Write-Success "    ppds_description created"
    }
    catch {
        if ($_.Exception.Message -match "already exists") {
            Write-Warning-Log "    ppds_description already exists"
        }
        else {
            throw
        }
    }

    # Add Status picklist attribute (linked to global option set)
    Write-Log "  Adding ppds_status attribute..."
    $statusAttr = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        SchemaName = "ppds_Status"
        RequiredLevel = @{
            Value = "None"
            CanBeChanged = $true
        }
        DisplayName = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            LocalizedLabels = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    Label = "Status"
                    LanguageCode = $Config.LanguageCode
                }
            )
        }
        Description = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            LocalizedLabels = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    Label = "Status of the demo record"
                    LanguageCode = $Config.LanguageCode
                }
            )
        }
        GlobalOptionSet = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            Name = "ppds_status"
        }
    }

    try {
        Invoke-DataverseApi -Connection $Connection -Method "POST" `
            -Endpoint "/EntityDefinitions($metadataId)/Attributes" `
            -Body $statusAttr
        Write-Success "    ppds_status created"
    }
    catch {
        if ($_.Exception.Message -match "already exists") {
            Write-Warning-Log "    ppds_status already exists"
        }
        else {
            throw
        }
    }
}

# =============================================================================
# Environment Variable Creation
# =============================================================================

function New-EnvironmentVariables {
    param([Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection)

    Write-Host ""
    Write-Host "--- Creating Environment Variables ---" -ForegroundColor Magenta
    Write-Host ""

    # Create ppds_ApiEndpoint
    New-EnvironmentVariable -Connection $Connection `
        -SchemaName "ppds_ApiEndpoint" `
        -DisplayName "API Endpoint" `
        -Description "External API endpoint URL for demo integrations" `
        -Type "String" `
        -DefaultValue "https://api.example.com"

    # Create ppds_EnableFeatureX
    New-EnvironmentVariable -Connection $Connection `
        -SchemaName "ppds_EnableFeatureX" `
        -DisplayName "Enable Feature X" `
        -Description "Toggle to enable/disable Feature X functionality" `
        -Type "Boolean" `
        -DefaultValue "false"
}

function New-EnvironmentVariable {
    param(
        [Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection,
        [string]$SchemaName,
        [string]$DisplayName,
        [string]$Description,
        [string]$Type,
        [string]$DefaultValue
    )

    # Check if already exists
    if (Test-ComponentExists -Connection $Connection -EntitySet "environmentvariabledefinitions" -Filter "schemaname eq '$SchemaName'") {
        Write-Warning-Log "Environment variable '$SchemaName' already exists, skipping..."
        return
    }

    Write-Log "Creating environment variable: $SchemaName"

    # Map type to Dataverse type code
    $typeCode = switch ($Type) {
        "String" { 100000000 }
        "Number" { 100000001 }
        "Boolean" { 100000002 }
        "JSON" { 100000003 }
        "DataSource" { 100000004 }
        "Secret" { 100000005 }
        default { 100000000 }
    }

    $envVarDefinition = @{
        schemaname = $SchemaName
        displayname = $DisplayName
        description = $Description
        type = $typeCode
        defaultvalue = $DefaultValue
    }

    try {
        Invoke-DataverseApi -Connection $Connection -Method "POST" `
            -Endpoint "/environmentvariabledefinitions" `
            -Body $envVarDefinition

        Write-Success "  Environment variable '$SchemaName' created"
    }
    catch {
        Write-Error-Log "Failed to create environment variable: $($_.Exception.Message)"
        throw
    }
}

# =============================================================================
# Publish Customizations
# =============================================================================

function Publish-Customizations {
    param([Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection)

    Write-Host ""
    Write-Host "--- Publishing Customizations ---" -ForegroundColor Magenta
    Write-Host ""

    Write-Log "Publishing all customizations..."

    try {
        Publish-CrmAllCustomization -conn $Connection
        Write-Success "Customizations published successfully"
    }
    catch {
        Write-Warning-Log "Publish failed (this is sometimes expected): $($_.Exception.Message)"
    }
}

# =============================================================================
# Main Execution
# =============================================================================

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Power Platform Schema Component Creator" -ForegroundColor Cyan
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

# Set API base URL from connection
$script:ApiBaseUrl = Get-WebApiBaseUrl -Connection $conn
Write-Log "API Base URL: $script:ApiBaseUrl"
Write-Log "Solution: $SolutionName"

# Create components
if (-not $SkipOptionSet) {
    New-GlobalOptionSet -Connection $conn
}

if (-not $SkipTable) {
    New-CustomTable -Connection $conn
}

if (-not $SkipEnvironmentVariables) {
    New-EnvironmentVariables -Connection $conn
}

# Publish
Publish-Customizations -Connection $conn

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Schema Components Created Successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Verify components in make.powerapps.com" -ForegroundColor White
Write-Host "  2. Export solution: pac solution export --name PPDSDemo --path solutions/exports/PPDSDemo.zip" -ForegroundColor White
Write-Host "  3. Unpack solution: pac solution unpack --zipfile solutions/exports/PPDSDemo.zip --folder solutions/PPDSDemo/src --allowDelete --allowWrite" -ForegroundColor White
Write-Host ""
