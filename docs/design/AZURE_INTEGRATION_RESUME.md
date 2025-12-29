# Azure Integration - Resume Document

> **TEMPORARY** - Delete after implementation is complete.

## Current State

- [x] Strategy documented: `docs/strategy/CROSS_TENANT_AZURE_INTEGRATION.md`
- [x] GitHub Issue created: #37
- [ ] Implementation not started

## Branch

```
feature/cross-tenant-azure-integration
```

## Quick Context

**Goal:** Build Azure integration components for:
1. Reference implementation (demo repo)
2. Test bed for PPDS Extension PRT

**Cross-tenant setup:**
- Power Platform: Developer subscription (no Azure)
- Azure: MSDN subscription (separate tenant)

**Pattern:** "Functions as Glue" - Azure Functions handle triggers, Web API contains business logic.

## Phases

| Phase | Components | Status |
|-------|------------|--------|
| 1 | Service Endpoints (Webhook + Service Bus) | Not started |
| 2 | Custom API | Not started |
| 3 | Virtual Table Data Provider | Not started |

## Key Decisions Made

- JSON message format (not .NET Binary)
- No session-enabled queues (Dataverse doesn't set SessionId)
- Virtual table uses in-memory API data (no SQL needed)
- Event Hub deferred to later

## To Resume

1. Open issue #37 for full details
2. Start with Phase 1: Azure resources (Function App, Service Bus, Web API)
3. Then Dataverse components (Service Endpoints, Plugin Steps)

## Resources

- Strategy doc: `docs/strategy/CROSS_TENANT_AZURE_INTEGRATION.md`
- Issue: https://github.com/joshsmithxrm/ppds-demo/issues/37
