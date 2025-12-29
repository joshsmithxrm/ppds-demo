#!/usr/bin/env dotnet run
// =============================================================================
// Query Scratchpad - .NET 10 Single-File C# Script
// =============================================================================
//
// Quick Dataverse queries without a project file.
// Edit the query section below and run: dotnet run query.cs
//
// Uses the same .NET User Secrets as the demo app (UserSecretsId: ppds-dataverse-demo)
//
// =============================================================================

#:package Microsoft.PowerPlatform.Dataverse.Client@1.1.27
#:package Microsoft.Extensions.Configuration@9.0.0
#:package Microsoft.Extensions.Configuration.UserSecrets@9.0.0
#:package Microsoft.Extensions.Configuration.EnvironmentVariables@9.0.0

// Enable dynamic code generation (required by Dataverse SDK)
#:property PublishAot=false
#:property EnableTrimAnalyzer=false

using Microsoft.Extensions.Configuration;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

// Load connection from User Secrets (same as demo app)
var config = new ConfigurationBuilder()
    .AddUserSecrets("ppds-dataverse-demo")
    .AddEnvironmentVariables()
    .Build();

// Default to 'Dev' environment for scratchpad scripts
const string env = "Dev";
var url = config[$"Dataverse:Environments:{env}:Url"];
var clientId = config[$"Dataverse:Environments:{env}:Connections:0:ClientId"];
var clientSecret = config[$"Dataverse:Environments:{env}:Connections:0:ClientSecret"];

if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
{
    Console.WriteLine($"Connection not configured for '{env}' environment.");
    Console.WriteLine("See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md for setup instructions.");
    return;
}

var connectionString = $"AuthType=ClientSecret;Url={url};ClientId={clientId};ClientSecret={clientSecret}";

using var client = new ServiceClient(connectionString);

if (!client.IsReady)
{
    Console.WriteLine($"Connection failed: {client.LastError}");
    return;
}

Console.WriteLine($"Connected to {client.ConnectedOrgFriendlyName}");
Console.WriteLine();

// =============================================================================
// EDIT YOUR QUERY HERE
// =============================================================================

// Example 1: Simple query
var accounts = client.RetrieveMultiple(new QueryExpression("account")
{
    ColumnSet = new ColumnSet("name", "telephone1", "createdon"),
    TopCount = 10,
    Orders = { new OrderExpression("createdon", OrderType.Descending) }
});

Console.WriteLine($"Recent Accounts ({accounts.Entities.Count}):");
Console.WriteLine(new string('-', 60));
foreach (var account in accounts.Entities)
{
    var name = account.GetAttributeValue<string>("name") ?? "(no name)";
    var phone = account.GetAttributeValue<string>("telephone1") ?? "";
    var created = account.GetAttributeValue<DateTime?>("createdon")?.ToString("yyyy-MM-dd") ?? "";
    Console.WriteLine($"  {name,-35} {phone,-15} {created}");
}

Console.WriteLine();

// Example 2: FetchXML query
var fetchXml = @"
<fetch top='5'>
  <entity name='systemuser'>
    <attribute name='fullname' />
    <attribute name='internalemailaddress' />
    <filter>
      <condition attribute='isdisabled' operator='eq' value='0' />
    </filter>
    <order attribute='fullname' />
  </entity>
</fetch>";

var users = client.RetrieveMultiple(new FetchExpression(fetchXml));

Console.WriteLine($"Active Users ({users.Entities.Count}):");
Console.WriteLine(new string('-', 60));
foreach (var user in users.Entities)
{
    var name = user.GetAttributeValue<string>("fullname") ?? "(no name)";
    var email = user.GetAttributeValue<string>("internalemailaddress") ?? "";
    Console.WriteLine($"  {name,-30} {email}");
}
