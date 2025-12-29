# PPDS.Dataverse Demo

Demo console app showcasing PPDS.Dataverse connection pooling, bulk operations, and PPDS.Migration data migration workflows.

## Quick Start

```bash
# Configure credentials (one-time setup)
cd src/Console/PPDS.Dataverse.Demo
dotnet user-secrets set "Dataverse:Environments:Dev:Url" "https://yourorg.crm.dynamics.com"
dotnet user-secrets set "Dataverse:Environments:Dev:Connections:0:ClientId" "your-client-id"
dotnet user-secrets set "Dataverse:Environments:Dev:Connections:0:ClientSecret" "your-secret"

# Test connectivity
dotnet run -- whoami
```

## Available Commands

| Command | Description |
|---------|-------------|
| `whoami` | Test connectivity with WhoAmI request |
| `seed` | Create sample accounts and contacts |
| `clean` | Remove sample data (`--env QA` for other environments) |
| `create-geo-schema` | Create geographic tables (state, city, zipcode) |
| `load-geo-data` | Download and load 42K US ZIP codes |
| `count-geo-data` | Display record counts for geo tables |
| `clean-geo-data` | Bulk delete geographic data |
| `export-geo-data` | Export geo data to portable ZIP package |
| `import-geo-data` | Import geo data from ZIP package |
| `migrate-geo-data` | Full migration workflow (export + import + verify) |
| `test-migration` | End-to-end test of PPDS.Migration library |
| `demo-features` | Demo migration features (M2M, filtering, etc.) |
| `migrate-to-qa` | Export from Dev and import to QA |
| `generate-user-mapping` | Generate user mapping for cross-env migration |

## Configuration

This app uses .NET User Secrets with a multi-environment structure:

```json
{
  "Dataverse": {
    "DefaultEnvironment": "Dev",
    "Environments": {
      "Dev": {
        "Url": "https://dev.crm.dynamics.com",
        "Connections": [
          { "ClientId": "...", "ClientSecret": "..." }
        ]
      },
      "QA": {
        "Url": "https://qa.crm.dynamics.com",
        "Connections": [
          { "ClientId": "...", "ClientSecret": "..." }
        ]
      }
    }
  }
}
```

See [LOCAL_DEVELOPMENT_GUIDE.md](../../docs/guides/LOCAL_DEVELOPMENT_GUIDE.md) for detailed setup instructions.

## Examples

```bash
# Basic connectivity test
dotnet run -- whoami

# Seed sample data
dotnet run -- seed

# Load geographic data (42K records)
dotnet run -- load-geo-data

# Cross-environment migration (dry run)
dotnet run -- migrate-to-qa --dry-run

# Export geo data for migration
dotnet run -- export-geo-data --output geo-backup.zip
```
