# Branching Strategy

This document defines our Git branching model and workflow for Power Platform solution development.

---

## Branch Overview

We use a simplified GitFlow model with two primary branches.

| Branch | Purpose | Protected | Deploys To |
|--------|---------|-----------|------------|
| `main` | Production-ready code | Yes | Prod |
| `develop` | Integration branch | Yes | QA |
| `feature/*` | Feature development | No | - |
| `hotfix/*` | Emergency fixes | No | - |

---

## Branch Flow

```mermaid
graph LR
    subgraph "Feature Development"
        F1[feature/add-validation]
        F2[feature/new-entity]
    end

    subgraph "Integration"
        DEV[develop]
    end

    subgraph "Production"
        MAIN[main]
    end

    F1 -->|PR| DEV
    F2 -->|PR| DEV
    DEV -->|PR| MAIN

    DEV -.->|auto-deploy| QA[QA Env]
    MAIN -.->|auto-deploy| PROD[Prod Env]
```

---

## Branch Details

### `main` Branch

**Purpose:** Represents production-ready code. Every commit to `main` should be deployable to production.

**Rules:**
- Protected branch (no direct commits)
- Requires pull request from `develop`
- Requires at least one approval
- All CI checks must pass

**Deployment:** Pushes to `main` trigger deployment to Production environment.

---

### `develop` Branch

**Purpose:** Integration branch where features are combined and tested before release.

**Rules:**
- Protected branch
- Receives automated exports from Dev environment (nightly)
- Receives pull requests from feature branches
- Can receive direct commits from automated export pipeline

**Deployment:** Pushes to `develop` trigger deployment to QA environment.

---

### `feature/*` Branches

**Purpose:** Isolated development of specific features or changes.

**Naming:** `feature/{short-description}`

**Examples:**
```
feature/add-account-validation
feature/new-contact-form
feature/update-business-rules
```

**Workflow:**
1. Create from `develop`
2. Make changes in Dev environment
3. Export and commit to feature branch
4. Create PR to `develop`
5. Delete after merge

---

### `hotfix/*` Branches

**Purpose:** Emergency fixes that need to go directly to production.

**Naming:** `hotfix/{issue-description}`

**Examples:**
```
hotfix/fix-critical-workflow
hotfix/security-patch
```

**Workflow:**
1. Create from `main`
2. Make minimal fix
3. PR to `main` (for immediate production deployment)
4. Cherry-pick or merge back to `develop`
5. Delete after merge

---

## Daily Workflow

### Automated Export (Nightly)

```mermaid
sequenceDiagram
    participant Dev as Dev Env
    participant GH as GitHub Actions
    participant Develop as develop branch
    participant QA as QA Env

    Note over Dev,QA: Nightly at 2 AM UTC
    GH->>Dev: Export solution
    GH->>Develop: Commit changes
    GH->>QA: Deploy managed solution
```

### Feature Development

```mermaid
sequenceDiagram
    participant Maker as Maker
    participant Dev as Dev Env
    participant Feature as feature/* branch
    participant Develop as develop branch

    Maker->>Dev: Make changes
    Maker->>Feature: Export & commit
    Maker->>Develop: Create PR
    Note over Develop: Review & merge
    Develop->>Feature: Delete branch
```

### Production Release

```mermaid
sequenceDiagram
    participant QA as QA Team
    participant Develop as develop branch
    participant Main as main branch
    participant Prod as Prod Env

    QA->>QA: Validate in QA environment
    QA->>Main: Create PR from develop
    Note over Main: Review & approve
    Main->>Prod: Auto-deploy to production
```

---

## Pull Request Requirements

### PR to `develop`

| Requirement | Required? |
|-------------|-----------|
| CI pipeline passes | Yes |
| At least 1 approval | Recommended |
| No merge conflicts | Yes |
| Linked work item | Optional |

### PR to `main`

| Requirement | Required? |
|-------------|-----------|
| CI pipeline passes | Yes |
| At least 1 approval | Yes |
| QA sign-off | Yes |
| No merge conflicts | Yes |
| All conversations resolved | Yes |

---

## Merge Strategy

We use different merge strategies for different branch flows to optimize history clarity.

### Squash Merge: Feature → Develop

**Use squash merge** when merging feature branches into `develop`.

```
feature/add-validation (12 commits) → develop (1 squashed commit)
```

**Why squash:**
| Reason | Explanation |
|--------|-------------|
| Clean history | Feature branches have noisy commits ("WIP", "fix typo", "try again") |
| Atomic features | Each feature = one commit, easy to identify and revert |
| Power Platform | Solution exports create many small commits; squashing cleans this up |
| PR preserves detail | Granular commits still visible in closed PR if needed |

**GitHub setting:** Repository Settings → Pull Requests → Allow squash merging ✓

---

### Regular Merge: Develop → Main

**Use regular merge** (merge commit) when merging `develop` into `main`.

```
develop → main (merge commit preserves all feature commits)
```

**Why regular merge:**
| Reason | Explanation |
|--------|-------------|
| Preserves features | Each squashed feature commit flows through to main |
| Release boundaries | Merge commit marks exactly when a release happened |
| Traceability | "Prod broke" → Which release? → Which feature? → Easy to trace |
| Selective revert | Can revert one feature without reverting entire release |

**GitHub setting:** Repository Settings → Pull Requests → Allow merge commits ✓

---

### Why NOT Squash Both Ways?

If you squash `develop` → `main`:

```
❌ BAD: Squash develop to main
main:
├── Release 5 (one giant commit with 10 features mixed together)
├── Release 4 (one giant commit with 8 features)
└── Release 3 (one giant commit)

Problems:
- "Which feature broke prod?" - Can't tell, all mixed together
- "Revert just account validation" - Can't, it's mixed with other features
- Loss of audit trail
```

```
✅ GOOD: Regular merge develop to main
main:
├── Merge develop → main (Release 5)
│   ├── feat: add account validation
│   ├── feat: new contact form
│   └── fix: workflow error
├── Merge develop → main (Release 4)
│   ├── feat: dashboard updates
│   └── feat: reporting changes

Benefits:
- Clear release boundaries (merge commits)
- Feature-level granularity preserved
- Can revert specific features OR entire releases
```

---

## Branch Protection Rules

### `main` Branch

```yaml
# GitHub branch protection settings
protection:
  required_pull_request_reviews:
    required_approving_review_count: 1
    dismiss_stale_reviews: true
  required_status_checks:
    strict: true
    contexts:
      - "deploy-to-qa / deploy"  # CD: Deploy to QA workflow
  enforce_admins: true
  restrictions: null  # Anyone with write access can merge after approval
```

### `develop` Branch

```yaml
protection:
  required_pull_request_reviews:
    required_approving_review_count: 0  # Optional for feature PRs
  required_status_checks:
    strict: false
    contexts: []
  enforce_admins: false
  allow_force_pushes: false
  allow_deletions: false
```

---

## Commit Message Convention

Follow conventional commits for clear history:

```
<type>: <short description>

[optional body]

[optional footer]
```

**Types:**
| Type | Description |
|------|-------------|
| `feat` | New feature or component |
| `fix` | Bug fix |
| `docs` | Documentation changes |
| `chore` | Maintenance, dependencies |
| `refactor` | Code restructuring |

**Examples:**
```
feat: add account validation plugin
fix: correct status transition in workflow
docs: update deployment guide
chore: sync solution from Dev environment
```

---

## When to Deviate

### Add Release Branches When:
- You need to maintain multiple production versions
- Formal release cycles require stabilization periods
- Hotfixes need isolation from ongoing development

### Add Environment-Specific Branches When:
- Multiple long-lived environments need different configurations
- UAT requires extended testing periods
- Regulatory requirements mandate branch-per-environment

### Skip Feature Branches When:
- Solo developer working on simple changes
- Automated exports are the only commits
- Changes are trivial (typos, config adjustments)

---

## 🔗 See Also

- [ALM_OVERVIEW.md](ALM_OVERVIEW.md) - High-level ALM philosophy
- [ENVIRONMENT_STRATEGY.md](ENVIRONMENT_STRATEGY.md) - Environment configuration
- [PIPELINE_STRATEGY.md](PIPELINE_STRATEGY.md) - CI/CD implementation
- [Atlassian GitFlow Guide](https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow) - GitFlow reference
