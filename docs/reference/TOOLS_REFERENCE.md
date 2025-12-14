# Tools Reference

PowerShell scripts for the PPDSDemo solution. These scripts wrap the **PPDS.Tools** PowerShell module for deployment automation.

---

## Prerequisites

Install the PPDS.Tools module before using deployment scripts:

```powershell
Install-Module PPDS.Tools -Scope CurrentUser
```

---

## Available Scripts

| Script | Purpose |
|--------|---------|
| `Deploy-Plugins.ps1` | Deploy plugin assemblies and register steps |
| `Deploy-Components.ps1` | Deploy plugins and web resources |
| `Extract-PluginRegistrations.ps1` | Generate registrations.json from compiled assemblies |
| `Create-SchemaComponents.ps1` | Create tables, option sets, environment variables |
| `Add-MissingSolutionComponents.ps1` | Add existing components to solution |
| `Generate-Entities.ps1` | Generate early-bound entity classes |
| `Generate-Snk.ps1` | Generate strong name key file for assembly signing |
| `Setup-BranchProtection.ps1` | Configure GitHub branch protection rules via gh CLI |

---

## Authentication

Scripts authenticate to Dataverse using environment files or interactive login.

### Environment Files

Create `.env.{environment}` files with your credentials:

```ini
# .env.dev
DATAVERSE_URL=https://orgXXXXX.crm.dynamics.com
SP_TENANT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
SP_APPLICATION_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
SP_CLIENT_SECRET=your-secret-here
```

| Variable | Description | Required |
|----------|-------------|----------|
| `DATAVERSE_URL` | Environment URL | Yes |
| `SP_TENANT_ID` | Azure AD Tenant ID | For SPN auth |
| `SP_APPLICATION_ID` | App Registration Client ID | For SPN auth |
| `SP_CLIENT_SECRET` | App Registration Secret | For SPN auth |

### Usage Examples

```powershell
# Using environment file (recommended for local dev)
.\tools\Deploy-Plugins.ps1 -EnvFile ".env.dev"

# Interactive OAuth for quick testing
.\tools\Deploy-Plugins.ps1 -Interactive

# Dry run to preview changes
.\tools\Deploy-Plugins.ps1 -WhatIf
```

---

## Security Practices

### Do

- Use `.env.{environment}` files for credentials (gitignored)
- Use service principals for automation
- Validate connection before performing work

### Don't

- Hardcode credentials in scripts
- Commit `.env` files with secrets
- Use `-Interactive` in CI/CD pipelines

---

## See Also

- [ENVIRONMENT_SETUP_GUIDE.md](../guides/ENVIRONMENT_SETUP_GUIDE.md) - Setting up environments and service principals
- [PAC_CLI_REFERENCE.md](PAC_CLI_REFERENCE.md) - PAC CLI commands
- [.env.example](../../.env.example) - Environment variable template
