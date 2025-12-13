# Power Platform Environment Setup Guide

This document covers setting up Power Platform developer environments from scratch, including Dataverse provisioning, environment creation, and CLI authentication.

## Prerequisites

- Power Platform Developer subscription (or M365 Developer Program subscription)
- Global Admin or Power Platform Admin role
- [Power Platform CLI](https://learn.microsoft.com/en-us/power-platform/developer/cli/introduction) installed

---

## 1. Obtain a Developer Subscription

### Option A: Power Apps Developer Plan (Free)
1. Go to [Power Apps Developer Plan](https://powerapps.microsoft.com/developerplan/)
2. Sign up with a work or school account
3. This creates a developer environment with Dataverse

### Option B: Microsoft 365 Developer Program (Free)
1. Go to [M365 Developer Program](https://developer.microsoft.com/microsoft-365/dev-program)
2. Join the program and set up a sandbox subscription
3. This provides a full M365 tenant with Power Platform capabilities
4. **Note:** Sandbox subscriptions expire after 90 days of inactivity

### Option C: Power Platform Trial (90 days)
1. Go to [Power Platform Admin Center](https://admin.powerplatform.microsoft.com)
2. Start a trial from the Environments section

---

## 2. Access Power Platform Admin Center

1. Navigate to: **https://admin.powerplatform.microsoft.com**
2. Sign in with your admin account
3. You should see the Admin Center dashboard

### First-Time Setup Checklist
- [ ] Verify your tenant name in Settings > Tenant details
- [ ] Note your Tenant ID (needed for some CLI operations)
- [ ] Check existing environments under Environments menu

### Quick Tip: Finding Your Environment Details

The easiest way to find all your environment identifiers is through **Power Apps Maker Portal**:

1. Go to [make.powerapps.com](https://make.powerapps.com)
2. Select your environment (top right)
3. Click the **Settings** gear icon (top right)
4. Click **Session details**

This shows you everything you need:

| Field | Description | Example |
|-------|-------------|---------|
| Tenant ID | Your Azure AD tenant | `34502e2f-89bb-...` |
| Object ID | Your user's object ID | `2868467f-1c73-...` |
| Organization ID | Dataverse org ID | `3a504f43-85d7-...` |
| Unique name | Org unique name | `unq3a504f43...` |
| Instance url | Dataverse URL | `https://orgXXXXX.crm.dynamics.com/` |
| Environment ID | Power Platform env ID | `8064eccb-3a2d-...` |

This is much faster than hunting through Admin Center!

---

## 3. Create Development Environment

### Step-by-Step:

1. In Admin Center, go to **Environments** in the left nav
2. Click **+ New** at the top
3. Fill in the environment details:

| Field | Value | Notes |
|-------|-------|-------|
| **Name** | `PPDS Demo - Dev` | Or your preferred naming |
| **Region** | Select nearest region | e.g., United States |
| **Type** | `Developer` or `Sandbox` | Developer = single user, Sandbox = team |
| **Purpose** | Development and testing | Optional description |
| **Create database** | `Yes` | **Critical** - enables Dataverse |

4. Click **Next** to configure Dataverse:

| Field | Value | Notes |
|-------|-------|-------|
| **Language** | English | Or preferred |
| **Currency** | USD | Or preferred |
| **Enable Dynamics 365 apps** | `No` | Unless you need D365 Sales/Service |
| **Deploy sample apps and data** | `No` | Keeps environment clean |
| **Security group** | None (or specific group) | Controls access |

5. Click **Save**
6. Wait 5-15 minutes for provisioning

### After Creation:
- Note the **Environment URL** (e.g., `https://org1234abcd.crm.dynamics.com`)
- Update your `.env.dev` file with this URL

---

## 4. Create QA Environment

Repeat Section 3 with these values:

| Field | Value |
|-------|-------|
| **Name** | `PPDS Demo - QA` |
| **Type** | `Developer` |
| **Purpose** | QA and integration testing |

> **Note:** With the free Power Apps Developer Plan, you can only create Developer-type environments (not Sandbox). Sandbox requires additional capacity from a trial or paid subscription. Developer environments work fine for ALM demos.

After creation, update `.env.qa` with the environment URL.

### Add Users to New Environments

New environments only grant access to the creator. Team members need to be explicitly added with appropriate security roles:

```bash
# Add admin users
pac admin assign-user \
  --environment <ENV_ID> \
  --user "admin@tenant.onmicrosoft.com" \
  --role "System Administrator"

# Add regular users
pac admin assign-user \
  --environment <ENV_ID> \
  --user "user@tenant.onmicrosoft.com" \
  --role "Basic User"
```

**Common security roles:**
| Role | Access Level |
|------|--------------|
| System Administrator | Full access to everything |
| System Customizer | Customize solution, no data access |
| Basic User | Read/write own records only |

> **Remember:** M365 admin roles (Global Admin, Power Platform Admin) manage environments at the tenant level but don't automatically grant access inside Dataverse. See Troubleshooting section for details.

---

## 5. Configure Power Platform CLI Authentication

### Install PAC CLI (if not installed)

```bash
# Using .NET tool (cross-platform)
dotnet tool install --global Microsoft.PowerApps.CLI.Tool

# Or download from:
# https://aka.ms/PowerAppsCLI
```

### Create Authentication Profiles

For each environment, create a named auth profile:

```bash
# Dev environment
pac auth create --name ppds-dev --url https://orgXXXXX.crm.dynamics.com

# QA environment
pac auth create --name ppds-qa --url https://orgYYYYY.crm.dynamics.com

# Demo environment (if applicable)
pac auth create --name ppds-demo --url https://orgZZZZZ.crm.dynamics.com
```

You'll be prompted to sign in via browser for each.

### List and Switch Profiles

```bash
# List all auth profiles
pac auth list

# Switch to a specific profile
pac auth select --name ppds-dev

# View current profile
pac auth who
```

### Service Principal Authentication (for CI/CD)

For automated deployments, create an App Registration via Azure CLI:

```bash
# 1. Create App Registration
az ad app create --display-name "PPDS-Pipeline-ServicePrincipal" --sign-in-audience AzureADMyOrg
# Note the appId from the output

# 2. Create Service Principal from App
az ad sp create --id <APP_ID>

# 3. Create client secret (2 year expiry)
az ad app credential reset --id <APP_ID> --append --display-name "Pipeline-Secret" --years 2
# SAVE THE PASSWORD - it won't be shown again!

# 4. Add Dynamics CRM API permission
az ad app permission add --id <APP_ID> \
  --api 00000007-0000-0000-c000-000000000000 \
  --api-permissions 78ce3f0f-a1ce-49c2-8cde-64b5c0896db4=Scope

# 5. Grant admin consent
az ad app permission grant --id <APP_ID> \
  --api 00000007-0000-0000-c000-000000000000 \
  --scope user_impersonation

# 6. Add as application user to each environment
pac admin assign-user \
  --environment <ENV_ID> \
  --user "<APP_ID>" \
  --role "System Administrator" \
  --application-user
```

Authenticate with Service Principal:

```bash
pac auth create --name ppds-pipeline \
  --applicationId <APP_ID> \
  --clientSecret <SECRET> \
  --tenant <TENANT_ID> \
  --url https://orgXXXXX.crm.dynamics.com/
```

**Important API GUIDs:**
- Dynamics CRM API: `00000007-0000-0000-c000-000000000000`
- user_impersonation scope: `78ce3f0f-a1ce-49c2-8cde-64b5c0896db4`

---

## 6. Verify Setup

### Test CLI Connection

```bash
# Select your dev profile
pac auth select --name ppds-dev

# List solutions in the environment
pac solution list

# Should show default solutions if Dataverse is working
```

### Test Environment Access

```bash
# Get environment details
pac org who

# Output should show:
# - Environment ID
# - Unique Name
# - Friendly Name
# - Organization URL
```

---

## 7. Environment Configuration Reference

### Our Environments

| Environment | Tenant | Purpose | Auth Profile |
|-------------|--------|---------|--------------|
| Dev | powerplatformdevelopersuite.onmicrosoft.com | Active development | `ppds-dev` |
| QA | powerplatformdevelopersuite.onmicrosoft.com | Testing & validation | `ppds-qa` |
| Prod | powerplatformdevelopersuite.onmicrosoft.com | Production deployment | `ppds-prod` |
| Demo | CRM384216.onmicrosoft.com | Demos & screenshots | `ppds-demo` |

### Credential Files

Credentials are stored in environment files (git-ignored):

```
.env.dev      # Dev environment credentials
.env.qa       # QA environment credentials
.env.prod     # Prod environment credentials
.env.demo     # Demo environment credentials
.env.example  # Template (safe to commit)
```

**Never commit actual credentials to git!**

---

## 8. Troubleshooting

### "Environment creation failed"
- Check your license/subscription limits
- Verify you have admin permissions
- Try a different region

### "Dataverse not available"
- Ensure you selected "Create database: Yes"
- Wait for provisioning to complete (can take 15+ minutes)
- Check environment status in Admin Center

### "Your org needs at least 1 GB of database capacity"
M365 E5 Developer licenses don't include Dataverse capacity by default. Solutions:

1. **Add Power Apps Developer Plan** (free, recommended):
   - Go to https://powerapps.microsoft.com/developerplan/
   - Sign in with your admin account
   - This adds capacity for developer environments

2. **Start a Power Apps Trial** (90 days):
   - In Admin Center, go to Environments → + New
   - Follow the prompts to start a trial

3. **Check Capacity**:
   - Admin Center → Resources → Capacity
   - View available database/file/log capacity

### "Not licensed for developer environments"
Even with M365 E5 Developer, you may need the specific Power Apps Developer Plan:
- Go to https://powerapps.microsoft.com/developerplan/
- Sign in and activate the developer plan
- This enables Developer-type environment creation

### "User has not been assigned any roles" / prvReadSolution
M365 admin roles (Global Admin, Power Platform Admin, D365 Admin) manage environments at the **tenant level** but don't grant access **inside** environments. Each Dataverse environment has its own security model.

**Solution:** Add users to each environment with Dataverse security roles:

```bash
# Add user with System Administrator role
pac admin assign-user \
  --environment ENVIRONMENT_ID \
  --user "user@tenant.onmicrosoft.com" \
  --role "System Administrator"

# Common security roles:
# - System Administrator: Full access
# - Basic User: Read/write own records
# - System Customizer: Customize but no data access
```

### "pac auth create hangs"
- Ensure popup blocker isn't blocking the auth window
- Try running from a fresh terminal
- Use `--deviceCode` flag for headless environments

### "Solution import failed"
- Verify target environment has required dependencies
- Check solution publisher matches or is compatible
- Review import job details in Solution History

### Azure CLI "Please run 'az login'" after logging in
Power Platform developer subscriptions don't include Azure subscriptions. When using Azure CLI for user/role management, you must use the `--allow-no-subscriptions` flag:

```bash
# This will fail (no Azure subscription in tenant):
az login --tenant yourtenant.onmicrosoft.com
# Result: "No subscriptions found" and subsequent commands fail

# This works:
az login --tenant yourtenant.onmicrosoft.com --allow-no-subscriptions
```

This allows Azure CLI to authenticate for Microsoft Graph operations (user management, role assignments) without requiring an Azure subscription.

---

## 9. User & Role Management via Azure CLI

Power Platform developer tenants can be managed via Azure CLI using Microsoft Graph API calls.

### Prerequisites
- Azure CLI installed (`az --version`)
- Logged in with `--allow-no-subscriptions` flag (see Troubleshooting above)

### Login to Tenant

```bash
# Login to your Power Platform tenant (no Azure subscription required)
az login --tenant yourtenant.onmicrosoft.com --allow-no-subscriptions
```

### List Users

```bash
# List all users
az ad user list --query "[].{displayName:displayName, userPrincipalName:userPrincipalName, id:id}" --output table
```

### Create a User

```bash
# Create a new user
az ad user create \
  --display-name "Power Platform Admin" \
  --user-principal-name powerplatformadmin@yourtenant.onmicrosoft.com \
  --password "TempP@ssw0rd123!" \
  --force-change-password-next-sign-in true

# IMPORTANT: Set usage location before assigning licenses
# (Required for license assignment - use ISO country code)
az rest --method PATCH \
  --url "https://graph.microsoft.com/v1.0/users/USER_UPN" \
  --headers "Content-Type=application/json" \
  --body '{"usageLocation": "US"}'
```

### Assign Licenses

```bash
# List available licenses in tenant
az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/subscribedSkus" \
  --query "value[].{sku:skuPartNumber, id:skuId, used:consumedUnits, total:prepaidUnits.enabled}" \
  --output table

# Assign license to user (requires usageLocation to be set first)
az rest --method POST \
  --url "https://graph.microsoft.com/v1.0/users/USER_UPN/assignLicense" \
  --headers "Content-Type=application/json" \
  --body '{"addLicenses": [{"skuId": "LICENSE_SKU_ID"}], "removeLicenses": []}'
```

### Delete a User

```bash
# Delete by UPN
az ad user delete --id user@yourtenant.onmicrosoft.com
```

### View User's Role Assignments

```bash
# Get roles assigned to a user (via Graph API)
az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/users/USER_UPN/memberOf" \
  --output json
```

### Assign Admin Roles

Admin roles must be activated in the tenant first before they can be assigned. Use `az rest` with Microsoft Graph:

```bash
# Get available directory roles
az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/directoryRoles" \
  --output table

# Get role template IDs (for activating roles)
az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/directoryRoleTemplates" \
  --output json

# Assign a user to a role (role must be activated first)
az rest --method POST \
  --url "https://graph.microsoft.com/v1.0/directoryRoles/ROLE_ID/members/\$ref" \
  --headers "Content-Type=application/json" \
  --body '{"@odata.id": "https://graph.microsoft.com/v1.0/users/USER_ID"}'
```

### Common Role Template IDs

| Role | Template ID |
|------|-------------|
| Global Administrator | `62e90394-69f5-4237-9190-012177145e10` |
| Power Platform Administrator | `11648597-926c-4cf3-9c36-bcebb0ba8dcc` |
| Dynamics 365 Administrator | `44367163-eba1-44c3-98af-f5787879f96a` |
| User Administrator | `fe930be7-5e62-47db-91af-98c3a49a38b1` |

---

## 10. Appendix: Useful Commands

```bash
# Environment operations
pac admin list                    # List all environments
pac org select --environment <id> # Select environment by ID

# Solution operations
pac solution list                 # List solutions
pac solution export --name PPDSDemo --path ./exports
pac solution import --path ./exports/PPDSDemo.zip

# Connection operations
pac auth list                     # List auth profiles
pac auth delete --name <profile>  # Remove a profile
pac auth clear                    # Remove all profiles
```
