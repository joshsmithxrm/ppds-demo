# Power Platform CLI (PAC)

The Power Platform CLI (`pac`) is Microsoft's command-line tool for working with Power Platform solutions, environments, and components. It's essential for solution management, deployment, and development workflows.

## Installation

### Option 1: .NET Global Tool (Recommended)

The easiest way to install PAC CLI is as a .NET global tool:

```bash
dotnet tool install --global Microsoft.PowerApps.CLI.Tool
```

**Update to latest version:**
```bash
dotnet tool update --global Microsoft.PowerApps.CLI.Tool
```

**Verify installation:**
```bash
pac --version
```

### Option 2: Standalone Installer

Download the standalone installer from Microsoft:

1. Go to [Power Platform CLI Installation](https://learn.microsoft.com/en-us/power-platform/developer/cli/introduction#install-microsoft-power-platform-cli)
2. Download the Windows MSI installer
3. Run the installer and follow the prompts

### Option 3: VS Code Extension

The Power Platform Tools extension for VS Code includes PAC CLI:

1. Open VS Code
2. Go to Extensions (Ctrl+Shift+X)
3. Search for "Power Platform Tools"
4. Install the extension
5. PAC CLI will be available in the integrated terminal

## Authentication

Before using PAC CLI with Dataverse, you need to authenticate:

### Interactive Browser Authentication

```bash
# Create an authentication profile (--url is required)
pac auth create --name "MyDevEnvironment" --url https://yourorg.crm.dynamics.com

# This opens a browser for Microsoft login
```

### Service Principal Authentication

```bash
pac auth create --name "ServicePrincipal" \
    --url https://yourorg.crm.dynamics.com \
    --applicationId "your-app-id" \
    --clientSecret "your-secret" \
    --tenant "your-tenant-id"
```

### Managing Auth Profiles

```bash
# List all authentication profiles
pac auth list

# Switch to a different profile
pac auth select --name "MyDevEnvironment"

# Delete a profile
pac auth delete --name "OldProfile"

# Clear all profiles
pac auth clear
```

## Common Commands

### Solution Management

```bash
# Export solution from Dataverse
pac solution export --name "MySolution" --path ./exports/MySolution.zip

# Export as managed
pac solution export --name "MySolution" --path ./exports/MySolution_managed.zip --managed

# Import solution to Dataverse
pac solution import --path ./exports/MySolution.zip

# Import managed solution
pac solution import --path ./exports/MySolution_managed.zip --activate-plugins

# Pack solution from source control format
pac solution pack --zipfile ./exports/MySolution.zip --folder ./solutions/MySolution/src

# Unpack solution to source control format
pac solution unpack --zipfile ./exports/MySolution.zip --folder ./solutions/MySolution/src

# Unpack with canvas apps processing
pac solution unpack --zipfile ./exports/MySolution.zip --folder ./solutions/MySolution/src --processCanvasApps
```

### Solution Clone (for new development)

```bash
# Clone a solution to work on locally
pac solution clone --name "MySolution" --outputDirectory ./solutions/MySolution
```

### Environment Management

```bash
# List available environments
pac admin list

# Get environment details
pac org who

# Select an environment
pac org select --environment "https://yourorg.crm.dynamics.com"
```

### Plugin Development

```bash
# Initialize a plugin project
pac plugin init

# Push plugin assembly to Dataverse
pac plugin push --solution "MySolution"
```

### PCF Development

```bash
# Initialize a new PCF control
pac pcf init --namespace "MyNamespace" --name "MyControl" --template field

# Build the control
npm run build

# Start the test harness
npm start watch

# Push PCF control to Dataverse
pac pcf push --publisher-prefix "ppds"
```

### Model Builder (Early Bound Classes)

```bash
# Generate early-bound entity classes
pac modelbuilder build --outdirectory ./Entities

# Generate with specific settings
pac modelbuilder build \
    --outdirectory ./Entities \
    --entitynamesfilter "account;contact;ppds_*" \
    --generateActions
```

### Canvas Apps

```bash
# Unpack a canvas app
pac canvas unpack --msapp ./MyApp.msapp --sources ./src/MyApp

# Pack a canvas app
pac canvas pack --msapp ./MyApp.msapp --sources ./src/MyApp
```

## Configuration Files

### pac CLI Settings

Create a `.pacconfig` file in your project root for default settings:

```json
{
  "defaultEnvironment": "https://yourorg.crm.dynamics.com",
  "solutionFolder": "./solutions/PPDSDemo/src",
  "exportFolder": "./solutions/exports"
}
```

### Solution Pack/Unpack Settings

The `pack` and `unpack` commands support additional options:

```bash
# Unpack with all options
pac solution unpack \
    --zipfile ./MySolution.zip \
    --folder ./src \
    --packagetype Both \
    --allowDelete \
    --allowWrite \
    --clobber \
    --processCanvasApps \
    --useUnmanagedFileForManaged
```

## Troubleshooting

### "pac: command not found"

**Cause:** PAC CLI not in PATH

**Solution:**
```bash
# If installed as dotnet tool, ensure dotnet tools are in PATH
# Add to your shell profile:
export PATH="$PATH:$HOME/.dotnet/tools"

# Or on Windows, restart your terminal after installation
```

### Authentication Errors

**Cause:** Expired or invalid auth profile

**Solution:**
```bash
# Clear and recreate auth
pac auth clear
pac auth create --name "Fresh" --url https://yourorg.crm.dynamics.com
```

### Solution Import Fails

**Cause:** Missing dependencies or version conflicts

**Solution:**
```bash
# Check solution dependencies
pac solution check --path ./MySolution.zip

# Import with verbose logging
pac solution import --path ./MySolution.zip --verbose
```

### "Could not load file or assembly"

**Cause:** Plugin assembly version mismatch

**Solution:**
- Ensure you're targeting .NET Framework 4.6.2
- Check that all dependencies are sandbox-compatible
- Verify assembly is signed (if required)

## Best Practices

1. **Use source control format** - Always unpack solutions for source control, never commit .zip files

2. **Separate managed/unmanaged** - Export both versions for ALM pipelines

3. **Auth profiles per environment** - Create separate profiles for dev, test, prod

4. **Automate with scripts** - Create shell scripts for common operations:

```bash
#!/bin/bash
# export-solution.sh
pac auth select --name "Dev"
pac solution export --name "PPDSDemo" --path ./solutions/exports/PPDSDemo.zip
pac solution unpack --zipfile ./solutions/exports/PPDSDemo.zip --folder ./solutions/PPDSDemo/src --allowDelete --allowWrite --clobber
```

5. **Version your solutions** - Update solution version before export:

```bash
pac solution version --solutionPath ./solutions/PPDSDemo/src --strategy solution --value 1.0.0.1
```

## Resources

- [Official PAC CLI Documentation](https://learn.microsoft.com/en-us/power-platform/developer/cli/introduction)
- [PAC CLI Command Reference](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/)
- [Solution Concepts](https://learn.microsoft.com/en-us/power-platform/alm/solution-concepts-alm)
- [ALM with Power Platform](https://learn.microsoft.com/en-us/power-platform/alm/)
