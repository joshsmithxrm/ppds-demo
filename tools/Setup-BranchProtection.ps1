<#
.SYNOPSIS
    Configures branch protection rules for the repository.

.DESCRIPTION
    Sets up branch protection rules for 'main' and 'develop' branches using the GitHub CLI.
    Requires: gh CLI authenticated with repo admin permissions.

.PARAMETER Owner
    Repository owner (user or organization). Defaults to current repo owner.

.PARAMETER Repo
    Repository name. Defaults to current repo name.

.PARAMETER WhatIf
    Shows what would be configured without making changes.

.EXAMPLE
    .\Setup-BranchProtection.ps1
    Configures branch protection for the current repository.

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

function Set-BranchProtection {
    param(
        [string]$Owner,
        [string]$Repo,
        [string]$Branch,
        [hashtable]$Settings
    )

    $endpoint = "repos/$Owner/$Repo/branches/$Branch/protection"
    $body = $Settings | ConvertTo-Json -Depth 10 -Compress

    Write-Host "Configuring protection for '$Branch' branch..." -ForegroundColor Cyan

    if ($WhatIf) {
        Write-Host "  [WhatIf] Would apply settings:" -ForegroundColor Yellow
        $Settings | ConvertTo-Json -Depth 10 | Write-Host
        return
    }

    try {
        $result = $body | gh api $endpoint -X PUT --input - 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Branch protection configured for '$Branch'" -ForegroundColor Green
        }
        else {
            throw $result
        }
    }
    catch {
        Write-Host "  Failed to configure '$Branch': $_" -ForegroundColor Red
        throw
    }
}

# =============================================================================
# Branch Protection Configurations
# =============================================================================

# Main branch - Production, strict protection
$mainProtection = @{
    required_status_checks = @{
        strict = $true
        contexts = @("Validation Status")
    }
    enforce_admins = $true
    required_pull_request_reviews = @{
        dismiss_stale_reviews = $true
        require_code_owner_reviews = $false
        required_approving_review_count = 1
    }
    restrictions = $null
    allow_force_pushes = $false
    allow_deletions = $false
    required_linear_history = $false
    required_conversation_resolution = $true
}

# Develop branch - Integration, allows automated commits
$developProtection = @{
    required_status_checks = @{
        strict = $false
        contexts = @("Validation Status")
    }
    enforce_admins = $false  # Allows GitHub Actions to push
    required_pull_request_reviews = @{
        dismiss_stale_reviews = $false
        require_code_owner_reviews = $false
        required_approving_review_count = 0
    }
    restrictions = $null
    allow_force_pushes = $false
    allow_deletions = $false
    required_linear_history = $false
    required_conversation_resolution = $false
}

# =============================================================================
# Main Script
# =============================================================================

Write-Host ""
Write-Host "Branch Protection Setup" -ForegroundColor Cyan
Write-Host "=======================" -ForegroundColor Cyan
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

# Configure main branch
Set-BranchProtection -Owner $Owner -Repo $Repo -Branch "main" -Settings $mainProtection

Write-Host ""

# Configure develop branch
Set-BranchProtection -Owner $Owner -Repo $Repo -Branch "develop" -Settings $developProtection

Write-Host ""
Write-Host "Branch protection setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Verify settings at:" -ForegroundColor Cyan
Write-Host "  https://github.com/$Owner/$Repo/settings/branches" -ForegroundColor White
Write-Host ""
