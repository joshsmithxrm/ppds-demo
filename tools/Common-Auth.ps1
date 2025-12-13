<#
.SYNOPSIS
    Shared authentication module for PPDSDemo tools.

.DESCRIPTION
    Provides common authentication functions for all PowerShell scripts.
    Implements the authentication hierarchy: Parameters → Environment → Interactive

.NOTES
    This module is dot-sourced by other scripts, not run directly.
    Usage: . "$PSScriptRoot\Common-Auth.ps1"
#>

# =============================================================================
# Logging Functions
# =============================================================================

function Write-Log {
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

function Write-Success { param([string]$Message) Write-Log $Message "SUCCESS" }
function Write-Warning-Log { param([string]$Message) Write-Log $Message "WARNING" }
function Write-Error-Log { param([string]$Message) Write-Log $Message "ERROR" }
function Write-Debug-Log { param([string]$Message) Write-Log $Message "DEBUG" }

# =============================================================================
# Environment File Functions
# =============================================================================

function Import-EnvFile {
    <#
    .SYNOPSIS
        Loads environment variables from a .env file.
    .PARAMETER Path
        Path to the .env file. If not specified, tries .env.dev then .env
    .OUTPUTS
        Returns $true if file was loaded, $false otherwise.
    #>
    param(
        [string]$Path
    )

    # Determine which file to load
    $filesToTry = @()
    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        $filesToTry += $Path
    }
    else {
        # Default search order
        $scriptRoot = if ($PSScriptRoot) { Split-Path $PSScriptRoot -Parent } else { Get-Location }
        $filesToTry += Join-Path $scriptRoot ".env.dev"
        $filesToTry += Join-Path $scriptRoot ".env"
    }

    foreach ($file in $filesToTry) {
        if (Test-Path $file) {
            Write-Log "Loading environment from: $file"

            $content = Get-Content $file -ErrorAction SilentlyContinue
            foreach ($line in $content) {
                # Skip comments and empty lines
                if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) {
                    continue
                }

                # Parse KEY=VALUE
                $parts = $line -split "=", 2
                if ($parts.Count -eq 2) {
                    $key = $parts[0].Trim()
                    $value = $parts[1].Trim()

                    # Remove surrounding quotes if present
                    if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
                        ($value.StartsWith("'") -and $value.EndsWith("'"))) {
                        $value = $value.Substring(1, $value.Length - 2)
                    }

                    # Set environment variable
                    [Environment]::SetEnvironmentVariable($key, $value, "Process")
                }
            }

            Write-Success "Environment loaded from: $file"
            return $true
        }
    }

    Write-Debug-Log "No .env file found"
    return $false
}

function Get-EnvVar {
    <#
    .SYNOPSIS
        Gets an environment variable with optional fallback.
    #>
    param(
        [string]$Name,
        [string]$Default = $null
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }
    return $value
}

# =============================================================================
# Connection Functions
# =============================================================================

function Get-DataverseConnection {
    <#
    .SYNOPSIS
        Gets an authenticated connection to Dataverse.
    .DESCRIPTION
        Implements the authentication hierarchy:
        1. Explicit parameters (ConnectionString or SPN credentials)
        2. Environment variables (from .env file)
        3. Interactive OAuth (if -Interactive specified)
    .OUTPUTS
        Microsoft.Xrm.Tooling.Connector.CrmServiceClient
    #>
    param(
        [string]$ConnectionString,
        [string]$TenantId,
        [string]$ClientId,
        [string]$ClientSecret,
        [string]$EnvironmentUrl,
        [string]$EnvFile,
        [switch]$Interactive
    )

    # Ensure module is available
    if (-not (Get-Module -ListAvailable -Name Microsoft.Xrm.Data.PowerShell)) {
        Write-Error-Log "Microsoft.Xrm.Data.PowerShell module not found."
        Write-Log "Install with: Install-Module Microsoft.Xrm.Data.PowerShell -Scope CurrentUser"
        throw "Required module not installed"
    }
    Import-Module Microsoft.Xrm.Data.PowerShell -ErrorAction Stop

    # Load environment file if specified or by default
    if (-not [string]::IsNullOrWhiteSpace($EnvFile)) {
        Import-EnvFile -Path $EnvFile | Out-Null
    }
    elseif ([string]::IsNullOrWhiteSpace($ConnectionString) -and
            [string]::IsNullOrWhiteSpace($ClientId)) {
        # Try to load default .env if no explicit credentials provided
        Import-EnvFile | Out-Null
    }

    # Build connection string using hierarchy
    $finalConnectionString = $null
    $authMethod = $null

    # Priority 1: Explicit connection string
    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
        $finalConnectionString = $ConnectionString
        $authMethod = "Explicit Connection String"
    }
    # Priority 2: Explicit SPN parameters
    elseif (-not [string]::IsNullOrWhiteSpace($ClientId) -and
            -not [string]::IsNullOrWhiteSpace($ClientSecret) -and
            -not [string]::IsNullOrWhiteSpace($TenantId)) {

        $url = if (-not [string]::IsNullOrWhiteSpace($EnvironmentUrl)) {
            $EnvironmentUrl
        } else {
            Get-EnvVar "DATAVERSE_URL"
        }

        if ([string]::IsNullOrWhiteSpace($url)) {
            throw "EnvironmentUrl required when using service principal parameters"
        }

        $finalConnectionString = "AuthType=ClientSecret;Url=$url;ClientId=$ClientId;ClientSecret=$ClientSecret"
        $authMethod = "Service Principal (Parameters)"
    }
    # Priority 3: Environment variables for SPN
    else {
        $envUrl = Get-EnvVar "DATAVERSE_URL"
        $envClientId = Get-EnvVar "SP_APPLICATION_ID"
        $envClientSecret = Get-EnvVar "SP_CLIENT_SECRET"
        $envTenantId = Get-EnvVar "SP_TENANT_ID"

        # Use explicit URL if provided, otherwise env var
        $url = if (-not [string]::IsNullOrWhiteSpace($EnvironmentUrl)) {
            $EnvironmentUrl
        } else {
            $envUrl
        }

        if (-not [string]::IsNullOrWhiteSpace($envClientId) -and
            -not [string]::IsNullOrWhiteSpace($envClientSecret) -and
            -not [string]::IsNullOrWhiteSpace($url)) {

            $finalConnectionString = "AuthType=ClientSecret;Url=$url;ClientId=$envClientId;ClientSecret=$envClientSecret"
            $authMethod = "Service Principal (Environment)"
        }
        # Priority 4: Interactive OAuth (if enabled)
        elseif ($Interactive) {
            if ([string]::IsNullOrWhiteSpace($url)) {
                throw "EnvironmentUrl required for interactive authentication"
            }

            # Well-known Power Platform AppId for interactive auth
            $finalConnectionString = "AuthType=OAuth;Url=$url;AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=http://localhost;LoginPrompt=Auto"
            $authMethod = "Interactive OAuth"
        }
        else {
            Write-Error-Log "No authentication method available."
            Write-Log "Options:"
            Write-Log "  1. Provide -ConnectionString parameter"
            Write-Log "  2. Provide -ClientId, -ClientSecret, -TenantId, -EnvironmentUrl parameters"
            Write-Log "  3. Create .env.dev file with SP_APPLICATION_ID, SP_CLIENT_SECRET, DATAVERSE_URL"
            Write-Log "  4. Use -Interactive flag for browser-based login"
            throw "No authentication credentials available"
        }
    }

    # Log connection attempt (redacted)
    $sanitized = $finalConnectionString -replace "(ClientSecret=)[^;]+", '$1***REDACTED***'
    Write-Log "Auth method: $authMethod"
    Write-Debug-Log "Connection: $sanitized"

    # Connect
    try {
        Write-Log "Connecting to Dataverse..."
        $conn = Get-CrmConnection -ConnectionString $finalConnectionString

        if (-not $conn -or -not $conn.IsReady) {
            throw "Connection failed - IsReady is false"
        }

        Write-Success "Connected to: $($conn.ConnectedOrgFriendlyName)"
        return $conn
    }
    catch {
        Write-Error-Log "Connection failed: $($_.Exception.Message)"
        throw
    }
}

function Get-AuthHeaders {
    <#
    .SYNOPSIS
        Gets HTTP headers for Dataverse Web API calls.
    .PARAMETER Connection
        CrmServiceClient connection object.
    .PARAMETER SolutionName
        Optional solution name to include in headers.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection,
        [string]$SolutionName
    )

    $headers = @{
        "Authorization"    = "Bearer $($Connection.CurrentAccessToken)"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
    }

    if (-not [string]::IsNullOrWhiteSpace($SolutionName)) {
        $headers["MSCRM.SolutionName"] = $SolutionName
    }

    return $headers
}

function Get-WebApiBaseUrl {
    <#
    .SYNOPSIS
        Gets the Web API base URL from a connection.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [Microsoft.Xrm.Tooling.Connector.CrmServiceClient]$Connection
    )

    $webAppUrl = $Connection.ConnectedOrgPublishedEndpoints["WebApplication"].TrimEnd("/")
    return "$webAppUrl/api/data/v9.2"
}

# =============================================================================
# Export functions (for module use)
# =============================================================================

# When dot-sourced, all functions are available
# This section is for potential future module conversion

Export-ModuleMember -Function @(
    'Write-Log',
    'Write-Success',
    'Write-Warning-Log',
    'Write-Error-Log',
    'Write-Debug-Log',
    'Import-EnvFile',
    'Get-EnvVar',
    'Get-DataverseConnection',
    'Get-AuthHeaders',
    'Get-WebApiBaseUrl'
) -ErrorAction SilentlyContinue
