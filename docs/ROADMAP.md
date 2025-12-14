# Roadmap

Planned additions to showcase Power Platform component types and ALM patterns.

---

## Guiding Principles

| Principle | Meaning |
|-----------|---------|
| **One of each** | Single correct example beats multiple variations |
| **Show, don't tell** | Components demonstrate patterns; CLAUDE.md documents them |
| **ALM-first** | Everything exists to prove the CI/CD story works |
| **Template-ready** | Clone and replace demo content, keep infrastructure |

---

## Extended Components

*Full scope of Power Platform capabilities to showcase the VS Code extension.*

| Item | Notes |
|------|-------|
| Custom API | `ppds_ValidateRecord` |
| PCF control | Simple field control |
| Cloud flow | Automated trigger example |
| Connection reference | Goes with flow |
| Web resource (HTML) | Dialog/page example |
| Web resource (CSS) | Stylesheet |
| Web resource (Images) | Icons for ribbon/UI |

---

## Polish

*Enhancements to round out the demo.*

| Item | Notes |
|------|-------|
| Security role | One example role |
| Business rule | One simple rule |
| Approval gates for Prod | GitHub environment protection |

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

## Out of Scope

This repo is intentionally minimal. We do NOT include:

- Multiple examples of same component type
- Unit test projects (patterns documented in [TESTING_PATTERNS.md](reference/TESTING_PATTERNS.md) for reference)
- Component development tutorials (use Microsoft docs)
- Complex multi-solution architectures

---

## See Also

- [strategy/](strategy/) - ALM, Environment, Branching, Pipeline strategies
- [CLAUDE.md](../CLAUDE.md) - AI-assistable coding patterns
- [README.md](README.md) - Documentation navigation
