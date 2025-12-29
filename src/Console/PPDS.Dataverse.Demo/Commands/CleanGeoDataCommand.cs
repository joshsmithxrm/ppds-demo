using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk.Query;
using PPDS.Dataverse.BulkOperations;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Dataverse.Pooling;
using PPDS.Dataverse.Progress;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Bulk deletes geographic reference data for clean volume testing.
/// Deletes in dependency order: ZIP codes → cities → states.
/// Uses IBulkOperationExecutor.DeleteMultipleAsync for optimal throughput with
/// connection pooling, throttle-aware routing, and progress reporting.
/// </summary>
public static class CleanGeoDataCommand
{
    public static Command Create()
    {
        var command = new Command("clean-geo-data", "Bulk delete geographic reference data");

        var zipOnlyOption = new Option<bool>(
            "--zip-only",
            "Only delete ZIP codes (preserve states)");

        var confirmOption = new Option<bool>(
            "--confirm",
            "Skip confirmation prompt");

        // Use standardized options from GlobalOptionsExtensions
        var envOption = GlobalOptionsExtensions.CreateEnvironmentOption();
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();
        var parallelismOption = GlobalOptionsExtensions.CreateParallelismOption();

        command.AddOption(zipOnlyOption);
        command.AddOption(confirmOption);
        command.AddOption(envOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);
        command.AddOption(parallelismOption);

        command.SetHandler(async (bool zipOnly, bool confirm, string? environment, bool verbose, bool debug, int? parallelism) =>
        {
            var options = new GlobalOptions
            {
                Environment = environment,
                Verbose = verbose,
                Debug = debug,
                Parallelism = parallelism
            };
            Environment.ExitCode = await ExecuteAsync(zipOnly, confirm, options);
        }, zipOnlyOption, confirmOption, envOption, verboseOption, debugOption, parallelismOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(
        bool zipOnly,
        bool confirm,
        GlobalOptions options)
    {
        ConsoleWriter.Header("Clean Geographic Data");

        // Create host with SDK services for bulk operations
        using var host = HostFactory.CreateHostForBulkOperations(options);
        var pool = host.Services.GetRequiredService<IDataverseConnectionPool>();
        var bulkExecutor = host.Services.GetRequiredService<IBulkOperationExecutor>();

        if (!pool.IsEnabled)
        {
            ConsoleWriter.Error("Connection pool not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        Console.WriteLine($"  Environment: {options.Environment ?? "Dev (default)"}");

        if (options.Parallelism.HasValue)
        {
            Console.WriteLine($"  Parallelism: {options.Parallelism.Value}");
        }
        if (options.Debug)
        {
            Console.WriteLine("  Logging: Debug (diagnostic details)");
        }
        else if (options.Verbose)
        {
            Console.WriteLine("  Logging: Verbose (operational messages)");
        }
        Console.WriteLine();

        var totalStopwatch = Stopwatch.StartNew();

        try
        {
            // Get connection info for display
            await using var displayClient = await pool.GetClientAsync();
            Console.WriteLine($"  Connected to: {displayClient.ConnectedOrgFriendlyName} (Pool: {pool.Statistics.TotalConnections} connections)");
            Console.WriteLine();

            // ===================================================================
            // Query all IDs to delete (no separate count - just query and use .Count)
            // ===================================================================
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine("| Querying Records                                                |");
            Console.WriteLine("+-----------------------------------------------------------------+");

            Console.Write("  Querying ZIP code IDs... ");
            var zipIds = await QueryAllIdsAsync(pool, "ppds_zipcode");
            Console.WriteLine($"{zipIds.Count:N0} found");

            var cityIds = new List<Guid>();
            var stateIds = new List<Guid>();

            if (!zipOnly)
            {
                Console.Write("  Querying city IDs... ");
                cityIds = await QueryAllIdsAsync(pool, "ppds_city");
                Console.WriteLine($"{cityIds.Count:N0} found");

                Console.Write("  Querying state IDs... ");
                stateIds = await QueryAllIdsAsync(pool, "ppds_state");
                Console.WriteLine($"{stateIds.Count:N0} found");
            }
            Console.WriteLine();

            var totalToDelete = zipIds.Count + cityIds.Count + stateIds.Count;
            if (totalToDelete == 0)
            {
                Console.WriteLine("  Nothing to delete.");
                return 0;
            }

            Console.WriteLine("  Records to delete:");
            Console.WriteLine($"    ZIP codes: {zipIds.Count:N0}");
            if (!zipOnly)
            {
                Console.WriteLine($"    Cities: {cityIds.Count:N0}");
                Console.WriteLine($"    States: {stateIds.Count:N0}");
            }
            Console.WriteLine($"    Total: {totalToDelete:N0}");
            Console.WriteLine();

            // Confirmation
            if (!confirm)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"  Delete {totalToDelete:N0} records? (y/N): ");
                Console.ResetColor();

                var response = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (response != "y" && response != "yes")
                {
                    Console.WriteLine("  Cancelled.");
                    return 0;
                }
                Console.WriteLine();
            }

            var totalDeleted = 0;
            var totalErrors = 0;

            // ===================================================================
            // Delete in dependency order: ZIP codes → cities → states
            // ===================================================================

            if (zipIds.Count > 0)
            {
                Console.WriteLine("+-----------------------------------------------------------------+");
                Console.WriteLine("| Deleting ZIP Codes                                              |");
                Console.WriteLine("+-----------------------------------------------------------------+");

                var result = await DeleteWithProgressAsync(bulkExecutor, "ppds_zipcode", zipIds);
                totalDeleted += result.SuccessCount;
                totalErrors += result.FailureCount;
                Console.WriteLine();
            }

            if (!zipOnly && cityIds.Count > 0)
            {
                Console.WriteLine("+-----------------------------------------------------------------+");
                Console.WriteLine("| Deleting Cities                                                 |");
                Console.WriteLine("+-----------------------------------------------------------------+");

                var result = await DeleteWithProgressAsync(bulkExecutor, "ppds_city", cityIds);
                totalDeleted += result.SuccessCount;
                totalErrors += result.FailureCount;
                Console.WriteLine();
            }

            if (!zipOnly && stateIds.Count > 0)
            {
                Console.WriteLine("+-----------------------------------------------------------------+");
                Console.WriteLine("| Deleting States                                                 |");
                Console.WriteLine("+-----------------------------------------------------------------+");

                var result = await DeleteWithProgressAsync(bulkExecutor, "ppds_state", stateIds);
                totalDeleted += result.SuccessCount;
                totalErrors += result.FailureCount;
                Console.WriteLine();
            }

            // ===================================================================
            // Summary
            // ===================================================================
            totalStopwatch.Stop();

            Console.WriteLine("+==============================================================+");
            if (totalErrors == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("|              Clean Complete                                   |");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("|              Clean Complete (with errors)                     |");
            }
            Console.ResetColor();
            Console.WriteLine("+==============================================================+");
            Console.WriteLine();
            Console.WriteLine($"  Total deleted: {totalDeleted:N0}");
            if (totalErrors > 0)
                Console.WriteLine($"  Total errors: {totalErrors:N0}");
            Console.WriteLine($"  Total time: {totalStopwatch.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine($"  Throughput: {totalDeleted / totalStopwatch.Elapsed.TotalSeconds:F1} deletes/second");

            return totalErrors > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }

    private static async Task<BulkOperationResult> DeleteWithProgressAsync(
        IBulkOperationExecutor bulkExecutor,
        string entityName,
        List<Guid> ids)
    {
        var progress = new Progress<ProgressSnapshot>(s =>
        {
            Console.WriteLine($"    Progress: {s.Processed:N0}/{s.Total:N0} ({s.PercentComplete:F1}%) " +
                $"| {s.RatePerSecond:F0}/s | {s.Elapsed:mm\\:ss} elapsed | ETA: {s.EstimatedRemaining:mm\\:ss}");
        });

        var result = await bulkExecutor.DeleteMultipleAsync(entityName, ids, progress: progress);

        Console.WriteLine($"  Deleted {result.SuccessCount:N0} {entityName} records in {result.Duration.TotalSeconds:F2}s");
        Console.WriteLine($"    Throughput: {result.SuccessCount / result.Duration.TotalSeconds:F1} records/second");

        if (result.FailureCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            foreach (var error in result.Errors.Take(5))
            {
                Console.WriteLine($"    Error at index {error.Index}: {error.Message}");
            }
            if (result.Errors.Count > 5)
            {
                Console.WriteLine($"    ... and {result.Errors.Count - 5} more errors");
            }
            Console.ResetColor();
        }

        return result;
    }

    private static async Task<List<Guid>> QueryAllIdsAsync(IDataverseConnectionPool pool, string entityName)
    {
        var allIds = new List<Guid>();

        await using var client = await pool.GetClientAsync();

        var query = new QueryExpression(entityName)
        {
            ColumnSet = new ColumnSet(false),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };

        while (true)
        {
            var result = await client.RetrieveMultipleAsync(query);
            allIds.AddRange(result.Entities.Select(e => e.Id));

            if (!result.MoreRecords)
                break;

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }

        return allIds;
    }
}
