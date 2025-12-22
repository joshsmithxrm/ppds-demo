# CLAUDE.md - ppds-demo

**Reference implementation for Dynamics 365 / Dataverse projects.**

**Part of the PPDS Ecosystem** - See `../CLAUDE.md` for cross-project context.

---

## 📊 Solution Context

| Property | Value |
|----------|-------|
| Solution Name | `PPDSDemo` |
| Publisher Prefix | `ppds` |
| Schema Prefix | `ppds_` |
| Entity Binding | Early-bound (see `src/Entities/`) |
| Plugin Framework | PPDS.Plugins (attribute-based registration) |

---

## 🔐 Dataverse Connection (User Secrets)

The demo app uses .NET User Secrets for Dataverse credentials.

| Property | Value |
|----------|-------|
| UserSecretsId | `ppds-dataverse-demo` |
| Pool Config | `Dataverse:Connections:0` |
| Environment Config | `Environments:{name}:ConnectionString` |

### Configuration Structure

The SDK uses **two separate config patterns** for different purposes:

1. **`Dataverse:Connections:*`** - For connection pool (load-balancing within ONE org)
2. **`Environments:*`** - For explicit environment targeting (cross-env operations)

> ⚠️ **Never put multiple orgs in `Dataverse:Connections`** - the pool will load-balance randomly across them. See [SDK README](../sdk/src/PPDS.Dataverse/README.md#multi-environment-scenarios).

### Single Environment (Default)

For most work, configure just the pool connection:

```powershell
cd src/Console/PPDS.Dataverse.Demo
dotnet user-secrets set "Dataverse:Connections:0:Name" "Primary"
dotnet user-secrets set "Dataverse:Connections:0:ConnectionString" "AuthType=ClientSecret;Url=https://dev.crm.dynamics.com;ClientId=...;ClientSecret=..."
```

### Multiple Environments (Cross-Env Migration)

For cross-environment operations, add environment-specific connections:

```powershell
cd src/Console/PPDS.Dataverse.Demo

# Pool connection (used by whoami, demo-features, general queries)
dotnet user-secrets set "Dataverse:Connections:0:Name" "Primary"
dotnet user-secrets set "Dataverse:Connections:0:ConnectionString" "AuthType=...;Url=https://dev.crm.dynamics.com;..."

# Environment-specific connections (used by seed, clean, migrate-to-qa, load-geo-data)
dotnet user-secrets set "Environments:Dev:Name" "DEV"
dotnet user-secrets set "Environments:Dev:ConnectionString" "AuthType=...;Url=https://dev.crm.dynamics.com;..."

dotnet user-secrets set "Environments:QA:Name" "QA"
dotnet user-secrets set "Environments:QA:ConnectionString" "AuthType=...;Url=https://qa.crm.dynamics.com;..."
```

### Check Current Connections

```powershell
dotnet user-secrets list --project src/Console/PPDS.Dataverse.Demo
```

### Command Connection Usage

| Command | Connection Source | Why |
|---------|-------------------|-----|
| `whoami` | Pool (`Dataverse:Connections:0`) | General pool usage |
| `demo-features` | Pool (`Dataverse:Connections:0`) | General pool usage |
| `seed` | `Environments:Dev` | Must target specific env |
| `clean` | `Environments:Dev` (default) | Must target specific env |
| `clean --env QA` | `Environments:QA` | Explicit targeting |
| `load-geo-data` | `Environments:Dev` (default) | Must target specific env |
| `migrate-to-qa` | `Environments:Dev` + `Environments:QA` | Cross-env operation |

### Why Two Config Sections?

The pool is designed for **load-balancing within a single org** (multiple App Users = multiplied API quota). Commands that modify data need **explicit environment targeting** to avoid accidentally writing to the wrong org.

---

## 🚫 NEVER

| Rule | Why |
|------|-----|
| **Modify files outside `demo/`** | This repo only; sdk/, tools/, extension/ are separate repos |
| `Console.WriteLine` in plugins | Sandbox blocks it; use `ITracingService` |
| Hardcoded GUIDs | Breaks across environments; use config or queries |
| `Xrm.Page` in JavaScript | Deprecated since v9; use `formContext` |
| `alert()` in web resources | Blocked in UCI; use `Xrm.Navigation.openAlertDialog` |
| Static state in plugins | Sandbox recycles instances; state is lost |
| External assemblies in plugins | Sandbox whitelist only; ILMerge if needed |
| Separate Managed/Unmanaged folders | Use `--packagetype Both` for unified source |
| PR directly to main | Always target `develop` first |
| Squash merge develop→main | Use regular merge to preserve feature commits |
| Sync plugins in Pre-Create | Entity doesn't exist yet; use Post-Create |

> **Cross-Repo Changes:** If a fix requires changes to `sdk/`, `tools/`, `extension/`, or `alm/`,
> describe the proposed change and get approval first. Do NOT edit files in other repositories.

---

## ✅ ALWAYS

| Rule | Why |
|------|-----|
| `ITracingService` for debugging | Only way to get runtime output in sandbox |
| try/catch with `InvalidPluginExecutionException` | Platform requires this type for user-facing errors |
| Check `InputParameters.Contains("Target")` | Not all messages have Target; prevents null ref |
| `formContext` from execution context | Required pattern since Xrm.Page deprecation |
| Namespace pattern in JS (`PPDSDemo.Account`) | Avoids global pollution and naming conflicts |
| Early-bound entities for type safety | Compile-time checking prevents runtime errors |
| Deployment settings per environment | Environment-specific connection refs and variables |
| Conventional commits | `feat:`, `fix:`, `chore:` for clear history |

---

## 💻 Error Handling Pattern

```csharp
// ✅ Correct - Full error handling with tracing
public void Execute(IServiceProvider serviceProvider)
{
    var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
    var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

    try
    {
        tracingService.Trace("Plugin started: {0}", context.MessageName);
        // Plugin logic here
        tracingService.Trace("Plugin completed successfully");
    }
    catch (InvalidPluginExecutionException)
    {
        throw; // Re-throw business exceptions as-is
    }
    catch (Exception ex)
    {
        tracingService.Trace("Error: {0}", ex.ToString());
        throw new InvalidPluginExecutionException(
            $"An error occurred. Contact support with timestamp: {DateTime.UtcNow:O}", ex);
    }
}

// ❌ Wrong - No tracing, swallows exceptions
public void Execute(IServiceProvider serviceProvider)
{
    try
    {
        // Plugin logic
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.Message); // Blocked in sandbox!
    }
}
```

---

## 🎯 When to Use What

| Scenario | Use | Why |
|----------|-----|-----|
| Sync validation/modification | **Plugin (Pre-Operation)** | Runs in transaction, can modify/cancel |
| Post-save automation | **Plugin (Post-Operation Async)** | Non-blocking, retries on failure |
| User-triggered automation | **Power Automate** | Easier to modify, visible to makers |
| Long-running process (>2 min) | **Azure Function** | No platform timeout limits |
| External system integration | **Custom API + Azure** | Clean contract, scalable |
| Simple field calculations | **Calculated/Rollup columns** | Zero code, platform-managed |

---

## 📛 Naming Conventions

Dataverse uses two name formats for schema objects:
- **Logical Name**: Always lowercase (`ppds_customerrecord`)
- **Schema Name**: PascalCase with lowercase prefix (`ppds_CustomerRecord`)

| Component | Logical Name | Schema Name |
|-----------|--------------|-------------|
| Tables | `ppds_demorecord` | `ppds_DemoRecord` |
| Columns | `ppds_firstname` | `ppds_FirstName` |
| Option Sets | `ppds_status` | `ppds_Status` |

| Component | Convention | Example |
|-----------|------------|---------|
| Web Resources | `prefix_/path/name.ext` | `ppds_/scripts/account.form.js` |
| Plugin Classes | `{Entity}{Message}Plugin` | `AccountCreatePlugin` |
| Plugin Steps | `{Entity}: {Message} - {Description}` | `account: Create - Validate tax ID` |

**In code:** Use logical names for API calls (`account`, `ppds_firstname`). Early-bound classes use Schema Names for properties.

---

## 📁 Solution Structure

```
solutions/PPDSDemo/
├── PPDSDemo.cdsproj              # Debug=Unmanaged, Release=Managed
├── config/
│   ├── qa.deploymentsettings.json
│   └── prod.deploymentsettings.json
└── src/                          # Flat structure (packagetype Both)
    ├── Other/Solution.xml
    ├── Entities/
    ├── OptionSets/
    └── WebResources/

src/
├── Plugins/PPDSDemo.Plugins/     # Plugin assemblies
├── Entities/PPDSDemo.Entities/   # Early-bound classes
└── WebResources/                 # TypeScript source
```

---

## 🛠️ Common Commands

```bash
# Build plugins
dotnet build src/Plugins/PPDSDemo.Plugins/PPDSDemo.Plugins.csproj -c Release

# Build solution (managed for deployment)
dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Release

# Export and unpack from environment
pac solution export --name PPDSDemo --path exports --managed false --overwrite
pac solution export --name PPDSDemo --path exports --managed true --overwrite
pac solution unpack --zipfile exports/PPDSDemo.zip --folder solutions/PPDSDemo/src --packagetype Both --allowDelete --allowWrite

# Regenerate early-bound entities
pac modelbuilder build --outdirectory src/Entities/PPDSDemo.Entities

# Deploy plugins
.\tools\Deploy-Plugins.ps1 -Environment Dev
```

---

## 🔀 Git Workflow

| Flow | Merge Strategy | Why |
|------|----------------|-----|
| `feature/*` → `develop` | Squash | Clean history, one commit per feature |
| `develop` → `main` | Regular merge | Preserve features, clear release points |
| `hotfix/*` → `main` | Regular merge | Then cherry-pick to develop |

**Automated CI/CD:** Exports from Dev commit to `develop` automatically. Merges to `develop` deploy to QA. Merges to `main` deploy to Prod.

---

## 📚 Reference Documentation

### Strategy (Why)
- [ALM_OVERVIEW.md](docs/strategy/ALM_OVERVIEW.md) - ALM philosophy and approach
- [BRANCHING_STRATEGY.md](docs/strategy/BRANCHING_STRATEGY.md) - Git workflow details
- [ENVIRONMENT_STRATEGY.md](docs/strategy/ENVIRONMENT_STRATEGY.md) - Dev/QA/Prod setup
- [PIPELINE_STRATEGY.md](docs/strategy/PIPELINE_STRATEGY.md) - CI/CD design

### Reference (How)
- [PLUGIN_COMPONENTS_REFERENCE.md](docs/reference/PLUGIN_COMPONENTS_REFERENCE.md) - Plugin patterns
- [WEBRESOURCE_PATTERNS.md](docs/reference/WEBRESOURCE_PATTERNS.md) - JavaScript/TypeScript
- [SOLUTION_STRUCTURE_REFERENCE.md](docs/reference/SOLUTION_STRUCTURE_REFERENCE.md) - Solution packaging
- [PAC_CLI_REFERENCE.md](docs/reference/PAC_CLI_REFERENCE.md) - PAC CLI commands
- [TESTING_PATTERNS.md](docs/reference/TESTING_PATTERNS.md) - Unit testing with FakeXrmEasy

### Guides (Step-by-Step)
- [GETTING_STARTED_GUIDE.md](docs/guides/GETTING_STARTED_GUIDE.md) - Initial setup
- [ENVIRONMENT_SETUP_GUIDE.md](docs/guides/ENVIRONMENT_SETUP_GUIDE.md) - Environment configuration
- [BRANCH_PROTECTION_GUIDE.md](docs/guides/BRANCH_PROTECTION_GUIDE.md) - GitHub rulesets
- [PLUGIN_REMOVAL_GUIDE.md](docs/guides/PLUGIN_REMOVAL_GUIDE.md) - Removing plugin steps

---

## ⚖️ Decision Presentation

When presenting choices or asking questions:
1. **Lead with your recommendation** and rationale
2. **List alternatives considered** and why they're not preferred
3. **Ask for confirmation**, not open-ended input

❌ "What testing approach should we use?"
✅ "I recommend X because Y. Alternatives considered: A (rejected because B), C (rejected because D). Do you agree?"
