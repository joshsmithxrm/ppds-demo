#!/usr/bin/env dotnet run
// =============================================================================
// WhoAmI Scratchpad - .NET 10 Single-File C# Script
// =============================================================================
//
// Run with: dotnet run whoami.cs
//
// Uses the same .NET User Secrets as the demo app (UserSecretsId: ppds-dataverse-demo)
// No additional configuration needed if you've already set up the demo app.
//
// See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md for configuration details.
//
// =============================================================================

#:package Microsoft.PowerPlatform.Dataverse.Client@1.1.27
#:package Microsoft.Extensions.Configuration@9.0.0
#:package Microsoft.Extensions.Configuration.UserSecrets@9.0.0
#:package Microsoft.Extensions.Configuration.EnvironmentVariables@9.0.0

// Enable dynamic code generation (required by Dataverse SDK)
#:property PublishAot=false
#:property EnableTrimAnalyzer=false

using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.PowerPlatform.Dataverse.Client;

// Load connection from User Secrets (same as demo app)
var config = new ConfigurationBuilder()
    .AddUserSecrets("ppds-dataverse-demo")  // Same UserSecretsId as demo project
    .AddEnvironmentVariables()               // Fallback to env vars
    .Build();

// Default to 'Dev' environment for scratchpad scripts
const string env = "Dev";
var url = config[$"Dataverse:Environments:{env}:Url"];
var clientId = config[$"Dataverse:Environments:{env}:Connections:0:ClientId"];
var clientSecret = config[$"Dataverse:Environments:{env}:Connections:0:ClientSecret"];

if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"Connection not configured for '{env}' environment!");
    Console.WriteLine();
    Console.ResetColor();
    Console.WriteLine("Configure using .NET User Secrets:");
    Console.WriteLine();
    Console.WriteLine("  cd src/Console/PPDS.Dataverse.Demo");
    Console.WriteLine($"  dotnet user-secrets set \"Dataverse:Environments:{env}:Url\" \"https://yourorg.crm.dynamics.com\"");
    Console.WriteLine($"  dotnet user-secrets set \"Dataverse:Environments:{env}:Connections:0:ClientId\" \"your-client-id\"");
    Console.WriteLine($"  dotnet user-secrets set \"Dataverse:Environments:{env}:Connections:0:ClientSecret\" \"your-secret\"");
    Console.WriteLine();
    Console.WriteLine("See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md for details.");
    Console.WriteLine();
    return;
}

var connectionString = $"AuthType=ClientSecret;Url={url};ClientId={clientId};ClientSecret={clientSecret}";

Console.WriteLine("Connecting to Dataverse...");
Console.WriteLine();

try
{
    using var client = new ServiceClient(connectionString);

    if (!client.IsReady)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Connection failed: {client.LastError}");
        Console.ResetColor();
        return;
    }

    // Execute WhoAmI
    var response = (WhoAmIResponse)client.Execute(new WhoAmIRequest());

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Connected Successfully!");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine($"  Organization ID:   {response.OrganizationId}");
    Console.WriteLine($"  Business Unit ID:  {response.BusinessUnitId}");
    Console.WriteLine($"  User ID:           {response.UserId}");
    Console.WriteLine();
    Console.WriteLine($"  Environment:       {client.ConnectedOrgUriActual}");
    Console.WriteLine($"  Organization:      {client.ConnectedOrgFriendlyName}");
    Console.WriteLine($"  Version:           {client.ConnectedOrgVersion}");
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Error: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner: {ex.InnerException.Message}");
    }
    Console.ResetColor();
}
