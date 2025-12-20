using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PPDS.Dataverse.Pooling;

Console.WriteLine("PPDS.Dataverse Connection Pool Demo");
Console.WriteLine("====================================");
Console.WriteLine();

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
    Console.WriteLine("Please configure a connection string in appsettings.Development.json:");
    Console.WriteLine();
    Console.ResetColor();
    Console.WriteLine("""
    {
      "Dataverse": {
        "Connections": [
          {
            "Name": "Primary",
            "ConnectionString": "AuthType=ClientSecret;Url=https://yourorg.crm.dynamics.com;ClientId=xxx;ClientSecret=xxx"
          }
        ]
      }
    }
    """);
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
