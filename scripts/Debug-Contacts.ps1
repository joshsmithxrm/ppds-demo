<#
Quick debug script to check contacts in Dataverse
#>
param([string]$ConnectionString)

# Parse connection string
$config = @{}
$ConnectionString -split ';' | ForEach-Object {
    if ($_ -match '(.+?)=(.+)') { $config[$matches[1].Trim()] = $matches[2].Trim() }
}

# Get tenant from redirect
$wellKnownUrl = "$($config.Url.TrimEnd('/'))/.well-known/openid-configuration"
$request = [System.Net.HttpWebRequest]::Create($wellKnownUrl)
$request.AllowAutoRedirect = $false
try { $response = $request.GetResponse() } catch { $response = $_.Exception.Response }
$location = $response.Headers["Location"]
$response.Close()
$tenantId = if ($location -match 'login\.microsoftonline\.com/([0-9a-f-]{36})') { $matches[1] } else { throw "No tenant" }

# Get token
$tokenUrl = "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token"
$body = @{
    grant_type = "client_credentials"
    client_id = $config.ClientId
    client_secret = $config.ClientSecret
    scope = "$($config.Url.TrimEnd('/'))/.default"
}
$tokenResponse = Invoke-RestMethod -Uri $tokenUrl -Method Post -Body $body -ContentType "application/x-www-form-urlencoded"
$token = $tokenResponse.access_token

# Query contacts - NO FILTER first
$headers = @{
    "Authorization" = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Accept" = "application/json"
}
$baseUrl = "$($config.Url.TrimEnd('/'))/api/data/v9.2"

Write-Host "Querying ALL contacts (top 20)..."
$response = Invoke-RestMethod -Uri "$baseUrl/contacts?`$select=contactid,fullname,firstname,lastname&`$top=20" -Headers $headers
Write-Host "Total contacts in response: $($response.value.Count)"
$response.value | ForEach-Object { Write-Host "  - $($_.fullname)" }

Write-Host "`nQuerying with PPDS filter..."
$response2 = Invoke-RestMethod -Uri "$baseUrl/contacts?`$select=contactid,fullname&`$filter=startswith(fullname,'PPDS-')" -Headers $headers
Write-Host "Filtered count: $($response2.value.Count)"
$response2.value | ForEach-Object { Write-Host "  - $($_.fullname)" }

Write-Host "`nQuerying with contains filter..."
$response3 = Invoke-RestMethod -Uri "$baseUrl/contacts?`$select=contactid,fullname&`$filter=contains(fullname,'PPDS')" -Headers $headers
Write-Host "Contains count: $($response3.value.Count)"
$response3.value | ForEach-Object { Write-Host "  - $($_.fullname)" }
