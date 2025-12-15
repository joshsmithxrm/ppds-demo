# Composite Actions Reference

This document provides complete reference documentation for all custom GitHub Actions in `.github/actions/`.

---

## Overview

| Action | Purpose | Prerequisites |
|--------|---------|---------------|
| [setup-pac-cli](#setup-pac-cli) | Install .NET SDK and PAC CLI | None |
| [pac-auth](#pac-auth) | Authenticate to Dataverse environment | setup-pac-cli |
| [export-solution](#export-solution) | Export and unpack solution from environment | pac-auth |
| [pack-solution](#pack-solution) | Pack solution from unpacked source | setup-pac-cli |
| [import-solution](#import-solution) | Import solution with version check and retry | pac-auth |
| [build-solution](#build-solution) | Build .NET solution and locate outputs | None |
| [copy-plugin-assemblies](#copy-plugin-assemblies) | Copy classic plugin DLLs to solution | build-solution |
| [copy-plugin-packages](#copy-plugin-packages) | Copy plugin packages to solution | build-solution |
| [check-solution](#check-solution) | Run Solution Checker validation | pac-auth (optional) |
| [analyze-changes](#analyze-changes) | Filter noise from solution exports | Git repo with staged changes |

---

## setup-pac-cli

Installs .NET SDK and Power Platform CLI for use in workflows.

### Inputs

| Name | Required | Default | Description |
|------|----------|---------|-------------|
| `dotnet-version` | No | `8.x` | .NET SDK version to install |
| `pac-version` | No | *(latest)* | PAC CLI version (empty = latest) |

### Outputs

| Name | Description |
|------|-------------|
| `pac-version` | Installed PAC CLI version |

### Usage

```yaml
- name: Setup PAC CLI
  uses: ./.github/actions/setup-pac-cli

# Pin to specific version for reproducibility
- name: Setup PAC CLI (pinned)
  uses: ./.github/actions/setup-pac-cli
  with:
    pac-version: '1.35.5'
```

### Notes

- Find available versions at: https://www.nuget.org/packages/Microsoft.PowerApps.CLI.Tool
- Pin versions in production workflows for stability

---

## pac-auth

Authenticates to a Power Platform environment using service principal credentials.

### Inputs

| Name | Required | Default | Description |
|------|----------|---------|-------------|
| `environment-url` | **Yes** | - | Dataverse URL (e.g., `https://org.crm.dynamics.com/`) |
| `tenant-id` | **Yes** | - | Azure AD tenant ID |
| `client-id` | **Yes** | - | Service principal application (client) ID |
| `client-secret` | **Yes** | - | Service principal client secret |
| `name` | No | `default` | Auth profile name (for multiple connections) |

### Outputs

| Name | Description |
|------|-------------|
| `environment-id` | Connected environment ID |
| `user-id` | Connected user/application ID |

### Usage

```yaml
- name: Authenticate to environment
  uses: ./.github/actions/pac-auth
  with:
    environment-url: ${{ vars.POWERPLATFORM_ENVIRONMENT_URL }}
    tenant-id: ${{ vars.POWERPLATFORM_TENANT_ID }}
    client-id: ${{ vars.POWERPLATFORM_CLIENT_ID }}
    client-secret: ${{ secrets.POWERPLATFORM_CLIENT_SECRET }}
```

### Notes

- Creates an auth profile that persists for subsequent PAC CLI commands
- Verifies connection with `pac org who` command
- Supports both JSON and text output parsing for compatibility

---

## export-solution

Exports and unpacks a Power Platform solution from the authenticated environment.

### Inputs

| Name | Required | Default | Description |
|------|----------|---------|-------------|
| `solution-name` | **Yes** | - | Solution unique name to export |
| `output-folder` | **Yes** | - | Folder for unpacked solution |
| `temp-folder` | No | `./exports` | Temporary folder for zip files |

### Outputs

| Name | Description |
|------|-------------|
| `solution-folder` | Path to unpacked solution folder |

### Usage

```yaml
- name: Export solution
  uses: ./.github/actions/export-solution
  with:
    solution-name: PPDSDemo
    output-folder: solutions/PPDSDemo/src
```

### Process

1. Publishes all customizations (`pac solution publish`)
2. Exports unmanaged solution
3. Exports managed solution
4. Unpacks with `--packagetype Both` (single source, builds either type)
5. Cleans up temporary zip files

### Notes

- Uses `--packagetype Both` for unified source that builds managed or unmanaged
- Includes `--processCanvasApps` for canvas app unpacking
- Uses `--allowDelete` and `--allowWrite` for clean updates

---

## pack-solution

Packs a Power Platform solution from unpacked source files.

### Inputs

| Name | Required | Default | Description |
|------|----------|---------|-------------|
| `solution-folder` | **Yes** | - | Path to unpacked solution folder |
| `solution-name` | **Yes** | - | Solution name (used in output filename) |
| `output-folder` | No | `./exports` | Output folder for packed zip |
| `package-type` | No | `Managed` | Package type: `Managed`, `Unmanaged`, or `Both` |

### Outputs

| Name | Description |
|------|-------------|
| `solution-path` | Full path to packed solution zip file |

### Usage

```yaml
- name: Pack managed solution
  id: pack
  uses: ./.github/actions/pack-solution
  with:
    solution-folder: solutions/PPDSDemo/src
    solution-name: PPDSDemo
    package-type: Managed

- name: Use packed solution
  run: echo "Packed to ${{ steps.pack.outputs.solution-path }}"
```

### Output Filename

Format: `{solution-name}_{package-type}.zip`
- Example: `PPDSDemo_managed.zip`

---

## import-solution

Imports a Power Platform solution with enterprise-grade features including version comparison, smart retry logic, and deployment settings support.

### Inputs

| Name | Required | Default | Description |
|------|----------|---------|-------------|
| `solution-path` | **Yes** | - | Path to solution zip file |
| `solution-name` | No | - | Solution unique name (required for version check) |
| `force-overwrite` | No | `true` | Force overwrite if solution exists |
| `publish-changes` | No | `true` | Publish customizations after import |
| `async` | No | `true` | Use async import (recommended for large solutions) |
| `cleanup` | No | `true` | Delete solution zip after import |
| `skip-if-same-version` | No | `true` | Skip if target has same or newer version |
| `max-retries` | No | `3` | Maximum retry attempts for transient failures |
| `retry-delay-seconds` | No | `300` | Delay between retries (5 minutes) |
| `settings-file` | No | - | Path to deployment settings JSON file |

### Outputs

| Name | Description |
|------|-------------|
| `imported` | Whether the solution was imported (`true`/`false`) |
| `skipped` | Whether import was skipped due to version match |
| `import-version` | Version of the solution being imported |
| `target-version` | Version currently in target environment |
| `retry-count` | Number of retry attempts made |

### Usage

```yaml
- name: Import solution
  uses: ./.github/actions/import-solution
  with:
    solution-path: ./exports/PPDSDemo_managed.zip
    solution-name: PPDSDemo
    skip-if-same-version: 'true'
    max-retries: '3'
    settings-file: ./config/prod.deploymentsettings.json
```

### Version Comparison

Compares import version against target environment:
- If target >= import: Skips import (idempotent)
- If target < import: Proceeds with import
- If target not found: New installation

### Retry Logic

**Transient errors (will retry):**
- "Cannot start another solution" - Concurrent import conflict
- "try again later" - Service temporarily unavailable

**Deterministic errors (immediate failure):**
- "File not found", "does not exist"
- "missing dependency", "Missing component"
- "cannot be deleted", "cannot be updated"
- "access denied", "unauthorized"

### Re-check on Retry

Before each retry, re-checks target version. If another process completed the import, skips retry and reports success.

---

## build-solution

Builds the .NET solution containing plugins, workflow activities, and custom APIs.

### Inputs

| Name | Required | Default | Description |
|------|----------|---------|-------------|
| `solution-path` | No | `PPDSDemo.sln` | Path to .NET solution file |
| `configuration` | No | `Release` | Build configuration (`Debug` or `Release`) |
| `dotnet-version` | No | `8.x` | .NET SDK version to use |
| `run-tests` | No | `false` | Run unit tests after build |
| `test-filter` | No | - | Test filter expression |

### Outputs

| Name | Description |
|------|-------------|
| `classic-assembly-path` | Path to classic plugin assembly DLL |
| `plugin-package-path` | Path to plugin package NuGet file |
| `entities-assembly-path` | Path to entities assembly DLL |
| `build-succeeded` | Whether build completed successfully |
| `test-succeeded` | Whether tests passed (empty if not run) |

### Usage

```yaml
- name: Build .NET solution
  id: build
  uses: ./.github/actions/build-solution
  with:
    solution-path: PPDSDemo.sln
    configuration: Release
    run-tests: 'true'

- name: Use build outputs
  run: |
    echo "Plugin DLL: ${{ steps.build.outputs.classic-assembly-path }}"
    echo "Plugin Package: ${{ steps.build.outputs.plugin-package-path }}"
```

### Output Location Discovery

Automatically finds build outputs:
- Classic plugins: `src/Plugins/**/bin/{Config}/*.dll`
- Plugin packages: `src/PluginPackages/**/bin/{Config}/*.nupkg`
- Entities: `src/Shared/**/bin/{Config}/*.Entities.dll`

---

## copy-plugin-assemblies

Copies built plugin assemblies to the solution's PluginAssemblies folder for packing.

### Inputs

| Name | Required | Default | Description |
|------|----------|---------|-------------|
| `source-assembly` | **Yes** | - | Path to built plugin assembly DLL |
| `solution-folder` | **Yes** | - | Base path to unpacked solution |

### Outputs

| Name | Description |
|------|-------------|
| `copied-count` | Number of locations where assembly was copied |
| `target-path` | Path where assembly was copied |

### Usage

```yaml
- name: Copy plugin assemblies
  uses: ./.github/actions/copy-plugin-assemblies
  with:
    source-assembly: ${{ steps.build.outputs.classic-assembly-path }}
    solution-folder: solutions/PPDSDemo/src
```

### Naming Convention

PAC CLI removes dots from assembly names:
- Build output: `PPDSDemo.Plugins.dll`
- Solution expects: `PPDSDemoPlugins.dll`
- Target folder: `PluginAssemblies/PPDSDemoPlugins-{GUID}/`

### Validation

Fails immediately if:
- Source assembly not found
- Solution folder not found
- Target folder pattern not matched
- Copy operation fails

---

## copy-plugin-packages

Copies built plugin packages (.nupkg) to the solution's pluginpackages folder for packing.

### Inputs

| Name | Required | Default | Description |
|------|----------|---------|-------------|
| `source-package` | **Yes** | - | Path to built plugin package (.nupkg) |
| `solution-folder` | **Yes** | - | Base path to unpacked solution |

### Outputs

| Name | Description |
|------|-------------|
| `copied-count` | Number of locations where package was copied |
| `target-path` | Path where package was copied |
| `package-id` | Extracted package ID (without version) |

### Usage

```yaml
- name: Copy plugin packages
  uses: ./.github/actions/copy-plugin-packages
  with:
    source-package: ${{ steps.build.outputs.plugin-package-path }}
    solution-folder: solutions/PPDSDemo/src
```

### Naming Convention

Strips version from package name:
- Build output: `ppds_PPDSDemo.PluginPackage.1.0.0.nupkg`
- Solution expects: `ppds_PPDSDemo.PluginPackage.nupkg`
- Target folder: `pluginpackages/ppds_PPDSDemo.PluginPackage/package/`

---

## check-solution

Runs the PowerApps Solution Checker to validate solution quality.

### Inputs

| Name | Required | Default | Description |
|------|----------|---------|-------------|
| `solution-path` | **Yes** | - | Path to solution zip file |
| `fail-on-level` | No | `High` | Fail threshold: `Critical`, `High`, `Medium`, `Low`, `Informational` |
| `geography` | No | `unitedstates` | Geography for Solution Checker service |
| `output-directory` | No | `./checker-results` | Directory for checker output files |
| `rule-level-override` | No | - | Path to rule level override file |

### Outputs

| Name | Description |
|------|-------------|
| `passed` | Whether the solution passed the threshold check |
| `critical-count` | Number of critical issues found |
| `high-count` | Number of high severity issues found |
| `medium-count` | Number of medium severity issues found |
| `low-count` | Number of low severity issues found |
| `informational-count` | Number of informational issues found |
| `total-count` | Total number of issues found |
| `results-file` | Path to the SARIF results file |

### Usage

```yaml
- name: Check solution quality
  uses: ./.github/actions/check-solution
  with:
    solution-path: ./exports/PPDSDemo_managed.zip
    fail-on-level: High
    geography: unitedstates
```

### Severity Levels

| Level | Description |
|-------|-------------|
| Critical | Blocking issues that will cause failures |
| High | Serious issues that should be addressed |
| Medium | Moderate issues worth reviewing |
| Low | Minor issues and best practice suggestions |
| Informational | Non-issues, just FYI |

### Available Geographies

`unitedstates`, `europe`, `asia`, `australia`, `japan`, `india`, `canada`, `southamerica`, `uk`, `france`, `uae`, `germany`, `switzerland`, `norway`, `korea`, `southafrica`

---

## analyze-changes

Analyzes git changes to filter out "noise" from Power Platform solution exports.

### Inputs

| Name | Required | Default | Description |
|------|----------|---------|-------------|
| `solution-folder` | **Yes** | - | Path to the unpacked solution folder |
| `debug` | No | `false` | Enable verbose debug logging |

### Outputs

| Name | Description |
|------|-------------|
| `has-real-changes` | Whether real (non-noise) changes were detected |
| `change-summary` | Human-readable summary of changes |
| `noise-count` | Number of noise changes filtered out |
| `real-count` | Number of real changes detected |
| `real-files` | List of files with real changes (newline-separated) |

### Usage

```yaml
# Stage changes first
- run: git add -A

- name: Analyze changes for noise
  id: analyze
  uses: ./.github/actions/analyze-changes
  with:
    solution-folder: solutions/PPDSDemo/src
    debug: 'false'

- name: Check results
  run: |
    echo "Has real changes: ${{ steps.analyze.outputs.has-real-changes }}"
    echo "Noise filtered: ${{ steps.analyze.outputs.noise-count }}"
```

### Noise Patterns Filtered

| Pattern | Description |
|---------|-------------|
| Solution.xml version-only | Only `<Version>` tag changed |
| Canvas App volatile URIs | DocumentUri/BackgroundImageUri random suffixes |
| Workflow session IDs | workflowName changes (workflowEntityId unchanged) |
| Whitespace-only | No content changes, just formatting |
| R100 renames | 100% identical content, filename changed |
| Dependency version refs | MissingDependency/schemaVersion updates only |

### Prerequisites

- Changes must be staged (`git add -A`) before calling
- Must run from within a git repository

---

## Typical Workflow Usage

### CI: Build and Pack

```yaml
steps:
  - uses: actions/checkout@v4

  - name: Setup PAC CLI
    uses: ./.github/actions/setup-pac-cli

  - name: Build .NET solution
    id: build
    uses: ./.github/actions/build-solution
    with:
      run-tests: 'true'

  - name: Copy plugin assemblies
    uses: ./.github/actions/copy-plugin-assemblies
    with:
      source-assembly: ${{ steps.build.outputs.classic-assembly-path }}
      solution-folder: solutions/PPDSDemo/src

  - name: Pack solution
    id: pack
    uses: ./.github/actions/pack-solution
    with:
      solution-folder: solutions/PPDSDemo/src
      solution-name: PPDSDemo
      package-type: Managed
```

### CD: Deploy to Environment

```yaml
steps:
  - uses: actions/checkout@v4

  - name: Setup PAC CLI
    uses: ./.github/actions/setup-pac-cli

  - name: Authenticate
    uses: ./.github/actions/pac-auth
    with:
      environment-url: ${{ vars.POWERPLATFORM_ENVIRONMENT_URL }}
      tenant-id: ${{ vars.POWERPLATFORM_TENANT_ID }}
      client-id: ${{ vars.POWERPLATFORM_CLIENT_ID }}
      client-secret: ${{ secrets.POWERPLATFORM_CLIENT_SECRET }}

  - name: Import solution
    uses: ./.github/actions/import-solution
    with:
      solution-path: ./artifact/PPDSDemo_managed.zip
      solution-name: PPDSDemo
      settings-file: ./config/qa.deploymentsettings.json
```

---

## See Also

- [PIPELINE_STRATEGY.md](../strategy/PIPELINE_STRATEGY.md) - Workflow architecture
- [PAC CLI Reference](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/) - Official PAC CLI docs
