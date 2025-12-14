# Power Platform Developer Suite Demo Solution

A reference implementation for Dynamics 365 CE / Dataverse that demonstrates the capabilities of the [Power Platform Developer Suite](https://github.com/joshsmithxrm/power-platform-developer-suite) VS Code extension and the PPDS ecosystem.

## Purpose

This repository serves four main goals:

1. **Showcase Extension Capabilities** - Provides real-world examples of plugins, web resources, cloud flows, and other components that demonstrate what the VS Code extension can do

2. **Reference Architecture / Starter Template** - Clone this repo, replace the demo content, keep the infrastructure (CI/CD, branching, docs) as a starting point for new projects

3. **AI-Assisted Development Reference** - Well-structured patterns in CLAUDE.md that AI coding assistants can use when helping build Power Platform solutions

4. **Educational Example** - Shows complex ALM concepts at a micro level without clutter - one correct example of each component type

## Prerequisites

### Required

- [.NET SDK 6.0+](https://dotnet.microsoft.com/download) (for tooling)
- [.NET Framework 4.6.2 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework) (for plugins)
- [Power Platform CLI (PAC)](https://learn.microsoft.com/en-us/power-platform/developer/cli/introduction) (for solution management)

### For Plugin Development

- **PPDS.Plugins** (NuGet): Plugin step registration attributes
  ```bash
  # Installed via project reference - see src/Plugins/PPDSDemo.Plugins/PPDSDemo.Plugins.csproj
  ```

### For Plugin Deployment

- **PPDS.Tools** (PowerShell): Deployment automation cmdlets
  ```powershell
  Install-Module PPDS.Tools -Scope CurrentUser
  ```

### For Web Resources & PCF

- [Node.js 18+](https://nodejs.org/)

### Optional

- [Visual Studio 2022](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/)

## Quick Start

```bash
# Clone the repository
git clone https://github.com/joshsmithxrm/ppds-demo.git
cd ppds-demo

# Restore NuGet packages
dotnet restore

# Build the plugin projects
dotnet build src/Plugins/PPDSDemo.Plugins/PPDSDemo.Plugins.csproj

# Run tests (when available)
dotnet test

# Pack the solution (requires PAC CLI)
pac solution pack --zipfile solutions/exports/PPDSDemo.zip --folder solutions/PPDSDemo/src
```

## What's Included

| Component | Description |
|-----------|-------------|
| **Plugin Assembly** | Classic IPlugin implementations with PPDS.Plugins attributes |
| **Plugin Package** | Modern NuGet-based plugin package with external dependencies |
| **Custom Workflow Activities** | CodeActivity implementations for classic workflows |
| **Web Resources** | JavaScript/TypeScript, HTML, CSS, and images |
| **Custom Tables** | Demo entities with relationships |
| **Solution** | Unpacked solution using `--packagetype Both` |
| **CI/CD Workflows** | GitHub Actions for export and deployment |

## Project Structure

```
/
├── src/
│   ├── Plugins/                    # Plugin assemblies
│   │   └── PPDSDemo.Plugins/       # Classic plugin assembly
│   ├── PluginPackages/             # Modern plugin packages
│   │   └── PPDSDemo.PluginPackage/ # NuGet-based plugin package
│   └── Shared/
│       └── PPDSDemo.Entities/      # Generated early-bound entities
│
├── solutions/
│   └── PPDSDemo/
│       └── src/                    # Unpacked solution (source control)
│
├── tools/                          # Example deployment scripts
├── .github/                        # CI/CD workflows
└── docs/                           # Documentation
```

## PPDS Ecosystem

This demo solution is part of the Power Platform Developer Suite ecosystem:

| Repository | Purpose | Usage in this Demo |
|------------|---------|-------------------|
| [ppds-sdk](https://github.com/joshsmithxrm/ppds-sdk) | NuGet packages for plugin development | `PPDS.Plugins` package for step registration attributes |
| [ppds-tools](https://github.com/joshsmithxrm/ppds-tools) | PowerShell module for Dataverse operations | Deployment scripts in `tools/` |
| [ppds-alm](https://github.com/joshsmithxrm/ppds-alm) | CI/CD templates for GitHub Actions | Workflow examples in `.github/workflows/` |
| [power-platform-developer-suite](https://github.com/joshsmithxrm/power-platform-developer-suite) | VS Code extension | Development experience |

## Plugin Development

Plugins use attribute-based registration with the PPDS.Plugins package:

```csharp
using PPDS.Plugins;

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
    protected override void ExecutePlugin(LocalPluginContext context)
    {
        // Plugin implementation
    }
}
```

### Deployment Workflow

1. Build: `dotnet build -c Release`
2. Extract: `.\tools\Extract-PluginRegistrations.ps1`
3. Deploy: `.\tools\Deploy-Plugins.ps1`

## CI/CD

This repository includes GitHub Actions workflows for Power Platform ALM:

| Workflow | Trigger | Description |
|----------|---------|-------------|
| **ci-export.yml** | Nightly / Manual | Exports from Dev with noise filtering |
| **cd-qa.yml** | Push to develop | Deploys to QA environment |
| **cd-prod.yml** | Push to main | Deploys managed solution to Production |
| **nightly-export.yml** | Nightly | Example using ppds-alm workflows |
| **deploy-qa.yml** | Push to develop | Example using ppds-alm workflows |
| **deploy-prod.yml** | Push to main | Example using ppds-alm workflows |

See [docs/strategy/PIPELINE_STRATEGY.md](docs/strategy/PIPELINE_STRATEGY.md) for details.

## Documentation

### Strategy (The "Why")
- [ALM Overview](docs/strategy/ALM_OVERVIEW.md) - High-level ALM philosophy
- [Environment Strategy](docs/strategy/ENVIRONMENT_STRATEGY.md) - Dev/QA/Prod configuration
- [Branching Strategy](docs/strategy/BRANCHING_STRATEGY.md) - develop/main workflow
- [Pipeline Strategy](docs/strategy/PIPELINE_STRATEGY.md) - CI/CD approach

### Reference
- [CLAUDE.md](CLAUDE.md) - AI-assistable coding patterns
- [Plugin Components Reference](docs/reference/PLUGIN_COMPONENTS_REFERENCE.md) - Plugin development
- [Solution Structure Reference](docs/reference/SOLUTION_STRUCTURE_REFERENCE.md) - ALM structure
- [Tools Reference](docs/reference/TOOLS_REFERENCE.md) - Deployment scripts

## Contributing

This is primarily a demonstration repository. If you find issues or have suggestions for better patterns, please open an issue.

## License

MIT License - See [LICENSE](LICENSE) for details.
