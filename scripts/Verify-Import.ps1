<#
.SYNOPSIS
    Verifies ppds-migrate import results against source data.

.DESCRIPTION
    Queries target Dataverse environment to verify imported records match
    expected counts and lookup relationships are correctly resolved.

.PARAMETER DataZip
    Path to the exported data.zip file containing source data.

.PARAMETER ConnectionString
    Dataverse connection string for target environment.

.PARAMETER OutputJson
    Optional path to write JSON verification report.

.EXAMPLE
    .\Verify-Import.ps1 -DataZip .\test-export.zip -ConnectionString "AuthType=..."
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DataZip,

    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,

    [Parameter(Mandatory = $false)]
    [string]$OutputJson
)

$ErrorActionPreference = 'Stop'

# Parse connection string
function Parse-ConnectionString {
    param([string]$cs)
    $result = @{}
    $cs -split ';' | ForEach-Object {
        if ($_ -match '(.+?)=(.+)') {
            $result[$matches[1].Trim()] = $matches[2].Trim()
        }
    }
    return $result
}

# Discover tenant ID from Dataverse instance
function Get-TenantId {
    param([string]$dataverseUrl)

    $wellKnownUrl = "$($dataverseUrl.TrimEnd('/'))/.well-known/openid-configuration"
    try {
        # Use WebRequest to capture redirect
        $request = [System.Net.HttpWebRequest]::Create($wellKnownUrl)
        $request.AllowAutoRedirect = $false
        $request.Method = "GET"
        $request.Timeout = 10000

        try {
            $response = $request.GetResponse()
        } catch [System.Net.WebException] {
            $response = $_.Exception.Response
        }

        # Check for redirect with tenant ID in URL
        if ($response.StatusCode -eq [System.Net.HttpStatusCode]::Found -or
            $response.StatusCode -eq [System.Net.HttpStatusCode]::Redirect) {
            $location = $response.Headers["Location"]
            if ($location -match 'login\.microsoftonline\.com/([0-9a-f-]{36})') {
                $response.Close()
                return $matches[1]
            }
        }
        $response.Close()
    } catch {
        Write-Host "  Discovery error: $_" -ForegroundColor Gray
    }

    # Fallback: try authorization challenge
    try {
        $apiUrl = "$($dataverseUrl.TrimEnd('/'))/api/data/v9.2/WhoAmI"
        $request = [System.Net.HttpWebRequest]::Create($apiUrl)
        $request.Method = "GET"
        $request.Timeout = 10000

        try { $request.GetResponse() } catch [System.Net.WebException] {
            $wwwAuth = $_.Exception.Response.Headers["WWW-Authenticate"]
            if ($wwwAuth -match 'authorization_uri="https://login\.microsoftonline\.com/([0-9a-f-]{36})') {
                return $matches[1]
            }
        }
    } catch {
        Write-Host "  Fallback error: $_" -ForegroundColor Gray
    }

    throw "Could not discover tenant ID from $dataverseUrl"
}

# Get OAuth token using client credentials
function Get-DataverseToken {
    param($config)

    $tenantId = Get-TenantId -dataverseUrl $config.Url
    Write-Host "  Tenant: $tenantId" -ForegroundColor Gray

    $tokenUrl = "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token"
    $scope = "$($config.Url.TrimEnd('/'))/.default"

    $body = @{
        grant_type    = "client_credentials"
        client_id     = $config.ClientId
        client_secret = $config.ClientSecret
        scope         = $scope
    }

    $response = Invoke-RestMethod -Uri $tokenUrl -Method Post -Body $body -ContentType "application/x-www-form-urlencoded"
    return $response.access_token
}

# Execute Web API request
function Invoke-DataverseApi {
    param(
        [string]$BaseUrl,
        [string]$Token,
        [string]$Query
    )

    $headers = @{
        "Authorization" = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
        "Accept" = "application/json"
        "Prefer" = "odata.include-annotations=*"
    }

    $url = "$($BaseUrl.TrimEnd('/'))/$Query"
    $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
    return $response
}

# Main verification logic
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  PPDS Import Verification" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Parse connection string
$config = Parse-ConnectionString -cs $ConnectionString
$baseUrl = "$($config.Url.TrimEnd('/'))/api/data/v9.2"

Write-Host "Target: $($config.Url)" -ForegroundColor Gray

# Get auth token
Write-Host "Authenticating..." -ForegroundColor Gray
$token = Get-DataverseToken -config $config
Write-Host "Authenticated successfully.`n" -ForegroundColor Green

# Read source data from zip
Write-Host "Reading source data from: $DataZip" -ForegroundColor Gray
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($DataZip)
$dataEntry = $zip.Entries | Where-Object { $_.Name -eq 'data.xml' }
$stream = $dataEntry.Open()
$reader = New-Object System.IO.StreamReader($stream)
$dataXml = [xml]$reader.ReadToEnd()
$reader.Close()
$stream.Close()
$zip.Dispose()

# Parse source record counts
$sourceAccounts = @($dataXml.entities.entity | Where-Object { $_.name -eq 'account' } | ForEach-Object { $_.records.record })
$sourceContacts = @($dataXml.entities.entity | Where-Object { $_.name -eq 'contact' } | ForEach-Object { $_.records.record })

Write-Host "Source records: $($sourceAccounts.Count) accounts, $($sourceContacts.Count) contacts`n" -ForegroundColor Gray

# Initialize results
$results = @{
    timestamp = (Get-Date).ToString('o')
    source = @{
        accounts = $sourceAccounts.Count
        contacts = $sourceContacts.Count
    }
    target = @{}
    checks = @()
    passed = $true
}

# Check 1: Account count
Write-Host "[Check 1] Account count..." -ForegroundColor Yellow
$accountsResponse = Invoke-DataverseApi -BaseUrl $baseUrl -Token $token -Query 'accounts?$select=accountid,name,parentaccountid&$filter=startswith(name,''PPDS-'')'
$targetAccounts = @($accountsResponse.value)
$results.target.accounts = $targetAccounts.Count

$check1 = @{
    name = "Account count"
    expected = $sourceAccounts.Count
    actual = $targetAccounts.Count
    passed = ($targetAccounts.Count -eq $sourceAccounts.Count)
}
$results.checks += $check1

if ($check1.passed) {
    Write-Host "  PASS: $($targetAccounts.Count) accounts found" -ForegroundColor Green
} else {
    Write-Host "  FAIL: Expected $($sourceAccounts.Count), found $($targetAccounts.Count)" -ForegroundColor Red
    $results.passed = $false
}

# Check 2: Contact count (contacts linked to PPDS accounts)
Write-Host "[Check 2] Contact count..." -ForegroundColor Yellow
# Get account IDs first, then find contacts linked to them
$accountIds = $targetAccounts | ForEach-Object { $_.accountid }
$allContacts = @()
foreach ($acctId in $accountIds) {
    $query = "contacts?`$select=contactid,fullname,_parentcustomerid_value&`$filter=_parentcustomerid_value eq $acctId"
    try {
        $resp = Invoke-DataverseApi -BaseUrl $baseUrl -Token $token -Query $query
        $allContacts += @($resp.value)
    } catch { }
}
$targetContacts = $allContacts
$results.target.contacts = $targetContacts.Count

$check2 = @{
    name = "Contact count"
    expected = $sourceContacts.Count
    actual = $targetContacts.Count
    passed = ($targetContacts.Count -eq $sourceContacts.Count)
}
$results.checks += $check2

if ($check2.passed) {
    Write-Host "  PASS: $($targetContacts.Count) contacts found" -ForegroundColor Green
} else {
    Write-Host "  FAIL: Expected $($sourceContacts.Count), found $($targetContacts.Count)" -ForegroundColor Red
    $results.passed = $false
}

# Check 3: Parent account lookups resolved
Write-Host "[Check 3] Parent account lookups..." -ForegroundColor Yellow
$accountsWithParent = @($targetAccounts | Where-Object { $_.'_parentaccountid_value' })
$orphanedParents = @()

foreach ($acct in $accountsWithParent) {
    $parentId = $acct.'_parentaccountid_value'
    $parentExists = $targetAccounts | Where-Object { $_.accountid -eq $parentId }
    if (-not $parentExists) {
        $orphanedParents += @{ account = $acct.name; parentId = $parentId }
    }
}

$check3 = @{
    name = "Parent account lookups"
    accountsWithParent = $accountsWithParent.Count
    orphaned = $orphanedParents.Count
    passed = ($orphanedParents.Count -eq 0)
}
$results.checks += $check3

if ($check3.passed) {
    Write-Host "  PASS: $($accountsWithParent.Count) parent lookups resolved" -ForegroundColor Green
} else {
    Write-Host "  FAIL: $($orphanedParents.Count) orphaned parent references" -ForegroundColor Red
    $results.passed = $false
}

# Check 4: Contact parentcustomerid lookups resolved
Write-Host "[Check 4] Contact company lookups..." -ForegroundColor Yellow
$contactsWithCompany = @($targetContacts | Where-Object { $_.'_parentcustomerid_value' })
$orphanedCompany = @()

foreach ($contact in $contactsWithCompany) {
    $companyId = $contact.'_parentcustomerid_value'
    $companyExists = $targetAccounts | Where-Object { $_.accountid -eq $companyId }
    if (-not $companyExists) {
        # Could also be a contact reference (polymorphic)
        $contactRef = $targetContacts | Where-Object { $_.contactid -eq $companyId }
        if (-not $contactRef) {
            $orphanedCompany += @{ contact = $contact.fullname; companyId = $companyId }
        }
    }
}

$check4 = @{
    name = "Contact company lookups"
    contactsWithCompany = $contactsWithCompany.Count
    orphaned = $orphanedCompany.Count
    passed = ($orphanedCompany.Count -eq 0)
}
$results.checks += $check4

if ($check4.passed) {
    Write-Host "  PASS: $($contactsWithCompany.Count) company lookups resolved" -ForegroundColor Green
} else {
    Write-Host "  FAIL: $($orphanedCompany.Count) orphaned company references" -ForegroundColor Red
    $results.passed = $false
}

# Check 5: Primary contact (deferred field) on accounts
Write-Host "[Check 5] Primary contact lookups (deferred)..." -ForegroundColor Yellow
$accountsQuery = 'accounts?$select=accountid,name,primarycontactid&$filter=startswith(name,''PPDS-'') and primarycontactid ne null'
try {
    $accountsWithPrimary = Invoke-DataverseApi -BaseUrl $baseUrl -Token $token -Query $accountsQuery
    $primaryCount = @($accountsWithPrimary.value).Count
} catch {
    $primaryCount = 0
}

$check5 = @{
    name = "Primary contact lookups (deferred)"
    count = $primaryCount
    passed = $true  # Info only - not all accounts have primary contacts
}
$results.checks += $check5
Write-Host "  INFO: $primaryCount accounts have primary contact set" -ForegroundColor Cyan

# Summary
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  VERIFICATION SUMMARY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$passedChecks = @($results.checks | Where-Object { $_.passed }).Count
$totalChecks = $results.checks.Count

if ($results.passed) {
    Write-Host "`n  RESULT: ALL CHECKS PASSED ($passedChecks/$totalChecks)" -ForegroundColor Green
} else {
    Write-Host "`n  RESULT: SOME CHECKS FAILED ($passedChecks/$totalChecks passed)" -ForegroundColor Red
}

Write-Host "`n  Source: $($results.source.accounts) accounts, $($results.source.contacts) contacts"
Write-Host "  Target: $($results.target.accounts) accounts, $($results.target.contacts) contacts`n"

# Output JSON if requested
if ($OutputJson) {
    $results | ConvertTo-Json -Depth 10 | Out-File -FilePath $OutputJson -Encoding UTF8
    Write-Host "Report saved to: $OutputJson" -ForegroundColor Gray
}

# Return results object
return $results
