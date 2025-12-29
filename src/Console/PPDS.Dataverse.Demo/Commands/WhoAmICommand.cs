using System.CommandLine;
using Microsoft.Crm.Sdk.Messages;
using PPDS.Dataverse.Demo.Infrastructure;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Tests connectivity by executing WhoAmI request.
/// </summary>
public static class WhoAmICommand
{
    public static Command Create()
    {
        var command = new Command("whoami", "Test connectivity with WhoAmI request");

        // Use standardized options from GlobalOptionsExtensions
        var envOption = GlobalOptionsExtensions.CreateEnvironmentOption();
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        command.AddOption(envOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (string? environment, bool verbose, bool debug) =>
        {
            var options = new GlobalOptions
            {
                Environment = environment,
                Verbose = verbose,
                Debug = debug
            };
            Environment.ExitCode = await ExecuteAsync(options);
        }, envOption, verboseOption, debugOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(GlobalOptions options)
    {
        ConsoleWriter.Header("Testing Dataverse Connectivity");

        using var host = HostFactory.CreateHostForMigration(options);
        var pool = HostFactory.GetConnectionPool(host, options.Environment);

        if (pool == null)
        {
            ConsoleWriter.Error("Connection pool not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        Console.WriteLine($"Environment: {options.Environment ?? "Dev (default)"}");
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
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }
}
