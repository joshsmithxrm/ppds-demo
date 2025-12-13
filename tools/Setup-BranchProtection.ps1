<#
.SYNOPSIS
    Configures branch rulesets for the repository.

.DESCRIPTION
    Sets up branch rulesets for 'main' and 'develop' branches using the GitHub CLI.
    Uses rulesets (not legacy branch protection) to enforce different merge strategies per branch:
    - develop: squash merge only
    - main: merge commit only (no squash)

    Requires: gh CLI authenticated with repo admin permissions.

.PARAMETER Owner
    Repository owner (user or organization). Defaults to current repo owner.

.PARAMETER Repo
    Repository name. Defaults to current repo name.

.PARAMETER WhatIf
    Shows what would be configured without making changes.

.EXAMPLE
    .\Setup-BranchProtection.ps1
    Configures branch rulesets for the current repository.

.EXAMPLE
    .\Setup-BranchProtection.ps1 -Owner "myorg" -Repo "myrepo" -WhatIf
    Shows what would be configured without making changes.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter()]
    [string]$Owner,

    [Parameter()]
    [string]$Repo,

    [Parameter()]
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

# =============================================================================
# Helper Functions
# =============================================================================

function Test-GhCli {
    try {
        $null = gh --version
        return $true
    }
    catch {
        return $false
    }
}

function Get-RepoInfo {
    $repoInfo = gh repo view --json owner,name 2>$null | ConvertFrom-Json
    if (-not $repoInfo) {
        throw "Could not determine repository. Run from a git repository or specify -Owner and -Repo."
    }
    return $repoInfo
}

function Set-Ruleset {
    param(
        [string]$Owner,
        [string]$Repo,
        [string]$Name,
        [hashtable]$Settings
    )

    $endpoint = "repos/$Owner/$Repo/rulesets"
    $body = $Settings | ConvertTo-Json -Depth 10 -Compress

    Write-Host "Configuring ruleset '$Name'..." -ForegroundColor Cyan

    if ($WhatIf) {
        Write-Host "  [WhatIf] Would apply settings:" -ForegroundColor Yellow
        $Settings | ConvertTo-Json -Depth 10 | Write-Host
        return
    }

    # Check if ruleset already exists (handle case where no rulesets exist or API errors)
    $existing = $null
    $listResult = gh api "$endpoint" 2>&1
    if ($LASTEXITCODE -eq 0 -and $listResult) {
        try {
            $existing = $listResult | ConvertFrom-Json | Where-Object { $_.name -eq $Name }
        } catch {
            Write-Host "  Warning: Could not parse existing rulesets, will attempt to create new" -ForegroundColor Yellow
        }
    }

    try {
        if ($existing) {
            # Update existing ruleset
            $updateEndpoint = "$endpoint/$($existing.id)"
            $result = $body | gh api $updateEndpoint -X PUT --input - 2>&1
        }
        else {
            # Create new ruleset
            $result = $body | gh api $endpoint -X POST --input - 2>&1
        }

        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Ruleset '$Name' configured" -ForegroundColor Green
        }
        else {
            throw $result
        }
    }
    catch {
        Write-Host "  Failed to configure '$Name': $_" -ForegroundColor Red
        throw
    }
}

function Set-RepoMergeSettings {
    param(
        [string]$Owner,
        [string]$Repo
    )

    Write-Host "Configuring repository merge settings..." -ForegroundColor Cyan

    if ($WhatIf) {
        Write-Host "  [WhatIf] Would enable: merge commits, squash merge" -ForegroundColor Yellow
        Write-Host "  [WhatIf] Would disable: rebase merge" -ForegroundColor Yellow
        return
    }

    try {
        $result = gh api "repos/$Owner/$Repo" -X PATCH `
            -f allow_merge_commit=true `
            -f allow_squash_merge=true `
            -f allow_rebase_merge=false `
            -f squash_merge_commit_title=PR_TITLE `
            -f squash_merge_commit_message=PR_BODY 2>&1

        if ($LASTEXITCODE -ne 0) {
            throw "API call failed: $result"
        }

        Write-Host "  Merge settings configured (merge + squash enabled, rebase disabled)" -ForegroundColor Green
    }
    catch {
        Write-Host "  Failed to configure merge settings: $_" -ForegroundColor Red
        throw
    }
}

# =============================================================================
# Ruleset Configurations
# =============================================================================

# Main branch - Production, merge commit only (no squash)
$mainRuleset = @{
    name = "Main Branch Rules"
    target = "branch"
    enforcement = "active"
    conditions = @{
        ref_name = @{
            include = @("refs/heads/main")
            exclude = @()
        }
    }
    rules = @(
        @{
            type = "deletion"
        },
        @{
            type = "required_status_checks"
            parameters = @{
                strict_required_status_checks_policy = $true
                do_not_enforce_on_create = $false
                required_status_checks = @(
                    @{ context = "Validation Status" }
                )
            }
        },
        @{
            type = "non_fast_forward"
        },
        @{
            type = "pull_request"
            parameters = @{
                required_approving_review_count = 1
                dismiss_stale_reviews_on_push = $true
                require_code_owner_review = $false
                require_last_push_approval = $true
                required_review_thread_resolution = $true
                allowed_merge_methods = @("merge")  # Merge commit only, no squash
            }
        }
    )
}

# Develop branch - Integration, squash merge only
$developRuleset = @{
    name = "Develop Branch Rules"
    target = "branch"
    enforcement = "active"
    conditions = @{
        ref_name = @{
            include = @("refs/heads/develop")
            exclude = @()
        }
    }
    rules = @(
        @{
            type = "deletion"
        },
        @{
            type = "required_status_checks"
            parameters = @{
                strict_required_status_checks_policy = $false
                do_not_enforce_on_create = $false
                required_status_checks = @(
                    @{ context = "Validation Status" }
                )
            }
        },
        @{
            type = "non_fast_forward"
        },
        @{
            type = "pull_request"
            parameters = @{
                required_approving_review_count = 1
                dismiss_stale_reviews_on_push = $true
                require_code_owner_review = $false
                require_last_push_approval = $false
                required_review_thread_resolution = $true
                allowed_merge_methods = @("squash")  # Squash only, no merge commit
            }
        }
    )
}

# =============================================================================
# Main Script
# =============================================================================

Write-Host ""
Write-Host "Branch Ruleset Setup" -ForegroundColor Cyan
Write-Host "====================" -ForegroundColor Cyan
Write-Host ""

# Verify gh CLI is available
if (-not (Test-GhCli)) {
    Write-Host "Error: GitHub CLI (gh) is not installed or not in PATH." -ForegroundColor Red
    Write-Host "Install from: https://cli.github.com/" -ForegroundColor Yellow
    exit 1
}

# Verify authentication
$authStatus = gh auth status 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Not authenticated with GitHub CLI." -ForegroundColor Red
    Write-Host "Run: gh auth login" -ForegroundColor Yellow
    exit 1
}

# Get repository info
if (-not $Owner -or -not $Repo) {
    $repoInfo = Get-RepoInfo
    $Owner = $repoInfo.owner.login
    $Repo = $repoInfo.name
}

Write-Host "Repository: $Owner/$Repo" -ForegroundColor White
Write-Host ""

if ($WhatIf) {
    Write-Host "[WhatIf Mode - No changes will be made]" -ForegroundColor Yellow
    Write-Host ""
}

# Configure repository merge settings first
Set-RepoMergeSettings -Owner $Owner -Repo $Repo
Write-Host ""

# Configure develop branch ruleset
Set-Ruleset -Owner $Owner -Repo $Repo -Name "Develop Branch Rules" -Settings $developRuleset
Write-Host ""

# Configure main branch ruleset
Set-Ruleset -Owner $Owner -Repo $Repo -Name "Main Branch Rules" -Settings $mainRuleset

Write-Host ""
Write-Host "Branch ruleset setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Key protections:" -ForegroundColor Cyan
Write-Host "  - develop: Squash merge ONLY (clean feature history)" -ForegroundColor White
Write-Host "  - main: Merge commit ONLY (preserve features, no squash)" -ForegroundColor White
Write-Host ""
Write-Host "Verify settings at:" -ForegroundColor Cyan
Write-Host "  https://github.com/$Owner/$Repo/settings/rules" -ForegroundColor White
Write-Host ""
