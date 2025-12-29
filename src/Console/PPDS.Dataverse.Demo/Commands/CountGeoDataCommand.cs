using System.CommandLine;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Displays record counts for geographic reference data tables.
/// Quick verification of geo data without running a full migration.
/// </summary>
public static class CountGeoDataCommand
{
    public static Command Create()
    {
        var command = new Command("count-geo-data", "Display record counts for geographic reference data");

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
        var env = options.Environment ?? "Dev";
        ConsoleWriter.Header($"Geographic Data Summary ({env})");

        using var host = HostFactory.CreateHostForMigration(options);
        var pool = HostFactory.GetConnectionPool(host, options.Environment);

        if (pool == null)
        {
            ConsoleWriter.Error($"{env} environment not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        try
        {
            await using var client = await pool.GetClientAsync();
            var summary = await QueryGeoSummary(client);

            Console.WriteLine($"  States:    {summary.StateCount:N0}");
            Console.WriteLine($"  Cities:    {summary.CityCount:N0}");
            Console.WriteLine($"  ZIP Codes: {summary.ZipCodeCount:N0}");
            Console.WriteLine($"  Total:     {summary.TotalCount:N0}");
            Console.WriteLine();

            return 0;
        }
        catch (Exception ex)
        {
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }

    private static async Task<GeoSummary> QueryGeoSummary(IPooledClient client)
    {
        var summary = new GeoSummary();

        // Query state count
        var stateQuery = new Microsoft.Xrm.Sdk.Query.QueryExpression("ppds_state")
        {
            ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false),
            PageInfo = new Microsoft.Xrm.Sdk.Query.PagingInfo { Count = 5000, PageNumber = 1 }
        };
        var stateResult = await client.RetrieveMultipleAsync(stateQuery);
        summary.StateCount = stateResult.Entities.Count;

        // Query city count (needs paging - 30k+ cities)
        var cityQuery = new Microsoft.Xrm.Sdk.Query.QueryExpression("ppds_city")
        {
            ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false),
            PageInfo = new Microsoft.Xrm.Sdk.Query.PagingInfo { Count = 5000, PageNumber = 1 }
        };
        var totalCities = 0;
        while (true)
        {
            var cityResult = await client.RetrieveMultipleAsync(cityQuery);
            totalCities += cityResult.Entities.Count;
            if (!cityResult.MoreRecords) break;
            cityQuery.PageInfo.PageNumber++;
            cityQuery.PageInfo.PagingCookie = cityResult.PagingCookie;
        }
        summary.CityCount = totalCities;

        // Query zipcode count (needs paging - 41k+ zips)
        var zipQuery = new Microsoft.Xrm.Sdk.Query.QueryExpression("ppds_zipcode")
        {
            ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false),
            PageInfo = new Microsoft.Xrm.Sdk.Query.PagingInfo { Count = 5000, PageNumber = 1 }
        };
        var totalZips = 0;
        while (true)
        {
            var zipResult = await client.RetrieveMultipleAsync(zipQuery);
            totalZips += zipResult.Entities.Count;
            if (!zipResult.MoreRecords) break;
            zipQuery.PageInfo.PageNumber++;
            zipQuery.PageInfo.PagingCookie = zipResult.PagingCookie;
        }
        summary.ZipCodeCount = totalZips;

        return summary;
    }

    private record GeoSummary
    {
        public int StateCount { get; set; }
        public int CityCount { get; set; }
        public int ZipCodeCount { get; set; }
        public int TotalCount => StateCount + CityCount + ZipCodeCount;
    }
}
