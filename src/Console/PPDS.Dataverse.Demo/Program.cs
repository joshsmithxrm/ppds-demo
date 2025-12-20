using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PPDS.Dataverse.DependencyInjection;
using PPDS.Dataverse.Pooling;

Console.WriteLine("PPDS.Dataverse Connection Pool Demo");
Console.WriteLine("====================================");
Console.WriteLine();

// Host.CreateDefaultBuilder automatically includes:
// - appsettings.json
// - appsettings.{Environment}.json
// - User secrets (when DOTNET_ENVIRONMENT=Development)
// - Environment variables
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddDataverseConnectionPool(context.Configuration);
    })
    .Build();

var pool = host.Services.GetRequiredService<IDataverseConnectionPool>();

// Validate configuration
if (!pool.IsEnabled)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Error: Connection pool is not enabled.");
    Console.WriteLine();
    Console.ResetColor();
    Console.WriteLine("Configure using .NET User Secrets (recommended):");
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("  dotnet user-secrets set \"Dataverse:Connections:0:Name\" \"Primary\"");
    Console.WriteLine("  dotnet user-secrets set \"Dataverse:Connections:0:ConnectionString\" \"AuthType=ClientSecret;Url=...\"");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("Or set environment variable Dataverse__Connections__0__ConnectionString");
    Console.WriteLine();
    return 1;
}

Console.WriteLine("Connecting to Dataverse...");
Console.WriteLine();

try
{
    await using var client = await pool.GetClientAsync();

    var request = new WhoAmIRequest();
    var response = (WhoAmIResponse)await client.ExecuteAsync(request);

    Console.WriteLine("WhoAmI Result:");
    Console.WriteLine($"  User ID:         {response.UserId}");
    Console.WriteLine($"  Organization ID: {response.OrganizationId}");
    Console.WriteLine($"  Business Unit:   {response.BusinessUnitId}");
    Console.WriteLine();

    // Display pool statistics
    var stats = pool.Statistics;
    Console.WriteLine("Pool Statistics:");
    Console.WriteLine($"  Total Connections: {stats.TotalConnections}");
    Console.WriteLine($"  Active:            {stats.ActiveConnections}");
    Console.WriteLine($"  Idle:              {stats.IdleConnections}");
    Console.WriteLine($"  Requests Served:   {stats.RequestsServed}");

    if (stats.ThrottledConnections > 0)
    {
        Console.WriteLine($"  Throttled:         {stats.ThrottledConnections}");
    }

    Console.WriteLine();
    return 0;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Error: {ex.Message}");
    Console.ResetColor();
    Console.WriteLine();
    return 1;
}
