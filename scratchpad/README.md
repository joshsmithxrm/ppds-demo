# Scratchpad - .NET 10 Single-File C# Scripts

Quick Dataverse scripts without project files. Like LinqPad, but native .NET.

## Requirements

- .NET 10 SDK (`dotnet --version` should show 10.x)

## Usage

```powershell
# Scripts automatically use the demo app's User Secrets - no setup needed!
cd scratchpad
dotnet run whoami.cs
dotnet run query.cs
```

**Connection:** Scripts read from the same User Secrets as the demo app (`ppds-dataverse-demo`).
If you've already configured the demo app, these scripts work immediately.

## Scripts

| Script | Purpose |
|--------|---------|
| `whoami.cs` | Test connectivity, show org info |
| `query.cs` | Query template with examples |

## How It Works

.NET 10 supports running `.cs` files directly. The `#:package` directive adds NuGet references:

```csharp
#:package Microsoft.PowerPlatform.Dataverse.Client@1.1.27

using Microsoft.PowerPlatform.Dataverse.Client;
// ... your code with top-level statements
```

## Tips

- **Edit and run** - No build step, just save and run
- **Add packages** - Use `#:package Name@Version` at top of file
- **Convert to project** - Run `dotnet project convert script.cs` to generate full project

## Reference

- [Announcing dotnet run app.cs](https://devblogs.microsoft.com/dotnet/announcing-dotnet-run-app/)
- [C# 14 Script Directives](https://endjin.com/what-we-think/talks/csharp-14-new-feature-script-directives)
