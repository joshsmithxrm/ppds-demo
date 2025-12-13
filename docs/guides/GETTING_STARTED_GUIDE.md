# Getting Started Guide

Step-by-step guide to clone this repository, configure your environments, and deploy the demo solution.

---

## Prerequisites

Before starting, ensure you have:

| Requirement | Purpose |
|-------------|---------|
| [Power Platform CLI](https://learn.microsoft.com/en-us/power-platform/developer/cli/introduction) | Solution management, deployment |
| [.NET SDK 8.x](https://dotnet.microsoft.com/download) | Building plugins |
| [Git](https://git-scm.com/) | Source control |
| Power Platform environments | Dev, QA, Prod (or subset) |
| System Administrator role | In target environments |

---

## Step 1: Clone the Repository

```bash
git clone https://github.com/your-org/Power-Platform-Developer-Suite-Demo-Solution.git
cd Power-Platform-Developer-Suite-Demo-Solution
```

---

## Step 2: Configure Environment Files

Copy the example environment file for each environment you'll use:

```bash
cp .env.example .env.dev
cp .env.example .env.qa
cp .env.example .env.prod
```

Edit each file with your environment-specific values:

```bash
# .env.dev
ENVIRONMENT_NAME=Dev
DATAVERSE_URL=https://your-dev-org.crm.dynamics.com/
TENANT_ID=your-tenant-id
```

> **Note:** These files are gitignored. Never commit credentials to source control.

---

## Step 3: Authenticate with PAC CLI

Create authentication profiles for each environment:

```bash
# Dev environment (interactive login)
pac auth create --url https://your-dev-org.crm.dynamics.com --name dev

# QA environment
pac auth create --url https://your-qa-org.crm.dynamics.com --name qa

# Prod environment
pac auth create --url https://your-prod-org.crm.dynamics.com --name prod
```

Verify your profiles:

```bash
pac auth list
```

Switch between environments:

```bash
pac auth select --name dev
```

---

## Step 4: Build the Solution

Build the .NET projects (plugins, workflow activities):

```bash
dotnet build PPDSDemo.sln --configuration Release
```

Expected output:
- `src/Plugins/PPDSDemo.Plugins/bin/Release/net462/PPDSDemo.Plugins.dll`
- `src/PluginPackages/PPDSDemo.PluginPackage/bin/Release/PPDSDemo.PluginPackage.*.nupkg`

---

## Step 5: Import to Dev Environment

### Option A: Import Existing Solution

If the solution already exists in source control:

```bash
# Select Dev environment
pac auth select --name dev

# Build unmanaged solution (for Dev import)
dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Debug

# Import to Dev
pac solution import \
  --path solutions/PPDSDemo/bin/Debug/PPDSDemo.zip \
  --publish-changes
```

### Option B: Fresh Start

If starting from scratch, create the solution in the Power Platform maker portal, then export:

```bash
pac auth select --name dev

# Export BOTH unmanaged and managed
pac solution export --name PPDSDemo --path solutions/exports --managed false --overwrite
pac solution export --name PPDSDemo --path solutions/exports --managed true --overwrite

# Unpack with packagetype Both (creates unified source)
pac solution unpack \
  --zipfile solutions/exports/PPDSDemo.zip \
  --folder solutions/PPDSDemo/src \
  --packagetype Both \
  --allowDelete \
  --allowWrite
```

> **Note:** Using `--packagetype Both` enables building both managed (Release) and unmanaged (Debug) from the same source.

---

## Step 6: Set Up CI/CD (GitHub Actions)

### Configure GitHub Secrets

In your GitHub repository, add these secrets:

| Secret | Description |
|--------|-------------|
| `POWERPLATFORM_CLIENT_SECRET` | Service principal client secret |

### Configure GitHub Variables

Add these variables per environment (Settings → Environments):

| Variable | Example |
|----------|---------|
| `POWERPLATFORM_ENVIRONMENT_URL` | `https://your-org.crm.dynamics.com` |
| `POWERPLATFORM_TENANT_ID` | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| `POWERPLATFORM_CLIENT_ID` | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |

### Create Service Principal

```bash
# Register app in Azure AD
az ad app create --display-name "Power Platform CI/CD"

# Create service principal
az ad sp create --id <app-id>

# Grant Power Platform admin role (in Power Platform Admin Center)
```

See [ENVIRONMENT_SETUP_GUIDE.md](ENVIRONMENT_SETUP_GUIDE.md) for detailed service principal setup.

---

## Step 7: Verify Deployment Pipeline

### Manual Trigger

1. Go to **Actions** in your GitHub repository
2. Select **CD: Deploy to QA**
3. Click **Run workflow**
4. Select the branch and run

### Automatic Trigger

Push to `develop` branch to trigger QA deployment:

```bash
git checkout develop
git merge feature/your-feature
git push origin develop
```

---

## Development Workflow

### Daily Development

```bash
# 1. Create feature branch
git checkout develop
git pull origin develop
git checkout -b feature/my-feature

# 2. Make changes in Dev environment (Power Platform maker portal)

# 3. Export changes (both managed and unmanaged)
pac auth select --name dev
pac solution export --name PPDSDemo --path solutions/exports --managed false --overwrite
pac solution export --name PPDSDemo --path solutions/exports --managed true --overwrite
pac solution unpack --zipfile solutions/exports/PPDSDemo.zip --folder solutions/PPDSDemo/src --packagetype Both --allowDelete --allowWrite

# 4. Build and test plugins
dotnet build PPDSDemo.sln --configuration Release
dotnet test

# 5. Commit and push
git add .
git commit -m "feat: add new feature"
git push origin feature/my-feature

# 6. Create PR to develop
```

### Plugin Development

```bash
# 1. Edit plugin code in src/Plugins/

# 2. Build
dotnet build PPDSDemo.sln --configuration Release

# 3. Register/update in Dev using Plugin Registration Tool or pac plugin push

# 4. Export solution to capture registration (both managed and unmanaged)
pac solution export --name PPDSDemo --path solutions/exports --managed false --overwrite
pac solution export --name PPDSDemo --path solutions/exports --managed true --overwrite
pac solution unpack --zipfile solutions/exports/PPDSDemo.zip --folder solutions/PPDSDemo/src --packagetype Both --allowDelete --allowWrite
```

---

## Troubleshooting

### Authentication Issues

```bash
# Clear and recreate auth
pac auth clear
pac auth create --url https://your-org.crm.dynamics.com --name dev
```

### Build Failures

```bash
# Clean and rebuild
dotnet clean PPDSDemo.sln
dotnet restore PPDSDemo.sln
dotnet build PPDSDemo.sln --configuration Release
```

### Import Failures

Check the import job in Power Platform Admin Center:
1. Go to **Environments** → Select environment
2. **Settings** → **Solutions** → **Solution History**

Common issues:
- Missing dependencies (check solution dependencies)
- Version conflicts (ensure target doesn't have newer version)
- Component conflicts (check for duplicate components)

---

## Next Steps

- Review [ENVIRONMENT_SETUP_GUIDE.md](ENVIRONMENT_SETUP_GUIDE.md) for detailed environment configuration
- Read [ALM_OVERVIEW.md](../strategy/ALM_OVERVIEW.md) for ALM philosophy
- Check [PIPELINE_STRATEGY.md](../strategy/PIPELINE_STRATEGY.md) for CI/CD details
- See [PLUGIN_COMPONENTS_REFERENCE.md](../reference/PLUGIN_COMPONENTS_REFERENCE.md) for plugin development

---

## See Also

- [Environment Setup Guide](ENVIRONMENT_SETUP_GUIDE.md)
- [ALM Overview](../strategy/ALM_OVERVIEW.md)
- [Branching Strategy](../strategy/BRANCHING_STRATEGY.md)
- [Pipeline Strategy](../strategy/PIPELINE_STRATEGY.md)
