# Rollback Guide

This guide covers how to rollback a failed or problematic deployment using our Git-based ALM strategy, following Microsoft's recommended practices.

---

## Rollback Philosophy

Microsoft recommends making **Git the source of truth** and managing all rollbacks via source control. This ensures rollbacks are:

- **Versioned** - Every state is tracked
- **Traceable** - Audit trail of what was deployed
- **Reproducible** - Same artifact can be redeployed

Our ALM implementation supports this through:
- Release tags (`v{VERSION}`) created on every production deployment
- Build artifacts retained for 30 days
- Version comparison that prevents duplicate imports

---

## Rollback Options

### Option 1: Redeploy Previous Artifact (Recommended)

**Use when:** The previous version worked and you have the artifact available (within 30 days).

**Steps:**

1. **Find the last working version**
   ```bash
   # List recent release tags
   git tag -l "v*" --sort=-version:refname | head -10

   # Example output:
   # v1.0.20251215.5   <- current (broken)
   # v1.0.20251214.3   <- last known good
   ```

2. **Find the CI Build run for that version**
   - Go to GitHub Actions → CI: Build Solution
   - Find the run that produced the working version
   - Note the run number (e.g., `#42`)

3. **Manually trigger CD: Deploy to Prod**
   - Go to GitHub Actions → CD: Deploy to Production
   - Click "Run workflow"
   - Enter the artifact run ID from step 2
   - Type `DEPLOY` to confirm
   - Run the workflow

4. **Verify deployment**
   - Check the workflow summary for success
   - Test the rollback in production

### Option 2: Checkout Tag and Rebuild

**Use when:** The artifact is no longer available (older than 30 days) or you need to modify something before redeploying.

**Steps:**

1. **Checkout the release tag**
   ```bash
   # List tags to find the target version
   git tag -l "v*" --sort=-version:refname

   # Checkout the tag
   git checkout v1.0.20251214.3
   ```

2. **Create a hotfix branch (optional but recommended)**
   ```bash
   git checkout -b hotfix/rollback-to-1.0.20251214.3
   git push -u origin hotfix/rollback-to-1.0.20251214.3
   ```

3. **Trigger CI Build manually**
   - Go to GitHub Actions → CI: Build Solution
   - Run workflow from the hotfix branch
   - Wait for artifact to be created

4. **Deploy to Production**
   - Trigger CD: Deploy to Production with the new artifact
   - Or merge hotfix to main to trigger automatic deployment

### Option 3: Environment Restore (Last Resort)

**Use when:** All else fails and you need to restore the entire environment state.

**WARNING:** This is destructive and rolls back ALL changes to the environment, not just your solution.

**Steps:**

1. Contact your Power Platform administrator
2. Request an environment restore to a specific point in time
3. Be aware this affects ALL solutions and data in the environment

---

## Prevention: Before You Need to Rollback

### 1. Always Test in QA First

Our pipeline automatically deploys to QA before production. Never bypass this.

### 2. Use Additive Schema Changes

Microsoft recommends additive schema changes that don't break existing data:

| Do | Don't |
|----|-------|
| Add new columns | Delete columns with data |
| Deprecate old columns | Rename columns |
| Add new option set values | Remove option set values in use |
| Create new entities | Delete entities with data |

### 3. Monitor Deployment Health

After deployment, verify:
- [ ] Solution import succeeded
- [ ] Plugins are executing correctly
- [ ] Flows are running
- [ ] Forms load without errors
- [ ] Business processes work as expected

---

## Version Comparison and Idempotency

Our import action includes smart version comparison:

```
Import version: 1.0.20251214.3
Target version: 1.0.20251215.5

Target has NEWER version → Skip import
```

**To force a rollback to an older version**, you must either:

1. **Increment the version** - Update version.txt and rebuild
2. **Disable version check** - Set `skip-if-same-version: false` in workflow dispatch

### Forcing an Older Version

If you need to deploy an older version:

1. Checkout the old tag
2. **Update version.txt** to a new, higher version:
   ```
   # Old version was 1.0.20251214.3
   # Current (broken) is 1.0.20251215.5
   # Set to: 1.0.20251216.1 (or current date)
   ```
3. Rebuild and deploy

This creates a "rollback release" that's actually a new version containing old code.

---

## Retry Logic and Transient Errors

Our import action includes smart retry logic. Understanding which errors retry helps diagnose issues:

### Transient Errors (Will Retry)

These errors trigger automatic retry with 5-minute delay (max 3 retries):

| Error Pattern | Cause |
|---------------|-------|
| "Cannot start another solution" | Another import in progress |
| "try again later" | Service temporarily unavailable |

### Deterministic Errors (Immediate Failure)

These errors fail immediately without retry:

| Error Pattern | Cause | Resolution |
|---------------|-------|------------|
| "File not found" | Solution zip missing | Check artifact download |
| "does not exist" | Referenced component missing | Check solution dependencies |
| "missing dependency" | Required solution not installed | Install dependencies first |
| "Missing component" | Component removed from solution | Add component back or use holding solution |
| "cannot be deleted" | Component in use | Remove references first |
| "cannot be updated" | Schema conflict | Check data compatibility |
| "access denied" | Permission issue | Check service principal permissions |
| "unauthorized" | Auth failed | Check credentials and token expiry |

### When Retries Succeed (Concurrent Import)

If another process was importing at the same time, our action re-checks the target version after retry delay. If the target now has the same or newer version, we consider it successful (another process completed our deployment).

---

## Emergency Contacts

| Situation | Contact |
|-----------|---------|
| Production deployment failed | DevOps team |
| Environment needs restore | Power Platform admin |
| Service principal issues | Azure AD admin |
| Data corruption suspected | DBA / Support team |

---

## See Also

- [BRANCHING_STRATEGY.md](../strategy/BRANCHING_STRATEGY.md) - Hotfix branch workflow
- [HOTFIX_GUIDE.md](HOTFIX_GUIDE.md) - Step-by-step hotfix process
- [PIPELINE_STRATEGY.md](../strategy/PIPELINE_STRATEGY.md) - CI/CD workflow details
- [Microsoft: Deployment failure mitigation](https://learn.microsoft.com/en-us/power-platform/well-architected/operational-excellence/mitigation-strategy)
