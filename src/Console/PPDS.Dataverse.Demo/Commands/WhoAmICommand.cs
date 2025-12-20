using System.CommandLine;
using Microsoft.Crm.Sdk.Messages;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Tests connectivity by executing WhoAmI request.
/// </summary>
public static class WhoAmICommand
{
    public static Command Create()
    {
        var command = new Command("whoami", "Test connectivity with WhoAmI request");

        command.SetHandler(async () =>
        {
            await ExecuteAsync(Array.Empty<string>());
        });

        return command;
    }

    public static async Task<int> ExecuteAsync(string[] args)
    {
        Console.WriteLine("Testing Dataverse Connectivity");
        Console.WriteLine("==============================");
        Console.WriteLine();

        using var host = CommandBase.CreateHost(args);
        var pool = CommandBase.GetConnectionPool(host);

        if (pool == null)
            return 1;

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
    }
}
