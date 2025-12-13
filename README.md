# Power Platform Developer Suite Demo Solution

A demonstration solution for Dynamics 365 CE / Dataverse that showcases the capabilities of the [Power Platform Developer Suite](https://github.com/JoshSmithXRM/Power-Platform-Developer-Suite) VS Code extension.

## Purpose

This repository serves four main goals:

1. **Showcase Extension Capabilities** - Provides real-world examples of plugins, web resources, cloud flows, and other components that demonstrate what the VS Code extension can do

2. **Reference Architecture / Starter Template** - Clone this repo, replace the demo content, keep the infrastructure (CI/CD, branching, docs) as a starting point for new projects

3. **AI-Assisted Development Reference** - Well-structured patterns in CLAUDE.md that AI coding assistants can use when helping build Power Platform solutions

4. **Educational Example** - Shows complex ALM concepts at a micro level without clutter - one correct example of each component type

## What's Included

| Component | Description |
|-----------|-------------|
| **Plugin Assembly** | Classic IPlugin implementations with proper patterns |
| **Plugin Package** | Modern NuGet-based plugin package with dependencies |
| **Custom Workflow Activities** | CodeActivity implementations for classic workflows |
| **Custom APIs** | Modern extensibility pattern for custom actions |
| **Web Resources** | JavaScript/TypeScript, HTML, CSS, and images |
| **PCF Controls** | PowerApps Component Framework controls |
| **Cloud Flows** | Solution-aware Power Automate flows |
| **Custom Tables** | Demo entities with relationships |
| **Environment Variables** | Configuration management examples |
| **Connection References** | Flow connection dependencies |
| **Security Roles** | Custom role definitions |

## Getting Started

### Prerequisites

- [.NET SDK 6.0+](https://dotnet.microsoft.com/download) (for tooling)
- [.NET Framework 4.6.2 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework) (for plugins)
- [Node.js 18+](https://nodejs.org/) (for web resources and PCF)
- [Power Platform CLI (PAC)](docs/tools/pac-cli.md) (for solution management)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/)

### Quick Start

```bash
# Clone the repository
git clone https://github.com/JoshSmithXRM/Power-Platform-Developer-Suite-Demo-Solution.git
cd Power-Platform-Developer-Suite-Demo-Solution

# Build the plugin projects
dotnet build src/Plugins/PPDSDemo.Plugins/PPDSDemo.Plugins.csproj

# Run tests
dotnet test

# Pack the solution (requires PAC CLI)
pac solution pack --zipfile solutions/exports/PPDSDemo.zip --folder solutions/PPDSDemo/src
```

## Project Structure

```
/
├── src/
│   ├── Plugins/                    # Plugin assemblies
│   ├── PluginPackages/             # Modern plugin packages
│   ├── WorkflowActivities/         # Custom workflow activities
│   ├── CustomAPIs/                 # Custom API implementations
│   ├── PCF/                        # PCF controls
│   └── WebResources/               # JS, HTML, CSS, images
│
├── solutions/
│   ├── PPDSDemo/                   # Unpacked solution (source control)
│   └── exports/                    # Exported solution zips
│
├── tests/                          # Unit and integration tests
├── tools/                          # Build and deployment scripts
└── docs/                           # Documentation
```

## Documentation

### Strategy (The "Why")
- [ALM Overview](docs/strategy/ALM_OVERVIEW.md) - High-level ALM philosophy
- [Environment Strategy](docs/strategy/ENVIRONMENT_STRATEGY.md) - Dev/QA/Prod configuration
- [Branching Strategy](docs/strategy/BRANCHING_STRATEGY.md) - develop/main workflow
- [Pipeline Strategy](docs/strategy/PIPELINE_STRATEGY.md) - CI/CD approach

### Reference
- [CLAUDE.md](CLAUDE.md) - AI-assistable coding patterns
- [PAC CLI Reference](docs/reference/PAC_CLI_REFERENCE.md) - Common PAC CLI commands
- [Roadmap](docs/ROADMAP.md) - What's complete vs. in progress

## CI/CD

This repository includes GitHub Actions workflows for Power Platform ALM:

| Workflow | Trigger | Description |
|----------|---------|-------------|
| **Export Solution** | Nightly / Manual | Exports from Dev, deploys to QA |
| **Deploy to Prod** | Push to main | Deploys managed solution to Production |

**Branch Flow:**
- `develop` branch deploys to QA environment
- `main` branch deploys to Production environment

See [Pipeline Strategy](docs/strategy/PIPELINE_STRATEGY.md) for details.

## Extension Integration

This demo is designed to work with the Power Platform Developer Suite VS Code extension:

| Extension Feature | What to Try |
|-------------------|-------------|
| **Plugin Trace Viewer** | Execute plugins and view traces |
| **Metadata Browser** | Explore custom tables and relationships |
| **Web Resources Manager** | Edit and publish web resources |
| **Data Explorer** | Query custom tables with FetchXML/SQL |
| **Solutions Explorer** | Browse solution components |

## Contributing

This is primarily a demonstration repository. If you find issues or have suggestions for better patterns, please open an issue.

## License

MIT License - See [LICENSE](LICENSE) for details.

## Related Projects

- [Power Platform Developer Suite](https://github.com/JoshSmithXRM/Power-Platform-Developer-Suite) - The VS Code extension this demo showcases
