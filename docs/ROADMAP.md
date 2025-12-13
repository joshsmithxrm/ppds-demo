# Roadmap

This demo solution showcases Power Platform ALM patterns with **one example of each major component type**.

---

## Guiding Principles

| Principle | Meaning |
|-----------|---------|
| **One of each** | Single correct example beats multiple variations |
| **Show, don't tell** | Components demonstrate patterns; CLAUDE.md documents them |
| **ALM-first** | Everything exists to prove the CI/CD story works |
| **Template-ready** | Clone and replace demo content, keep infrastructure |

---

## Phase 1: ALM Infrastructure

*The foundation that makes everything else work.*

| Item | Status | Notes |
|------|--------|-------|
| Repository structure | Done | Folders, naming conventions |
| CLAUDE.md patterns | Done | AI-assistable coding guide |
| Strategy docs | Done | ALM, Environment, Branching, Pipeline |
| CI/CD: Dev to QA | Done | ci-export.yml, cd-qa.yml |
| Branching (develop/main) | Done | Feature branch workflow |
| CI/CD: QA to Prod | Done | cd-prod.yml |
| Plugin build integration | Done | Build, copy assemblies & packages to solution |
| Plugin components reference | Done | docs/reference/PLUGIN_COMPONENTS_REFERENCE.md |
| Environment setup guide | Done | docs/guides/ENVIRONMENT_SETUP_GUIDE.md |
| Getting started guide | Done | docs/guides/GETTING_STARTED_GUIDE.md |
| Solution structure reference | Done | docs/reference/SOLUTION_STRUCTURE_REFERENCE.md |
| Deployment settings | Done | Per-environment config (qa/prod.deploymentsettings.json) |

---

## Phase 2: Core Solution Components

*Minimum viable solution content to prove ALM works.*

| Item | Status | Notes |
|------|--------|-------|
| Plugin assembly (classic) | Done | AccountPreCreatePlugin, PluginBase |
| Plugin package (modern) | Done | With Newtonsoft.Json dependency |
| Workflow activity | Done | SendNotificationActivity |
| Web resource (JS) | Done | account.form.js |
| Custom table | Done | ppds_DemoRecord |
| Global option set | Done | ppds_status |
| Environment variables | Done | ppds_ApiEndpoint, ppds_EnableFeatureX |

---

## Phase 3: Extended Components

*Full scope of Power Platform capabilities.*

| Item | Status | Notes |
|------|--------|-------|
| Custom API | Pending | ppds_ValidateRecord |
| PCF control | Pending | Simple field control |
| Cloud flow | Pending | Automated trigger example |
| Connection reference | Pending | Goes with flow |
| Web resource (HTML) | Pending | Dialog/page example |
| Web resource (CSS) | Pending | Stylesheet |
| Web resource (Images) | Pending | Icons for ribbon/UI |

---

## Phase 4: Polish

*Enhancements after core is solid.*

| Item | Status | Notes |
|------|--------|-------|
| PR validation workflow | Done | pr-validate.yml with build & pack validation |
| Solution Checker integration | Done | Quality gates in PR validation |
| Security role | Pending | One example role |
| Business rule | Pending | One simple rule |
| Approval gates for Prod | Pending | Manual approval workflow |

---

## Future Considerations

Topics to document when the need arises:

- Personal Developer Environments (PDE)
- Power Platform Pipelines integration
- Environment provisioning automation
- Canvas app source control challenges
- Multi-solution deployment ordering
- Rollback automation
- ALM Accelerator comparison

---

## Explicitly Out of Scope

This repo is intentionally minimal. We do NOT include:

- Multiple examples of same component type
- Unit testing frameworks (FakeXrmEasy, etc.)
- TypeScript build pipelines
- Component development tutorials (use Microsoft docs)
- Complex multi-solution architectures

---

## Documentation Strategy

| What We Document | Where |
|------------------|-------|
| Coding patterns | CLAUDE.md |
| ALM philosophy | docs/strategy/ |
| How to set up | docs/guides/ (ENVIRONMENT_SETUP, GETTING_STARTED) |

| What We DON'T Document |
|------------------------|
| How to write a plugin (use Microsoft docs) |
| How to build a PCF control (use Microsoft docs) |
| Comprehensive development guides |

The components ARE the documentation. They exist, follow patterns, and deploy.

---

## See Also

- [strategy/](strategy/) - ALM, Environment, Branching, Pipeline strategies
- [CLAUDE.md](../CLAUDE.md) - AI-assistable coding patterns
- [README.md](README.md) - Documentation navigation
