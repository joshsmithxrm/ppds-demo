# Branch Protection Setup Guide

Step-by-step instructions for configuring branch protection rules in GitHub.

---

## Quick Setup (Script)

Run the PowerShell script for automated setup:

```powershell
# From repository root
.\tools\Setup-BranchProtection.ps1

# Preview changes without applying
.\tools\Setup-BranchProtection.ps1 -WhatIf
```

**Requirements:**
- GitHub CLI (`gh`) installed and authenticated
- Admin permissions on the repository

---

## Manual Setup (GitHub UI)

### Step 1: Navigate to Branch Settings

1. Go to your repository on GitHub
2. Click **Settings** → **Branches**
3. Under "Branch protection rules", click **Add rule**

---

### Step 2: Configure `main` Branch

**Branch name pattern:** `main`

#### Pull Request Settings

| Setting | Value |
|---------|-------|
| ☑ Require a pull request before merging | Enabled |
| ☑ Require approvals | 1 |
| ☑ Dismiss stale pull request approvals when new commits are pushed | Enabled |
| ☐ Require review from Code Owners | Disabled |
| ☑ Require conversation resolution before merging | Enabled |

#### Status Check Settings

| Setting | Value |
|---------|-------|
| ☑ Require status checks to pass before merging | Enabled |
| ☑ Require branches to be up to date before merging | Enabled |
| Status checks that are required | `Validation Status` |

> **Note:** The `Validation Status` check appears after the PR validation workflow has run at least once.

#### Additional Settings

| Setting | Value |
|---------|-------|
| ☑ Do not allow bypassing the above settings | Enabled |
| ☐ Allow force pushes | Disabled |
| ☐ Allow deletions | Disabled |

Click **Create** to save the rule.

---

### Step 3: Configure `develop` Branch

**Branch name pattern:** `develop`

#### Pull Request Settings

| Setting | Value |
|---------|-------|
| ☑ Require a pull request before merging | Enabled |
| ☐ Require approvals | Disabled (0 required) |
| ☐ Dismiss stale pull request approvals | Disabled |
| ☐ Require review from Code Owners | Disabled |
| ☐ Require conversation resolution | Disabled |

#### Status Check Settings

| Setting | Value |
|---------|-------|
| ☑ Require status checks to pass before merging | Enabled |
| ☐ Require branches to be up to date before merging | Disabled |
| Status checks that are required | `Validation Status` |

> **Important:** "Require branches to be up to date" must be **disabled** for develop. The nightly export pipeline commits directly to develop, and strict mode would cause conflicts.

#### Additional Settings

| Setting | Value |
|---------|-------|
| ☐ Do not allow bypassing the above settings | Disabled |
| ☐ Allow force pushes | Disabled |
| ☐ Allow deletions | Disabled |

> **Important:** "Do not allow bypassing" must be **disabled** for develop. This allows GitHub Actions to push commits from the nightly export pipeline.

Click **Create** to save the rule.

---

## Verification

After setup, verify protection is working:

### Test 1: Direct Push Blocked

```bash
# This should fail on main
git checkout main
echo "test" >> README.md
git commit -am "test direct push"
git push origin main
# Expected: rejected (protected branch)
```

### Test 2: PR Required

```bash
# Create a test branch
git checkout -b test/protection-check
echo "test" >> README.md
git commit -am "test PR requirement"
git push origin test/protection-check
# Create PR via GitHub UI - should require status checks
```

### Test 3: Status Checks Required

1. Create a PR to `main` or `develop`
2. Verify the "Validation Status" check appears
3. The merge button should be disabled until checks pass

---

## Troubleshooting

### "Validation Status" check not appearing

The status check must run at least once before it appears in the dropdown.

**Fix:** Create a test PR that modifies solution or code files to trigger the workflow.

### Nightly export failing on develop

If the export pipeline can't push to develop:

1. Verify "Do not allow bypassing" is **disabled**
2. Verify `enforce_admins` is `false` in API settings
3. Check the workflow has `contents: write` permission

### Admin can't merge without approval

If "Do not allow bypassing" is enabled, even admins need approval.

**For main:** This is intentional (production safety)
**For develop:** Disable this setting

---

## GitHub API Reference

For automation or CI/CD setup, use the GitHub API:

```bash
# Get current protection rules
gh api repos/{owner}/{repo}/branches/main/protection

# Update protection (see tools/Setup-BranchProtection.ps1 for full example)
gh api repos/{owner}/{repo}/branches/main/protection -X PUT --input protection.json
```

API Documentation: [Branch Protection](https://docs.github.com/en/rest/branches/branch-protection)

---

## See Also

- [BRANCHING_STRATEGY.md](../strategy/BRANCHING_STRATEGY.md) - Branch strategy and rationale
- [tools/Setup-BranchProtection.ps1](../../tools/Setup-BranchProtection.ps1) - Automated setup script
- [GitHub Docs: Protected Branches](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches)
