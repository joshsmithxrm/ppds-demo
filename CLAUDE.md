# CLAUDE.md - Quick Reference

**Essential rules for AI assistants working on this Dynamics 365 / Dataverse demo solution.**

This repository is a **demonstration solution** for the [Power Platform Developer Suite](https://github.com/joshsmithxrm/power-platform-developer-suite) VS Code extension. It showcases plugins, web resources, cloud flows, custom APIs, and ALM patterns.

This is part of the **PPDS ecosystem**:
- **PPDS.Plugins** (NuGet) - Plugin step registration attributes
- **PPDS.Tools** (PowerShell) - Deployment automation cmdlets
- **ppds-alm** (GitHub) - CI/CD workflow templates

---

## Solution Context

- **Solution Name:** Power Platform Developer Suite Demo
- **Publisher Prefix:** `ppds`
- **Schema Prefix:** `ppds_`
- **Purpose:** Educational/demo, not production code

---

## NEVER (Non-Negotiable)

1. **Console.WriteLine in plugins** - Use `ITracingService` (sandbox restriction)
2. **Hardcoded GUIDs** - Use configuration or environment variables
3. **Xrm.Page** - Deprecated; use `formContext` from execution context
4. **alert() in web resources** - Use `Xrm.Navigation.openAlertDialog`
5. **Static state in plugins** - Sandbox recycles; no static variables
6. **External assemblies** - Only sandbox-allowed assemblies in plugins
7. **Separate Managed/Unmanaged folders** - Use `--packagetype Both` unified source
8. **PR directly to main** - Always target `develop` first
9. **AI attribution in commits** - No co-author tags, no "Generated with Claude"
10. **Squash merge develop to main** - Use regular merge to preserve feature commits

---

## ALWAYS (Required Patterns)

1. **ITracingService for debugging** - All plugin trace output via tracing service
2. **try/catch with InvalidPluginExecutionException** - Proper error handling
3. **Check Target existence** - Validate `InputParameters.Contains("Target")`
4. **formContext from executionContext** - Store and use throughout form scripts
5. **Namespace pattern in JS** - Avoid global pollution (`PPDSDemo.Account`)
6. **Export both managed AND unmanaged** - Then unpack with `--packagetype Both`
7. **Deployment settings per environment** - `config/qa.deploymentsettings.json`
8. **Target `develop` for feature PRs** - Never PR directly to main
9. **Squash merge to `develop`** - Clean feature commits
10. **Regular merge `develop` to `main`** - Preserve feature history for releases

---

## Shell Commands & Tools

1. **NEVER use `cd`** - Use absolute paths or tool path parameters
2. **Use Grep tool** - NOT `bash grep` or `bash rg`
3. **Use Glob tool** - NOT `bash find` or `bash ls` for file searches
4. **Use Read tool** - NOT `bash cat`, `bash head`, or `bash tail`

```
# BAD - triggers permission prompts, harder to read
cd C:/VS/ppds/demo && grep -r "pattern" --include="*.cs"

# GOOD - use Grep tool with path parameter
Grep(pattern="pattern", path="C:/VS/ppds/demo", glob="*.cs")
```

---

## Solution Structure (Packagetype Both)

Solutions use unified source control via `pac solution unpack --packagetype Both`:

```
solutions/PPDSDemo/
├── PPDSDemo.cdsproj          # MSBuild project (Debug=Unmanaged, Release=Managed)
├── config/
│   ├── qa.deploymentsettings.json
│   └── prod.deploymentsettings.json
└── src/                      # Flat structure (NO Managed/Unmanaged subfolders)
    ├── Other/Solution.xml
    ├── Entities/
    ├── OptionSets/
    └── WebResources/
```

**Build outputs:**
- `dotnet build -c Debug` → Unmanaged solution (for dev import)
- `dotnet build -c Release` → Managed solution (for QA/Prod deployment)

See [SOLUTION_STRUCTURE_REFERENCE.md](docs/reference/SOLUTION_STRUCTURE_REFERENCE.md) for details.

---

## Tech Stack

| Area | Technology |
|------|------------|
| Plugins | .NET Framework 4.6.2, Microsoft.CrmSdk.CoreAssemblies, **PPDS.Plugins** (NuGet) |
| Workflows | Microsoft.CrmSdk.Workflow |
| Web Resources | TypeScript/JavaScript, Xrm SDK |
| PCF Controls | TypeScript, React (optional), Fluent UI |
| Deployment | **PPDS.Tools** (PowerShell module: `Install-Module PPDS.Tools`) |
| Testing | FakeXrmEasy, MSTest |

---

## Naming Conventions

| Component | Convention | Example |
|-----------|------------|---------|
| Tables | `ppds_PascalCase` | `ppds_DemoRecord` |
| Columns | `ppds_camelcase` | `ppds_customfield` |
| Option Sets | `ppds_PascalCase` | `ppds_Status` |
| Web Resources | `ppds_/path/name.ext` | `ppds_/scripts/account.form.js` |
| Plugin Classes | `{Entity}{Stage}{Message}Plugin` | `AccountPreCreatePlugin` |

---

## Common Commands

```bash
# Build plugins
dotnet build src/Plugins/PPDSDemo.Plugins/PPDSDemo.Plugins.csproj

# Build solution (unmanaged for dev)
dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Debug

# Build solution (managed for deployment)
dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Release

# Export from environment (both managed and unmanaged)
pac solution export --name PPDSDemo --path exports --managed false --overwrite
pac solution export --name PPDSDemo --path exports --managed true --overwrite

# Unpack with packagetype Both
pac solution unpack --zipfile exports/PPDSDemo.zip --folder solutions/PPDSDemo/src --packagetype Both --allowDelete --allowWrite

# Extract plugin registrations from compiled assemblies
.\tools\Extract-PluginRegistrations.ps1

# Deploy plugins to Dev environment
.\tools\Deploy-Plugins.ps1 -Environment Dev

# Deploy with dry run
.\tools\Deploy-Plugins.ps1 -WhatIf
```

---

## Plugin Deployment

Plugins use attribute-based registration with automated deployment tooling.

### Step Registration Attributes

```csharp
using PPDS.Plugins;  // NuGet package for attributes

[PluginStep(
    Message = "Update",
    EntityLogicalName = "account",
    Stage = PluginStage.PostOperation,
    Mode = PluginMode.Asynchronous,
    FilteringAttributes = "name,telephone1")]
[PluginImage(
    ImageType = PluginImageType.PreImage,
    Name = "PreImage",
    Attributes = "name,telephone1")]
public class AccountAuditPlugin : PluginBase
{
    // Plugin implementation
}
```

### Deployment Workflow

1. Add `[PluginStep]` and `[PluginImage]` attributes to plugin classes
2. Build: `dotnet build -c Release`
3. Extract: `.\tools\Extract-PluginRegistrations.ps1`
4. Deploy: `.\tools\Deploy-Plugins.ps1`

The `registrations.json` files are committed to source control for review and documentation.

---

## Thinking Modes

For complex/uncertain problems:

| Situation | Mode |
|-----------|------|
| Plugin registration strategy | `think` |
| Complex entity relationships | `think` |
| Solution layering decisions | `think hard` |
| Cross-cutting concerns | `think harder` |

---

## Git Workflow

**Branch Model:** GitFlow-lite with `main` and `develop`

| Flow | Merge Strategy | Why |
|------|----------------|-----|
| `feature/*` → `develop` | Squash | Clean history, one commit per feature |
| `develop` → `main` | Regular merge | Preserve features, clear release boundaries |

**PR Targets:**
- Feature/fix branches → `develop`
- Release PRs → `main` (from develop only)
- Hotfixes → `main` (then cherry-pick to develop)

**Commit messages:** Conventional commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`)

See [BRANCHING_STRATEGY.md](docs/strategy/BRANCHING_STRATEGY.md) for complete details.

---

## References

### Detailed Patterns (in this repo)
- [docs/reference/PLUGIN_COMPONENTS_REFERENCE.md](docs/reference/PLUGIN_COMPONENTS_REFERENCE.md) - Plugin development
- [docs/reference/WEBRESOURCE_PATTERNS.md](docs/reference/WEBRESOURCE_PATTERNS.md) - Web resource patterns
- [docs/reference/TESTING_PATTERNS.md](docs/reference/TESTING_PATTERNS.md) - Unit testing
- [docs/reference/SOLUTION_STRUCTURE_REFERENCE.md](docs/reference/SOLUTION_STRUCTURE_REFERENCE.md) - ALM structure
- [docs/reference/DOCUMENTATION_STANDARDS.md](docs/reference/DOCUMENTATION_STANDARDS.md) - Doc conventions

### Strategy & Guides (in this repo)
- [docs/strategy/ALM_OVERVIEW.md](docs/strategy/ALM_OVERVIEW.md) - ALM philosophy
- [docs/strategy/PIPELINE_STRATEGY.md](docs/strategy/PIPELINE_STRATEGY.md) - CI/CD approach
- [docs/guides/GETTING_STARTED_GUIDE.md](docs/guides/GETTING_STARTED_GUIDE.md) - Setup instructions

### Microsoft Documentation
- [Power Platform Documentation](https://learn.microsoft.com/en-us/power-platform/)
- [Dataverse Developer Guide](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/)
- [Plugin Development](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/plug-ins)
- [Web Resources](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/web-resources)
- [Solution Packager](https://learn.microsoft.com/en-us/power-platform/alm/solution-packager-tool)

---

**Remember:** This is a demo/reference solution. Prioritize clarity and showcasing patterns over production-level complexity.
