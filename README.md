# Power Platform Developer Suite Demo Solution

A demonstration solution for Dynamics 365 CE / Dataverse that showcases the capabilities of the [Power Platform Developer Suite](https://github.com/your-org/Power-Platform-Developer-Suite) VS Code extension.

## Purpose

This repository serves three main goals:

1. **Showcase Extension Capabilities** - Provides real-world examples of plugins, web resources, cloud flows, and other components that demonstrate what the VS Code extension can do

2. **Reference Material for AI-Assisted Development** - Contains well-structured, documented patterns that AI coding assistants (like Claude) can use as reference when helping developers build Power Platform solutions

3. **Best Practices Documentation** - Models proper solution structure, naming conventions, and architectural patterns for Dynamics 365 development

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
git clone https://github.com/your-org/Power-Platform-Developer-Suite-Demo-Solution.git
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

- [PAC CLI Setup](docs/tools/pac-cli.md) - Installing and configuring Power Platform CLI
- [Plugin Development](docs/development/plugins.md) - Plugin patterns and best practices
- [Web Resources](docs/development/web-resources.md) - JavaScript/TypeScript patterns
- [Solution Management](docs/development/solutions.md) - Working with solutions

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

- [Power Platform Developer Suite](https://github.com/your-org/Power-Platform-Developer-Suite) - The VS Code extension this demo showcases
