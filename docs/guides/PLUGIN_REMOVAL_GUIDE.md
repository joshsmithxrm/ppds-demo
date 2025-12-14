# Plugin Removal Guide

This guide explains the proper workflow for removing plugins from a Dataverse assembly. Removing plugins is a multi-step process that must be followed carefully to avoid deployment failures.

---

## Why Plugin Removal is Complex

When you remove a plugin class from an assembly, several things are registered in Dataverse that must be cleaned up:

1. **SDK Message Processing Step Images** - Pre/post images attached to steps
2. **SDK Message Processing Steps** - The actual plugin step registrations
3. **Plugin Types** - The class registration in Dataverse

These must be removed in the correct order, and the removal must be deployed to ALL environments before proceeding to the next step.

### The PAC CLI Constraint

The `pac plugin push` command validates that all plugin types registered in Dataverse exist in the assembly being pushed. If a plugin type exists in Dataverse but not in the assembly, the push fails with:

```
Error: PluginType [Namespace.ClassName] not found in PluginAssembly
```

This means you cannot simply delete a plugin class and push - you must clean up Dataverse first.

---

## Plugin Removal Workflow

### Phase 1: Remove Step Registrations

**Goal:** Delete all steps (and their images) for the plugin being removed.

1. **Remove `[PluginStep]` attributes** from the plugin class (keep the class file for now)
   ```csharp
   // BEFORE: Has step registration
   [PluginStep(Message = "Update", EntityLogicalName = "account", ...)]
   public class MyPlugin : PluginBase { ... }

   // AFTER: No step registration (class still exists)
   public class MyPlugin : PluginBase { ... }
   ```

2. **Build and extract registrations**
   ```powershell
   dotnet build src/Plugins/MyProject -c Release
   .\tools\Extract-PluginRegistrations.ps1 -Project MyProject
   ```

3. **Deploy with `-Force` to delete orphaned steps**
   ```powershell
   .\tools\Deploy-Plugins.ps1 -Project MyProject -Force
   ```

   You should see:
   ```
   [WARNING] Deleting orphaned step: MyPlugin: Update of account
   [SUCCESS]   Deleted
   ```

4. **Commit and deploy to ALL environments**
   ```powershell
   git add .
   git commit -m "chore: remove step registrations for MyPlugin"
   git push
   ```

   Deploy to QA, Prod, etc. via your CI/CD pipeline or manual deployment.

### Phase 2: Remove Plugin Type and Class

**Goal:** Delete the plugin type from Dataverse, then remove the class from the assembly.

> **Important:** Only proceed to Phase 2 after Phase 1 has been deployed to ALL environments.

1. **Delete the plugin class file**
   ```powershell
   rm src/Plugins/MyProject/Plugins/MyPlugin.cs
   ```

2. **Build and extract registrations**
   ```powershell
   dotnet build src/Plugins/MyProject -c Release
   .\tools\Extract-PluginRegistrations.ps1 -Project MyProject
   ```

3. **Deploy with `-Force` to delete orphaned plugin type**
   ```powershell
   .\tools\Deploy-Plugins.ps1 -Project MyProject -Force
   ```

   You should see:
   ```
   [WARNING] Deleting orphaned plugin type: Namespace.MyPlugin
   [SUCCESS]   Plugin type deleted
   [SUCCESS] Assembly updated successfully
   ```

4. **Commit and deploy to ALL environments**
   ```powershell
   git add .
   git commit -m "chore: remove MyPlugin class"
   git push
   ```

---

## Why Two Phases?

### Solution Import Considerations

When deploying via managed solutions to upstream environments (QA, Prod):

- **Solution import handles steps and types differently** - Trying to remove both in one deployment can cause import failures
- **Dependency order matters** - Steps depend on plugin types; removing the type first breaks the step

### PAC CLI Considerations

- PAC CLI validates plugin types before pushing
- If you delete the class first, PAC CLI will fail because the type exists in Dataverse but not in the assembly
- By removing steps first (Phase 1), the plugin type becomes "orphaned" (no steps)
- The deployment script detects this and deletes the orphaned type before pushing (Phase 2)

---

## Quick Reference

| Phase | Action | Deploy To | Commit Message |
|-------|--------|-----------|----------------|
| 1 | Remove `[PluginStep]` attributes, deploy with `-Force` | All environments | `chore: remove step registrations for MyPlugin` |
| 2 | Delete class file, deploy with `-Force` | All environments | `chore: remove MyPlugin class` |

---

## Troubleshooting

### "PluginType not found in PluginAssembly"

You tried to push an assembly where a plugin type exists in Dataverse but not in the DLL.

**Solution:** Run deployment with `-Force` to delete the orphaned plugin type:
```powershell
.\tools\Deploy-Plugins.ps1 -Project MyProject -Force
```

### "Orphaned plugin type has X active step(s)"

The plugin type still has steps registered. You must delete the steps first.

**Solution:** Complete Phase 1 first - remove `[PluginStep]` attributes and deploy with `-Force`.

### Workflow Activities

Workflow activities (classes inheriting from `CodeActivity`) don't have `[PluginStep]` attributes but are still registered as plugin types in Dataverse.

- They appear in `allTypeNames` in registrations.json
- They will NOT be deleted by `-Force` as long as the class exists in the assembly
- If you remove a workflow activity class, follow the same Phase 2 process

---

## See Also

- [PLUGIN_DEPLOYMENT_DESIGN.md](../design/PLUGIN_DEPLOYMENT_DESIGN.md) - Overall deployment architecture
- [PLUGIN_COMPONENTS_REFERENCE.md](../reference/PLUGIN_COMPONENTS_REFERENCE.md) - Plugin development patterns
- [Microsoft Docs: Register a plug-in](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in)
