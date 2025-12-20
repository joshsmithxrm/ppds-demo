# PPDS.Dataverse Demo

Demonstrates PPDS.Dataverse connection pooling with a simple WhoAmI request.

## Setup

1. Create `appsettings.Development.json` with your connection string:

```json
{
  "Dataverse": {
    "Connections": [
      {
        "Name": "Primary",
        "ConnectionString": "AuthType=ClientSecret;Url=https://yourorg.crm.dynamics.com;ClientId=xxx;ClientSecret=xxx"
      }
    ]
  }
}
```

2. Run the demo:

```bash
dotnet run --project src/Console/PPDS.Dataverse.Demo
```

## Connection String Formats

**Client Secret (Application User):**
```
AuthType=ClientSecret;Url=https://yourorg.crm.dynamics.com;ClientId=<app-id>;ClientSecret=<secret>
```

**Interactive (Browser Login):**
```
AuthType=OAuth;Url=https://yourorg.crm.dynamics.com;AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=http://localhost;LoginPrompt=Auto
```

## Expected Output

```
PPDS.Dataverse Connection Pool Demo
====================================

Connecting to Dataverse...

WhoAmI Result:
  User ID:         xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
  Organization ID: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
  Business Unit:   xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx

Pool Statistics:
  Total Connections: 1
  Active:            0
  Idle:              1
  Requests Served:   1
```
