# Hotfix Guide

This guide provides step-by-step instructions for creating and deploying emergency fixes to production.

---

## When to Use a Hotfix

Use a hotfix when:
- A critical bug is affecting production users
- A security vulnerability needs immediate patching
- The fix cannot wait for the normal release cycle
- The issue exists in production but NOT in develop (or you need it deployed immediately)

**Do NOT use a hotfix for:**
- Normal bug fixes (use `fix/*` branch to `develop`)
- Feature enhancements
- Non-critical issues

---

## Hotfix Workflow Overview

```
main (production code)
  │
  └─► hotfix/fix-critical-bug (create from main)
        │
        ├─► Make minimal fix
        │
        ├─► PR to main (triggers validation)
        │
        └─► Merge to main (deploys to production)
              │
              └─► Cherry-pick to develop (keep branches in sync)
```

---

## Step-by-Step Process

### 1. Create Hotfix Branch from Main

```bash
# Ensure you're on the latest main
git checkout main
git pull origin main

# Create hotfix branch
git checkout -b hotfix/fix-critical-workflow
```

**Naming convention:** `hotfix/{short-description}`

Examples:
- `hotfix/fix-critical-workflow`
- `hotfix/security-patch-auth`
- `hotfix/null-reference-plugin`

### 2. Make the Fix

For **plugin code changes:**

```bash
# Make your code changes
# Edit src/Plugins/PPDSDemo.Plugins/...

# Build to verify
dotnet build src/Plugins/PPDSDemo.Plugins/PPDSDemo.Plugins.csproj -c Release

# Commit
git add .
git commit -m "fix: resolve null reference in account validation"
```

For **solution/customization changes:**

1. Make the fix directly in the **Production environment** (or a copy)
2. Export the solution:
   ```bash
   # Connect to the environment with the fix
   pac auth create --environment https://your-prod.crm.dynamics.com

   # Export
   pac solution export --name PPDSDemo --path ./hotfix-export.zip --managed false

   # Unpack to your hotfix branch
   pac solution unpack --zipfile ./hotfix-export.zip --folder solutions/PPDSDemo/src --packagetype Both --allowWrite
   ```
3. Review and commit only the necessary changes:
   ```bash
   git status
   git diff

   # Add only the files related to your fix
   git add solutions/PPDSDemo/src/Workflows/your-workflow.json
   git commit -m "fix: correct status transition in approval workflow"
   ```

### 3. Update Version

Hotfixes should increment the version to ensure deployment:

```bash
# Edit version.txt - increment the build number
# Example: 1.0.20251215.5 → 1.0.20251215.6

# Or use current date if significant time has passed
# Example: 1.0.20251215.5 → 1.0.20251216.1
```

Update `solutions/PPDSDemo/src/Other/Solution.xml`:
```xml
<Version>1.0.20251216.1</Version>
```

Commit the version change:
```bash
git add solutions/PPDSDemo/version.txt solutions/PPDSDemo/src/Other/Solution.xml
git commit -m "chore: bump version for hotfix release"
```

### 4. Push and Create PR to Main

```bash
# Push your hotfix branch
git push -u origin hotfix/fix-critical-workflow
```

Create a PR targeting `main`:
- Go to GitHub → Pull Requests → New Pull Request
- Base: `main` ← Compare: `hotfix/fix-critical-workflow`
- Title: `fix: [HOTFIX] Resolve critical workflow issue`
- Description: Include:
  - What broke
  - What the fix does
  - Testing performed
  - Urgency level

### 5. PR Validation

The PR will trigger `pr-validate.yml`:
- Branch policy check (hotfix/* to main is allowed)
- Build validation
- Solution packing validation
- Solution Checker (if configured)

**Wait for all checks to pass** before merging.

### 6. Merge to Main

Once approved:
1. Use **merge commit** (not squash - this is enforced by ruleset)
2. The merge triggers `cd-prod.yml`
3. Monitor the deployment in GitHub Actions

### 7. Verify Production Deployment

After deployment completes:
- [ ] Check workflow summary for success
- [ ] Verify the fix in production
- [ ] Test related functionality
- [ ] Monitor for any new errors

### 8. Sync to Develop (Critical!)

**Do not skip this step!** The hotfix must be merged back to `develop` to prevent regression.

**Option A: Cherry-pick (Recommended)**

```bash
# Switch to develop
git checkout develop
git pull origin develop

# Cherry-pick the hotfix commits
git cherry-pick <commit-sha-1>
git cherry-pick <commit-sha-2>

# Push to develop
git push origin develop
```

**Option B: Merge hotfix to develop**

```bash
git checkout develop
git pull origin develop

# Merge the hotfix branch
git merge hotfix/fix-critical-workflow

# Resolve any conflicts
git push origin develop
```

**Option C: Create PR from hotfix to develop**

If you need review:
1. Create PR: `hotfix/fix-critical-workflow` → `develop`
2. This PR will be squash-merged (develop ruleset)
3. Wait for approval and merge

### 9. Clean Up

```bash
# Delete the hotfix branch locally
git branch -d hotfix/fix-critical-workflow

# Delete the remote branch
git push origin --delete hotfix/fix-critical-workflow
```

---

## Hotfix Decision Tree

```
Is it a production emergency?
├── Yes → Create hotfix/* branch from main
│         Make minimal fix
│         PR to main
│         Cherry-pick to develop
│
└── No → Is it already in develop but not main?
         ├── Yes → Create PR from develop to main (normal release)
         │
         └── No → Create fix/* branch from develop
                  Fix the issue
                  PR to develop (normal flow)
```

---

## Common Mistakes to Avoid

| Mistake | Why It's Bad | What to Do Instead |
|---------|--------------|-------------------|
| Creating hotfix from develop | Wrong starting point | Always create from main |
| Forgetting to sync to develop | Regression in next release | Always cherry-pick or merge back |
| Making non-essential changes | Increases risk | Only fix the immediate issue |
| Skipping version update | Deployment may be skipped | Always increment version |
| Not testing in production | May not actually fix issue | Verify immediately after deploy |

---

## Emergency Contacts

| Situation | Contact |
|-----------|---------|
| Need PR approval urgently | Team lead / designated reviewer |
| Deployment failing | DevOps team |
| Can't access production | Power Platform admin |
| Need to rollback | See [ROLLBACK_GUIDE.md](ROLLBACK_GUIDE.md) |

---

## Hotfix vs Normal Fix

| Aspect | Hotfix (`hotfix/*`) | Normal Fix (`fix/*`) |
|--------|--------------------|--------------------|
| Created from | `main` | `develop` |
| PR target | `main` | `develop` |
| Deploys to | Production (immediate) | QA first, then production |
| Sync after | Cherry-pick to develop | Flows naturally to main |
| Use when | Emergency | Normal priority |

---

## See Also

- [BRANCHING_STRATEGY.md](../strategy/BRANCHING_STRATEGY.md) - Branch rules and merge strategies
- [ROLLBACK_GUIDE.md](ROLLBACK_GUIDE.md) - If the hotfix makes things worse
- [PIPELINE_STRATEGY.md](../strategy/PIPELINE_STRATEGY.md) - CI/CD workflow details
