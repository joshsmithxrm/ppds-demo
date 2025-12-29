# Local Development Guide

This guide covers setting up your local development environment for .NET projects that connect to Dataverse, including secure credential management using .NET User Secrets.

---

## Prerequisites

| Requirement                                            | Purpose                                        |
| ------------------------------------------------------ | ---------------------------------------------- |
| [.NET SDK 8.0+](https://dotnet.microsoft.com/download) | Building and running .NET projects             |
| Azure AD App Registration                              | Service Principal for Dataverse authentication |
| Dataverse environment                                  | Target for local testing                       |

---

## Understanding Credential Sources

This repository uses a layered approach to credential management:

| Source                | Purpose                                     | Committed to Git           |
| --------------------- | ------------------------------------------- | -------------------------- |
| `appsettings.json`    | Configuration structure and placeholders    | Yes                        |
| `.NET User Secrets`   | Developer-specific values including secrets | No (stored in `%APPDATA%`) |
| Environment variables | CI/CD and production deployments            | No                         |

### Configuration Flow

```
appsettings.json (structure + placeholders)
    ↓
User Secrets override placeholders
    ↓
%APPDATA%\Microsoft\UserSecrets\{project-id}\secrets.json
    ↓
Host.CreateDefaultBuilder() auto-loads in Development
    ↓
IConfiguration available in your app
```

---

## Configuration Structure

All environments are configured under `Dataverse:Environments:*`. Each environment has its own URL and connections:

```json
{
  "Dataverse": {
    "Environments": {
      "Dev": {
        "Url": "https://dev.crm.dynamics.com",
        "Connections": [
          {
            "Name": "Primary",
            "ClientId": "00000000-0000-0000-0000-000000000000",
            "ClientSecret": "from-user-secrets"
          }
        ]
      },
      "QA": {
        "Url": "https://qa.crm.dynamics.com",
        "Connections": [
          {
            "Name": "Primary",
            "ClientId": "00000000-0000-0000-0000-000000000000",
            "ClientSecret": "from-user-secrets"
          }
        ]
      }
    },
    "DefaultEnvironment": "Dev",
    "Pool": {
      "MinPoolSize": 1,
      "DisableAffinityCookie": true
    }
  }
}
```

### Configuration Properties

| Property                                                  | Purpose                                |
| --------------------------------------------------------- | -------------------------------------- |
| `Dataverse:Environments:{env}:Url`                        | Environment URL                        |
| `Dataverse:Environments:{env}:Connections:N:Name`         | Connection identifier for logging      |
| `Dataverse:Environments:{env}:Connections:N:ClientId`     | Azure AD App Registration ID           |
| `Dataverse:Environments:{env}:Connections:N:ClientSecret` | Client secret (dev) or env var name containing secret (prod) |
| `Dataverse:DefaultEnvironment`                            | Default environment when not specified |

---

## Setting Up User Secrets

### Step 1: Gather Your Credentials

You need the following from your Azure AD App Registration:

| Value         | Where to Find                                                           |
| ------------- | ----------------------------------------------------------------------- |
| Dataverse URL | Power Platform Admin Center > Environments > Your Env > Environment URL |
| Client ID     | Azure Portal > App Registrations > Your App > Application (client) ID   |
| Client Secret | Azure Portal > App Registrations > Your App > Certificates & secrets    |

### Step 2: Configure User Secrets

Navigate to the project directory and set the secrets:

```powershell
cd src/Console/PPDS.Dataverse.Demo

# Set the Dev environment
dotnet user-secrets set "Dataverse:Environments:Dev:Url" "https://yourorg.crm.dynamics.com"
dotnet user-secrets set "Dataverse:Environments:Dev:Connections:0:ClientId" "00000000-0000-0000-0000-000000000000"
dotnet user-secrets set "Dataverse:Environments:Dev:Connections:0:ClientSecret" "your-client-secret-value"
```

### Step 3: Verify Secrets

List the configured secrets:

```powershell
dotnet user-secrets list
```

Expected output:

```
Dataverse:Environments:Dev:Url = https://yourorg.crm.dynamics.com
Dataverse:Environments:Dev:Connections:0:ClientId = 00000000-0000-0000-0000-000000000000
Dataverse:Environments:Dev:Connections:0:ClientSecret = ****
```

---

## Running the Demo App

Once secrets are configured, run the demo:

```powershell
# From repository root
dotnet run --project src/Console/PPDS.Dataverse.Demo -- whoami

# Or from project directory
cd src/Console/PPDS.Dataverse.Demo
dotnet run -- whoami
```

Expected output:

```
Testing Dataverse Connectivity
==============================

Connecting to Dataverse...

WhoAmI Result:
  User ID:         xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
  Organization ID: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
  Business Unit:   xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx

Pool Statistics:
  Total Connections: 5
  Active:            1
  Idle:              4
  Requests Served:   1
```

---

## How It Works

### Configuration Hierarchy

`Host.CreateDefaultBuilder()` automatically loads configuration from multiple sources (in order of precedence):

1. `appsettings.json` (lowest priority - structure and placeholders)
2. `appsettings.{Environment}.json`
3. **User Secrets** (when `DOTNET_ENVIRONMENT=Development`)
4. Environment variables (highest priority)

### User Secrets Storage Location

Secrets are stored outside the project directory:

| OS          | Location                                                       |
| ----------- | -------------------------------------------------------------- |
| Windows     | `%APPDATA%\Microsoft\UserSecrets\{UserSecretsId}\secrets.json` |
| macOS/Linux | `~/.microsoft/usersecrets/{UserSecretsId}/secrets.json`        |

The `UserSecretsId` is defined in the project's `.csproj` file:

```xml
<PropertyGroup>
  <UserSecretsId>ppds-dataverse-demo</UserSecretsId>
</PropertyGroup>
```

### Configuration Binding

The SDK uses environment-specific pool creation:

```csharp
// Create pool for specific environment
services.AddDataverseConnectionPool(context.Configuration, environment: "Dev");
```

This binds configuration from `Dataverse:Environments:Dev:*`.

---

## Troubleshooting

### "Connection pool is not enabled"

User secrets are not configured or not being loaded.

**Check 1:** Verify secrets exist

```powershell
dotnet user-secrets list
```

**Check 2:** Verify environment is Development

```powershell
echo $env:DOTNET_ENVIRONMENT  # Should be "Development" or not set
```

**Check 3:** Verify UserSecretsId in .csproj

```xml
<UserSecretsId>ppds-dataverse-demo</UserSecretsId>
```

### "Environment 'Dev' not configured"

The Dev environment configuration is missing.

**Check:** Verify the full path in user secrets:

```powershell
dotnet user-secrets list | findstr "Dev"
```

Should show:

```
Dataverse:Environments:Dev:Url = ...
Dataverse:Environments:Dev:Connections:0:ClientId = ...
Dataverse:Environments:Dev:Connections:0:ClientSecret = ...
```

### "Failed to connect to Dataverse"

Credentials are invalid or Service Principal lacks access.

**Check 1:** Verify URL format

```
https://yourorg.crm.dynamics.com  (no trailing slash)
```

**Check 2:** Verify Service Principal has access

- Must be registered as Application User in the target environment
- Must have System Administrator or appropriate security role

**Check 3:** Test credentials with PAC CLI

```powershell
pac auth create --name test `
  --applicationId YOUR_CLIENT_ID `
  --clientSecret YOUR_CLIENT_SECRET `
  --tenant YOUR_TENANT_ID `
  --environment YOUR_DATAVERSE_URL

pac org who  # Should show org details
```

### Clearing and Resetting Secrets

```powershell
# Remove all secrets for this project
dotnet user-secrets clear

# Remove a specific secret
dotnet user-secrets remove "Dataverse:Environments:Dev:Connections:0:ClientSecret"
```

---

## Multi-Environment Setup

For cross-environment operations (migration, comparison), configure multiple environments:

```powershell
cd src/Console/PPDS.Dataverse.Demo

# Dev environment
dotnet user-secrets set "Dataverse:Environments:Dev:Url" "https://dev.crm.dynamics.com"
dotnet user-secrets set "Dataverse:Environments:Dev:Connections:0:ClientId" "dev-client-id"
dotnet user-secrets set "Dataverse:Environments:Dev:Connections:0:ClientSecret" "dev-secret"

# QA environment
dotnet user-secrets set "Dataverse:Environments:QA:Url" "https://qa.crm.dynamics.com"
dotnet user-secrets set "Dataverse:Environments:QA:Connections:0:ClientId" "qa-client-id"
dotnet user-secrets set "Dataverse:Environments:QA:Connections:0:ClientSecret" "qa-secret"
```

Then use environment-specific commands:

```powershell
dotnet run -- clean --env QA
dotnet run -- migrate-to-qa
dotnet run -- generate-user-mapping
```

---

## Production Configuration

For production deployments, use environment variables instead of User Secrets.

### Using ClientSecret

Configure `appsettings.json` or environment-specific config:

```json
{
  "Dataverse": {
    "Environments": {
      "Prod": {
        "Url": "https://prod.crm.dynamics.com",
        "Connections": [
          {
            "Name": "Primary",
            "ClientId": "production-client-id",
            "ClientSecret": "DATAVERSE_SECRET"
          }
        ]
      }
    }
  }
}
```

The platform sets the environment variable:

| Platform          | How to Set                                  |
| ----------------- | ------------------------------------------- |
| Azure App Service | Configuration > Application settings        |
| GitHub Actions    | Repository secrets + `env:` in workflow     |
| Azure DevOps      | Pipeline variables (secret)                 |
| Docker            | `-e DATAVERSE_SECRET=xxx` or docker-compose |
| Kubernetes        | Secret + environment variable in deployment |

### Environment Variable Format

For environment variables that override config, use double underscore as separator:

```powershell
# PowerShell
$env:Dataverse__Environments__Dev__Url = "https://dev.crm.dynamics.com"
$env:Dataverse__Environments__Dev__Connections__0__ClientId = "client-id"

# Bash
export Dataverse__Environments__Dev__Url="https://dev.crm.dynamics.com"
export Dataverse__Environments__Dev__Connections__0__ClientId="client-id"
```

---

## Security Best Practices

| Practice                                  | Reason                                        |
| ----------------------------------------- | --------------------------------------------- |
| Never commit secrets to git               | Use `.gitignore` for `secrets.json`, `.env.*` |
| Use Service Principals                    | Not tied to user accounts, can be rotated     |
| Rotate secrets regularly                  | Limit blast radius of compromised credentials |
| Use different credentials per environment | Dev credentials can't access Prod             |
| Use User Secrets for development          | Microsoft-recommended secure local storage    |
| Use environment variables for production  | Secrets never in config files                 |

---

## See Also

- [Environment Setup Guide](ENVIRONMENT_SETUP_GUIDE.md) - Power Platform environment configuration
- [Getting Started Guide](GETTING_STARTED_GUIDE.md) - Repository setup and deployment
- [Microsoft Docs: Safe storage of secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
