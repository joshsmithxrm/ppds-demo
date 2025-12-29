# Documentation

Reference architecture documentation for Power Platform ALM using GitHub Actions and PAC CLI.

This documentation provides patterns and guidance for implementing enterprise-grade application lifecycle management (ALM) for Power Platform solutions. It is designed to be both a working implementation and a reference for teams starting new projects.

---

## Strategy Documents

Decision documentation explaining **why** we use specific approaches.

| Document | Description |
|----------|-------------|
| [ALM_OVERVIEW.md](strategy/ALM_OVERVIEW.md) | High-level ALM philosophy and principles |
| [ENVIRONMENT_STRATEGY.md](strategy/ENVIRONMENT_STRATEGY.md) | Dev/QA/Prod environments and their purposes |
| [BRANCHING_STRATEGY.md](strategy/BRANCHING_STRATEGY.md) | Git workflow with develop/main branches |
| [PIPELINE_STRATEGY.md](strategy/PIPELINE_STRATEGY.md) | CI/CD approach using PAC CLI |

---

## Guides

Step-by-step documentation explaining **how** to perform common tasks.

| Document | Description |
|----------|-------------|
| [GETTING_STARTED_GUIDE.md](guides/GETTING_STARTED_GUIDE.md) | Clone, configure, and deploy the demo solution |
| [LOCAL_DEVELOPMENT_GUIDE.md](guides/LOCAL_DEVELOPMENT_GUIDE.md) | .NET User Secrets and console app configuration |
| [ENVIRONMENT_SETUP_GUIDE.md](guides/ENVIRONMENT_SETUP_GUIDE.md) | Power Platform environments, PAC CLI, service principals |
| [BRANCH_PROTECTION_GUIDE.md](guides/BRANCH_PROTECTION_GUIDE.md) | Configure GitHub branch protection rules |
| [PLUGIN_REMOVAL_GUIDE.md](guides/PLUGIN_REMOVAL_GUIDE.md) | Two-phase workflow for removing plugin steps |
| [CLAUDE_MD_GUIDE.md](guides/CLAUDE_MD_GUIDE.md) | Creating effective CLAUDE.md files for Dynamics projects |

---

## Reference

Quick-reference material for specifications, patterns, and standards.

| Document | Description |
|----------|-------------|
| [PAC_CLI_REFERENCE.md](reference/PAC_CLI_REFERENCE.md) | Common PAC CLI commands and usage |
| [PLUGIN_COMPONENTS_REFERENCE.md](reference/PLUGIN_COMPONENTS_REFERENCE.md) | Plugin Assemblies vs Plugin Packages |
| [SOLUTION_STRUCTURE_REFERENCE.md](reference/SOLUTION_STRUCTURE_REFERENCE.md) | Solution folder structure and packaging |
| [TOOLS_REFERENCE.md](reference/TOOLS_REFERENCE.md) | PowerShell deployment scripts and PPDS.Tools |
| [WEBRESOURCE_PATTERNS.md](reference/WEBRESOURCE_PATTERNS.md) | JavaScript/TypeScript web resource patterns |
| [TESTING_PATTERNS.md](reference/TESTING_PATTERNS.md) | Plugin and workflow unit testing patterns |
| [DOCUMENTATION_STANDARDS.md](reference/DOCUMENTATION_STANDARDS.md) | Documentation file naming and structure |

---

## Quick Links

- [CLAUDE.md](../CLAUDE.md) - AI assistant coding guide (includes documentation standards)
- [ROADMAP.md](ROADMAP.md) - Project roadmap and status tracking
- [GitHub Workflows](../.github/workflows/) - CI/CD pipeline definitions

---

## See Also

- [Power Platform ALM Documentation](https://learn.microsoft.com/en-us/power-platform/alm/) - Official Microsoft ALM guidance
- [PAC CLI Reference](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/) - Official PAC CLI documentation
- [Power Platform Developer Suite](https://github.com/joshsmithxrm/power-platform-developer-suite) - VS Code extension this demo showcases
