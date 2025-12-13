# Solution Structure Reference

Standards for Power Platform solution folder structure in source control.

---

## Core Principle

**Export BOTH managed and unmanaged, unpack with `--packagetype Both` to create unified source.**

Per [Microsoft Solution Packager documentation](https://learn.microsoft.com/en-us/power-platform/alm/solution-packager-tool):
- Export both managed and unmanaged solutions from your dev environment
- Unpack with `--packagetype Both` to merge into a single folder
- This single source can build **either** managed or unmanaged solutions
- Debug config → Unmanaged, Release config → Managed

---

## Correct Folder Structure

```
solutions/
└── PPDSDemo/
    ├── PPDSDemo.cdsproj              # MSBuild project file
    ├── .gitignore                    # Excludes bin/, obj/, *.zip
    ├── version.txt                   # Solution version tracking
    ├── config/                       # Deployment settings per environment
    │   ├── dev.deploymentsettings.json
    │   ├── qa.deploymentsettings.json
    │   └── prod.deploymentsettings.json
    └── src/                          # Unpacked solution (packagetype Both)
        ├── Other/
        │   ├── Solution.xml          # Solution manifest
        │   ├── Customizations.xml    # Solution customizations
        │   └── Relationships/        # Entity relationships
        ├── Entities/                 # Tables
        │   └── ppds_tablename/
        │       ├── Entity.xml
        │       ├── FormXml/
        │       ├── SavedQueries/
        │       └── RibbonDiff.xml
        ├── OptionSets/               # Global option sets
        │   └── ppds_optionsetname.xml
        ├── WebResources/             # JavaScript, HTML, CSS, images
        │   └── ppds_/
        │       └── scripts/
        ├── PluginAssemblies/         # Plugin DLLs (appears after registration)
        │   └── AssemblyName-GUID/
        │       ├── AssemblyName.dll
        │       └── AssemblyName.dll.data.xml
        ├── SdkMessageProcessingSteps/  # Plugin steps (appears after registration)
        │   └── {step-guid}.xml
        ├── pluginpackages/           # Plugin packages (appears after registration)
        │   └── PackageName-GUID/
        └── environmentvariabledefinitions/  # Environment variables
            └── ppds_variablename/
                └── environmentvariabledefinition.xml
```

**Key points:**
- No `Managed/` or `Unmanaged/` subfolders - the `--packagetype Both` merge creates a flat structure
- Plugin folders (`PluginAssemblies/`, `SdkMessageProcessingSteps/`, `pluginpackages/`) only appear after plugins are registered in the solution

---

## Anti-Patterns (DO NOT DO)

### Managed/Unmanaged Subfolders

```
src/
├── Managed/      ← WRONG - this structure is obsolete
├── Unmanaged/    ← WRONG - use packagetype Both instead
└── Other/
```

This happens when you export managed and unmanaged separately and unpack to different folders. The correct approach is to unpack with `--packagetype Both` which merges them.

### Unmanaged-Only Source

```
src/
└── Other/
    └── Solution.xml  ← Contains <Managed>0</Managed>
```

If you only export unmanaged, you can only build unmanaged. You cannot build managed from unmanaged-only source (the build will fail).

---

## PAC CLI Commands

### Export Solution (from Dataverse)

```bash
# Export BOTH unmanaged and managed (same folder, specific naming)
pac solution export --name PPDSDemo --path solutions/exports --managed false --overwrite
pac solution export --name PPDSDemo --path solutions/exports --managed true --overwrite

# This creates:
# - solutions/exports/PPDSDemo.zip (unmanaged)
# - solutions/exports/PPDSDemo_managed.zip (managed)
```

### Unpack Solution (to source control)

```bash
# Unpack with packagetype Both - merges managed and unmanaged into single folder
pac solution unpack \
    --zipfile solutions/exports/PPDSDemo.zip \
    --folder solutions/PPDSDemo/src \
    --packagetype Both \
    --allowDelete \
    --allowWrite

# The tool automatically finds PPDSDemo_managed.zip in the same folder
```

### Pack Solution (build artifact)

```bash
# Pack UNMANAGED (for dev/test import)
pac solution pack \
    --zipfile solutions/exports/PPDSDemo.zip \
    --folder solutions/PPDSDemo/src \
    --packagetype Unmanaged

# Pack MANAGED (for production deployment)
pac solution pack \
    --zipfile solutions/exports/PPDSDemo_managed.zip \
    --folder solutions/PPDSDemo/src \
    --packagetype Managed

# Pack BOTH at once
pac solution pack \
    --zipfile solutions/exports/PPDSDemo.zip \
    --folder solutions/PPDSDemo/src \
    --packagetype Both
```

### Using MSBuild/dotnet

```bash
# Build unmanaged (Debug)
dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Debug
# Output: bin/Debug/PPDSDemo.zip

# Build managed (Release)
dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Release
# Output: bin/Release/PPDSDemo.zip
```

---

## cdsproj Configuration

The `.cdsproj` file controls solution packaging:

```xml
<PropertyGroup>
  <SolutionRootPath>src</SolutionRootPath>
</PropertyGroup>

<!--
  Default behavior (from Microsoft.PowerApps.MSBuild.Solution.props):
  - Debug builds → Unmanaged
  - Release builds → Managed

  This works because source was unpacked with 'pac solution unpack' using packagetype Both
-->
```

| Build Config | Default Output |
|--------------|----------------|
| Debug | Unmanaged (`bin/Debug/PPDSDemo.zip`) |
| Release | Managed (`bin/Release/PPDSDemo.zip`) |

---

## CI/CD Integration

### Export from Dev (nightly or on-demand)

```yaml
- name: Export unmanaged
  run: |
    pac solution export --name PPDSDemo \
      --path ./exports/PPDSDemo.zip \
      --managed false --overwrite

- name: Export managed
  run: |
    pac solution export --name PPDSDemo \
      --path ./exports/PPDSDemo_managed.zip \
      --managed true --overwrite

- name: Unpack with packagetype Both
  run: |
    pac solution unpack \
      --zipfile ./exports/PPDSDemo.zip \
      --folder solutions/PPDSDemo/src \
      --packagetype Both \
      --allowDelete --allowWrite
```

### Build for Deployment

```yaml
- name: Build managed solution
  run: |
    dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Release
    # Output: solutions/PPDSDemo/bin/Release/PPDSDemo.zip (managed)
```

### Deploy to Target Environment

```yaml
- name: Import managed solution
  run: |
    pac solution import --path solutions/PPDSDemo/bin/Release/PPDSDemo.zip --activate-plugins
```

---

## Migration: Fixing Incorrect Structure

If your solution has `Managed/` and `Unmanaged/` subfolders or unmanaged-only source:

1. **Clean the src/ folder**
   ```bash
   rm -rf solutions/PPDSDemo/src/*
   ```

2. **Export BOTH from dev**
   ```bash
   pac solution export --name PPDSDemo --path solutions/exports --managed false --overwrite
   pac solution export --name PPDSDemo --path solutions/exports --managed true --overwrite
   ```

3. **Unpack with packagetype Both**
   ```bash
   pac solution unpack \
       --zipfile solutions/exports/PPDSDemo.zip \
       --folder solutions/PPDSDemo/src \
       --packagetype Both \
       --allowDelete --allowWrite
   ```

4. **Verify builds work**
   ```bash
   dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Debug   # Unmanaged
   dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Release # Managed
   ```

5. **Commit the corrected structure**

---

## See Also

- [TOOLS_REFERENCE.md](TOOLS_REFERENCE.md) - PowerShell script authentication
- [PAC_CLI_REFERENCE.md](PAC_CLI_REFERENCE.md) - PAC CLI commands
- [Microsoft: Solution Packager tool](https://learn.microsoft.com/en-us/power-platform/alm/solution-packager-tool)
- [Microsoft: Solution concepts](https://learn.microsoft.com/en-us/power-platform/alm/solution-concepts-alm)
