# Local Development Guide

This guide covers setting up your local development environment for .NET projects that connect to Dataverse, including secure credential management using .NET User Secrets.

---

## Prerequisites

| Requirement | Purpose |
|-------------|---------|
| [.NET SDK 8.0+](https://dotnet.microsoft.com/download) | Building and running .NET projects |
| Access to `.env.dev` file | Contains environment credentials |
| Dataverse environment | Target for local testing |

---

## Understanding Credential Sources

This repository uses a layered approach to credential management:

| Source | Purpose | Committed to Git |
|--------|---------|------------------|
| `.env.{environment}` files | Master credential store for each environment | No (gitignored) |
| `.NET User Secrets` | Per-project secrets for local development | No (stored in `%APPDATA%`) |
| `appsettings.json` | Non-sensitive configuration defaults | Yes |
| Environment variables | CI/CD and container deployments | No |

### Credential Flow

```
.env.dev (master credentials)
    ↓
dotnet user-secrets set (one-time setup)
    ↓
%APPDATA%\Microsoft\UserSecrets\{project-id}\secrets.json
    ↓
Host.CreateDefaultBuilder() auto-loads in Development
    ↓
IConfiguration available in your app
```

---

## Setting Up .NET User Secrets

### Step 1: Locate Your Credentials

Open the `.env.dev` file in the repository root. You'll find credentials in this format:

```bash
# Environment URL
DATAVERSE_URL=https://orgXXXXXX.crm.dynamics.com/

# Service Principal (for automation)
SP_APPLICATION_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
SP_CLIENT_SECRET=your-client-secret
SP_TENANT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

### Step 2: Build the Connection String

Dataverse connection strings use this format for Service Principal authentication:

```
AuthType=ClientSecret;Url={DATAVERSE_URL};ClientId={SP_APPLICATION_ID};ClientSecret={SP_CLIENT_SECRET}
```

Using values from `.env.dev`:

```
AuthType=ClientSecret;Url=https://orgXXXXXX.crm.dynamics.com;ClientId=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx;ClientSecret=your-client-secret
```

### Step 3: Initialize User Secrets

Navigate to the project directory and set the secrets:

```bash
cd src/Console/PPDS.Dataverse.Demo

# Set the connection name
dotnet user-secrets set "Dataverse:Connections:0:Name" "Primary"

# Set the connection string (use your actual values from .env.dev)
dotnet user-secrets set "Dataverse:Connections:0:ConnectionString" "AuthType=ClientSecret;Url=https://orgXXXXXX.crm.dynamics.com;ClientId=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx;ClientSecret=your-client-secret"
```

### Step 4: Verify Secrets

List the configured secrets:

```bash
dotnet user-secrets list
```

Expected output:

```
Dataverse:Connections:0:ConnectionString = AuthType=ClientSecret;Url=...
Dataverse:Connections:0:Name = Primary
```

---

## Running the Demo App

Once secrets are configured, run the demo:

```bash
# From repository root
dotnet run --project src/Console/PPDS.Dataverse.Demo

# Or with explicit environment
DOTNET_ENVIRONMENT=Development dotnet run --project src/Console/PPDS.Dataverse.Demo
```

Expected output:

```
PPDS.Dataverse Connection Pool Demo
====================================

info: PPDS.Dataverse.Pooling.DataverseConnectionPool[0]
      DataverseConnectionPool initialized. Connections: 1, MaxPoolSize: 50, Strategy: ThrottleAware
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

1. `appsettings.json` (lowest priority)
2. `appsettings.{Environment}.json`
3. **User Secrets** (when `DOTNET_ENVIRONMENT=Development`)
4. Environment variables (highest priority)

### User Secrets Storage Location

Secrets are stored outside the project directory:

| OS | Location |
|----|----------|
| Windows | `%APPDATA%\Microsoft\UserSecrets\{UserSecretsId}\secrets.json` |
| macOS/Linux | `~/.microsoft/usersecrets/{UserSecretsId}/secrets.json` |

The `UserSecretsId` is defined in the project's `.csproj` file:

```xml
<PropertyGroup>
  <UserSecretsId>ppds-dataverse-demo</UserSecretsId>
</PropertyGroup>
```

### Configuration Binding

The SDK uses the standard .NET configuration pattern:

```csharp
services.AddDataverseConnectionPool(context.Configuration);
```

This binds the `Dataverse` section from configuration to `DataverseOptions`:

```json
{
  "Dataverse": {
    "Connections": [
      {
        "Name": "Primary",
        "ConnectionString": "AuthType=ClientSecret;..."
      }
    ],
    "Pool": {
      "MaxPoolSize": 50
    }
  }
}
```

---

## Troubleshooting

### "Connection pool is not enabled"

User secrets are not configured or not being loaded.

**Check 1:** Verify secrets exist
```bash
dotnet user-secrets list
```

**Check 2:** Verify environment is Development
```bash
echo $DOTNET_ENVIRONMENT  # Should be "Development"
```

**Check 3:** Verify UserSecretsId in .csproj
```xml
<UserSecretsId>ppds-dataverse-demo</UserSecretsId>
```

### "Failed to connect to Dataverse"

Connection string is invalid or credentials are incorrect.

**Check 1:** Verify connection string format
```
AuthType=ClientSecret;Url=https://org.crm.dynamics.com;ClientId=xxx;ClientSecret=xxx
```

**Check 2:** Verify Service Principal has access
- Must be registered as Application User in the target environment
- Must have System Administrator or appropriate security role

**Check 3:** Test credentials with PAC CLI
```bash
pac auth create --name test \
  --applicationId <CLIENT_ID> \
  --clientSecret <CLIENT_SECRET> \
  --tenant <TENANT_ID> \
  --url <DATAVERSE_URL>

pac org who  # Should show org details
```

### Clearing and Resetting Secrets

```bash
# Remove all secrets for this project
dotnet user-secrets clear

# Remove a specific secret
dotnet user-secrets remove "Dataverse:Connections:0:ConnectionString"
```

---

## Alternative: Environment Variables

For CI/CD or containerized environments, use environment variables instead of user secrets:

```bash
# Bash/Linux/macOS
export Dataverse__Connections__0__Name="Primary"
export Dataverse__Connections__0__ConnectionString="AuthType=ClientSecret;..."

# PowerShell
$env:Dataverse__Connections__0__Name = "Primary"
$env:Dataverse__Connections__0__ConnectionString = "AuthType=ClientSecret;..."
```

Note: Use double underscore (`__`) as the hierarchy separator in environment variables.

---

## Security Best Practices

| Practice | Reason |
|----------|--------|
| Never commit secrets to git | Use `.gitignore` for `.env.*`, `secrets.json` |
| Use Service Principals | Not tied to user accounts, can be rotated |
| Rotate secrets regularly | Limit blast radius of compromised credentials |
| Use different credentials per environment | Dev credentials can't access Prod |
| Store master credentials in `.env` files | Single source of truth, gitignored |

---

## See Also

- [Environment Setup Guide](ENVIRONMENT_SETUP_GUIDE.md) - Power Platform environment configuration
- [Getting Started Guide](GETTING_STARTED_GUIDE.md) - Repository setup and deployment
- [Microsoft Docs: Safe storage of secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
