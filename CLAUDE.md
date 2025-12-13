# CLAUDE.md - Quick Reference

**Essential rules for AI assistants working on this Dynamics 365 / Dataverse demo solution.**

This repository is a **demonstration solution** for the [Power Platform Developer Suite](https://github.com/your-org/Power-Platform-Developer-Suite) VS Code extension. It showcases plugins, web resources, cloud flows, custom APIs, and ALM patterns.

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

---

## ALWAYS (Required Patterns)

1. **ITracingService for debugging** - All plugin trace output via tracing service
2. **try/catch with InvalidPluginExecutionException** - Proper error handling
3. **Check Target existence** - Validate `InputParameters.Contains("Target")`
4. **formContext from executionContext** - Store and use throughout form scripts
5. **Namespace pattern in JS** - Avoid global pollution (`PPDSDemo.Account`)
6. **Export both managed AND unmanaged** - Then unpack with `--packagetype Both`
7. **Deployment settings per environment** - `config/qa.deploymentsettings.json`

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
| Plugins | .NET Framework 4.6.2, Microsoft.CrmSdk.CoreAssemblies |
| Workflows | Microsoft.CrmSdk.Workflow |
| Web Resources | TypeScript/JavaScript, Xrm SDK |
| PCF Controls | TypeScript, React (optional), Fluent UI |
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

# Run tests
dotnet test tests/PPDSDemo.Plugins.Tests/

# Build solution (unmanaged for dev)
dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Debug

# Build solution (managed for deployment)
dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Release

# Export from environment (both managed and unmanaged)
pac solution export --name PPDSDemo --path exports --managed false --overwrite
pac solution export --name PPDSDemo --path exports --managed true --overwrite

# Unpack with packagetype Both
pac solution unpack --zipfile exports/PPDSDemo.zip --folder solutions/PPDSDemo/src --packagetype Both --allowDelete --allowWrite
```

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

## Git Conventions

**Branch strategy:** Feature branches, squash merge to main

**Commit messages:**
```
feat: add account validation plugin
fix: correct status transition logic
docs: update plugin patterns
refactor: extract discount calculation service
```

**No AI attribution** in commits or PR descriptions.

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
