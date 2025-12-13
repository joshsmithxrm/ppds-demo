# Tools Reference

PowerShell script standards for the PPDSDemo solution tools.

---

## Authentication Hierarchy

Scripts authenticate using a **waterfall approach** - the first available method wins:

```
1. Explicit Parameters     → -ConnectionString or -ClientId/-ClientSecret/-TenantId
2. Environment Variables   → .env file or system environment
3. Interactive OAuth       → Browser popup (requires -Interactive flag)
```

| Priority | Method | Use Case | Requires |
|----------|--------|----------|----------|
| 1 | Parameters | CI/CD override, explicit control | Command-line args |
| 2 | Environment | Local dev, containers, pipelines | `.env.{environment}` file |
| 3 | Interactive | Quick testing, new developers | `-Interactive` flag + browser |

---

## Standard Parameters

All tools scripts accept these common parameters:

```powershell
param(
    # Connection string (highest priority)
    [string]$ConnectionString,

    # Service Principal credentials
    [string]$TenantId,
    [string]$ClientId,
    [string]$ClientSecret,

    # Environment URL (required if not in connection string)
    [string]$EnvironmentUrl,

    # Environment file to load (default: .env.dev)
    [string]$EnvFile,

    # Enable interactive OAuth fallback
    [switch]$Interactive
)
```

### Usage Examples

```powershell
# Using environment file (recommended for local dev)
.\tools\Deploy-Components.ps1 -EnvFile ".env.dev"

# Using service principal explicitly
.\tools\Deploy-Components.ps1 `
    -EnvironmentUrl "https://org.crm.dynamics.com" `
    -TenantId "xxx" -ClientId "xxx" -ClientSecret "xxx"

# Interactive fallback for quick testing
.\tools\Deploy-Components.ps1 -EnvironmentUrl "https://org.crm.dynamics.com" -Interactive

# Full connection string
.\tools\Deploy-Components.ps1 -ConnectionString "AuthType=ClientSecret;Url=https://org.crm.dynamics.com;ClientId=xxx;ClientSecret=xxx"
```

---

## Environment Variables

Scripts read from `.env.{environment}` files. Variable names match `.env.example`:

| Variable | Description | Required |
|----------|-------------|----------|
| `DATAVERSE_URL` | Environment URL (e.g., `https://org.crm.dynamics.com`) | Yes |
| `SP_TENANT_ID` | Azure AD Tenant ID | For SPN auth |
| `SP_APPLICATION_ID` | App Registration Client ID | For SPN auth |
| `SP_CLIENT_SECRET` | App Registration Secret | For SPN auth |

### .env File Selection

```powershell
# Explicit file
.\tools\Script.ps1 -EnvFile ".env.qa"

# Default behavior (no -EnvFile specified):
# 1. Check for .env.dev
# 2. Check for .env
# 3. Proceed without (will need -Interactive or explicit params)
```

### .env File Format

```ini
# .env.dev
DATAVERSE_URL=https://orgcabef92d.crm.dynamics.com
SP_TENANT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
SP_APPLICATION_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
SP_CLIENT_SECRET=your-secret-here
```

---

## Common-Auth Module

All scripts import the shared authentication module:

```powershell
# At top of script
. "$PSScriptRoot\Common-Auth.ps1"

# Get connection
$conn = Get-DataverseConnection -EnvFile $EnvFile -Interactive:$Interactive
```

### Functions Provided

| Function | Description |
|----------|-------------|
| `Import-EnvFile` | Loads `.env` file into environment variables |
| `Get-DataverseConnection` | Returns authenticated CrmServiceClient |
| `Get-AuthHeaders` | Returns headers for Web API calls |
| `Write-Log` | Consistent logging with timestamps |

---

## Security Practices

### Do

- Use `.env.{environment}` files for credentials (gitignored)
- Use service principals for automation
- Redact secrets in log output
- Validate connection before performing work

### Don't

- Hardcode credentials in scripts
- Commit `.env` files with secrets
- Log connection strings or tokens
- Use `-Interactive` in CI/CD pipelines

### Secret Redaction

```powershell
# Automatic in Common-Auth.ps1
$sanitized = $connString -replace "(ClientSecret=)[^;]+", '$1***REDACTED***'
Write-Log "Connecting with: $sanitized"
```

---

## Script Template

New tools should follow this structure:

```powershell
<#
.SYNOPSIS
    Brief description.
.DESCRIPTION
    Detailed description.
.EXAMPLE
    .\tools\My-Script.ps1 -EnvFile ".env.dev"
#>

param(
    [string]$ConnectionString,
    [string]$TenantId,
    [string]$ClientId,
    [string]$ClientSecret,
    [string]$EnvironmentUrl,
    [string]$EnvFile,
    [switch]$Interactive,
    # Script-specific params below
    [string]$SolutionName = "PPDSDemo"
)

$ErrorActionPreference = "Stop"

# Import shared auth
. "$PSScriptRoot\Common-Auth.ps1"

# Connect
$conn = Get-DataverseConnection `
    -ConnectionString $ConnectionString `
    -TenantId $TenantId `
    -ClientId $ClientId `
    -ClientSecret $ClientSecret `
    -EnvironmentUrl $EnvironmentUrl `
    -EnvFile $EnvFile `
    -Interactive:$Interactive

# Script logic here
# ...
```

---

## Available Tools

| Script | Purpose |
|--------|---------|
| `Common-Auth.ps1` | Shared authentication module (dot-source, don't run directly) |
| `Deploy-Components.ps1` | Deploy plugins and web resources |
| `Create-SchemaComponents.ps1` | Create tables, option sets, environment variables |
| `Add-MissingSolutionComponents.ps1` | Add existing components to solution |
| `Generate-Snk.ps1` | Generate strong name key file for assembly signing |
| `Setup-BranchProtection.ps1` | Configure GitHub branch protection rules via gh CLI |

---

## See Also

- [ENVIRONMENT_SETUP_GUIDE.md](../guides/ENVIRONMENT_SETUP_GUIDE.md) - Setting up environments and service principals
- [PAC_CLI_REFERENCE.md](PAC_CLI_REFERENCE.md) - PAC CLI commands
- [.env.example](../../.env.example) - Environment variable template
