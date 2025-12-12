# Power Platform Developer Suite Demo Solution - Roadmap

This document tracks what needs to be built for this demo solution. Check items off as they're completed.

---

## Phase 1: Project Foundation

### Repository Setup
- [x] Create CLAUDE.md with coding standards and patterns
- [x] Create README.md with project overview
- [x] Set up .claude/settings.local.json for permissions
- [x] Create docs folder structure
- [x] Document PAC CLI installation and usage
- [ ] Create .editorconfig for consistent formatting
- [ ] Set up solution-level .gitignore additions (if needed)

### Documentation
- [ ] Create docs/development/plugins.md - Plugin development patterns
- [ ] Create docs/development/web-resources.md - Web resource patterns
- [ ] Create docs/development/custom-apis.md - Custom API patterns
- [ ] Create docs/development/pcf.md - PCF control patterns
- [ ] Create docs/development/testing.md - Testing strategies
- [x] Create docs/cicd/solution-export-pipeline.md - CI/CD pipeline documentation

---

## Phase 2: C# Projects Setup

### Plugin Assembly (Classic)
- [x] Create src/Plugins/PPDSDemo.Plugins/PPDSDemo.Plugins.csproj (.NET Framework 4.6.2)
- [x] Add NuGet references (Microsoft.CrmSdk.CoreAssemblies, Microsoft.CrmSdk.Workflow)
- [x] Create folder structure (Plugins/, WorkflowActivities/, Services/)
- [x] Create base plugin class with common patterns (PluginBase.cs, LocalPluginContext)
- [x] Create 2-3 example plugins:
  - [x] AccountPreCreatePlugin - Validation example
  - [x] ContactPostUpdatePlugin - Audit/logging example with pre-image handling
- [x] Create workflow activity in same assembly:
  - [x] SendNotificationActivity - Shows input/output arguments
- [x] Enable strong-name signing (SNK file for Dataverse sandbox)

### Plugin Package (Modern)
- [x] Create src/PluginPackages/PPDSDemo.PluginPackage/ using `pac plugin init`
- [x] Add example plugin with NuGet dependency (Newtonsoft.Json)
- [ ] Document the difference between classic and package approach

### Workflow Activities
- [x] Workflow activities included in PPDSDemo.Plugins assembly (common pattern)
- [x] SendNotificationActivity - Shows input/output arguments
- [ ] CalculateValueActivity - Shows business logic in workflow (optional)

### Custom APIs
- [ ] Create src/CustomAPIs/PPDSDemo.CustomAPIs/PPDSDemo.CustomAPIs.csproj
- [ ] Create example Custom API implementation:
  - [ ] ppds_CalculateDiscount - Shows request/response parameters
  - [ ] ppds_ValidateRecord - Shows bound action pattern

### Unit Tests
- [ ] Create tests/PPDSDemo.Plugins.Tests/PPDSDemo.Plugins.Tests.csproj
- [ ] Add FakeXrmEasy or similar testing framework
- [ ] Create example tests for each plugin
- [ ] Create tests/PPDSDemo.Workflow.Tests/ with workflow activity tests

---

## Phase 3: Web Resources

### TypeScript Setup
- [x] Create src/WebResources/ppds_/ folder structure
- [ ] Set up TypeScript configuration (tsconfig.json)
- [ ] Add Xrm typings (@types/xrm or custom)
- [ ] Create build script for compiling TS to JS

### Form Scripts
- [x] Create ppds_/scripts/account.form.js - Account form handler (JavaScript)
- [ ] Create ppds_/scripts/contact.form.ts - Contact form handler
- [ ] Create ppds_/scripts/common.ts - Shared utilities

### Ribbon Scripts
- [ ] Create ppds_/scripts/account.ribbon.ts - Custom ribbon button handlers

### HTML Web Resources
- [ ] Create ppds_/html/example-dialog.html - Custom dialog example
- [ ] Create ppds_/css/custom-styles.css - Stylesheet example

### Images
- [ ] Add sample icon images for ribbon buttons

---

## Phase 4: PCF Controls

### Field Control
- [ ] Create src/PCF/PPDSDemo.Controls/ base structure
- [ ] Create simple field control (e.g., formatted phone number input)
- [ ] Add control manifest and resources

### Dataset Control (Optional)
- [ ] Create dataset control example (e.g., custom grid view)

---

## Phase 5: Solution Structure

### Solution Metadata
- [x] Create solutions/PPDSDemo/src/ folder structure
- [x] Create Solution.xml with proper publisher info (ppds prefix, PPDSDemoPublisher)
- [x] Set up Customizations.xml
- [x] Import solution to Dataverse

### Custom Tables
- [ ] Define ppds_DemoEntity table (simple demo entity)
- [ ] Define ppds_DemoChild table (shows 1:N relationship)
- [ ] Add sample columns of various types:
  - [ ] Text, Number, DateTime, OptionSet, Lookup
- [ ] Create table relationship definitions

### Option Sets
- [ ] Create ppds_DemoStatus global option set
- [ ] Create ppds_Priority global option set

### Security Roles
- [ ] Create ppds Demo User role
- [ ] Create ppds Demo Admin role

### Environment Variables
- [ ] Create ppds_ApiEndpoint environment variable
- [ ] Create ppds_FeatureFlag environment variable (boolean)

### Plugin Registration
- [x] Create PluginAssemblies/ registration XML (via export)
- [x] Create SdkMessageProcessingSteps/ step definitions (via export)
- [x] Deploy plugins to Dataverse (Deploy-Components.ps1)

### Web Resource Registration
- [x] Add web resource definitions to solution (via export)
- [x] Deploy web resources to Dataverse (Deploy-Components.ps1)

---

## Phase 6: Power Automate Flows

### Cloud Flows
- [ ] Create solutions/PPDSDemo/src/Workflows/ structure
- [ ] Create example instant flow (manual trigger)
- [ ] Create example automated flow (record trigger)
- [ ] Include connection reference example

---

## Phase 7: Build & Deployment

### Build Scripts
- [ ] Create tools/build.ps1 - Builds all C# projects
- [ ] Create tools/pack-solution.ps1 - Packs solution for deployment
- [x] Create tools/Deploy-Components.ps1 - Deploys web resources and plugins to Dataverse
- [x] Create tools/Generate-Snk.ps1 - Generates strong name keys for assembly signing

### CI/CD
- [x] Create .github/workflows/export-solution.yml - Nightly solution export from Dataverse
- [ ] Create .github/workflows/build.yml - Build validation on PR
- [ ] Create .github/workflows/release.yml - Solution packaging for release

---

## Phase 8: Documentation Polish

### Developer Guides
- [ ] Complete all docs/development/ guides with examples
- [ ] Add troubleshooting section to each guide
- [ ] Cross-reference CLAUDE.md patterns

### Extension Integration Docs
- [ ] Document how each component showcases extension features
- [ ] Add screenshots/examples of extension usage

---

## Notes

- This is a demo solution - prioritize clarity over complexity
- Each component should demonstrate a specific pattern or extension feature
- Keep business logic simple but realistic enough to be educational
- All code should follow patterns defined in CLAUDE.md
