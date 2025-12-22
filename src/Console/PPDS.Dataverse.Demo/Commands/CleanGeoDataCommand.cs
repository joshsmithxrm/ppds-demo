using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Bulk deletes geographic reference data for clean volume testing.
/// Deletes in dependency order: ZIP codes → cities → states.
/// Uses ExecuteMultipleRequest with parallel batches for optimal throughput.
/// (DeleteMultipleRequest only exists for elastic tables, not standard tables)
/// </summary>
public static class CleanGeoDataCommand
{
    public static Command Create()
    {
        var command = new Command("clean-geo-data", "Bulk delete geographic reference data");

        var batchSizeOption = new Option<int>(
            "--batch-size",
            () => 100,
            "Batch size for DeleteMultiple requests (1-1000)");

        var parallelOption = new Option<int>(
            "--parallel",
            () => 4,
            "Number of parallel batches (1=sequential, 4-8 recommended)");

        var zipOnlyOption = new Option<bool>(
            "--zip-only",
            "Only delete ZIP codes (preserve states)");

        var confirmOption = new Option<bool>(
            "--confirm",
            "Skip confirmation prompt");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            "Enable verbose logging (shows SDK debug output)");

        command.AddOption(batchSizeOption);
        command.AddOption(parallelOption);
        command.AddOption(zipOnlyOption);
        command.AddOption(confirmOption);
        command.AddOption(verboseOption);

        command.SetHandler(async (int batchSize, int parallel, bool zipOnly, bool confirm, bool verbose) =>
        {
            Environment.ExitCode = await ExecuteAsync(batchSize, parallel, zipOnly, confirm, verbose);
        }, batchSizeOption, parallelOption, zipOnlyOption, confirmOption, verboseOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(int batchSize, int maxParallel, bool zipOnly, bool confirm, bool verbose)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       Clean Geographic Data                                  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        batchSize = Math.Clamp(batchSize, 1, 1000);
        maxParallel = Math.Clamp(maxParallel, 1, 16);

        // Configure verbose logging if requested
        ILoggerFactory? loggerFactory = null;
        ILogger? logger = null;
        if (verbose)
        {
            Console.WriteLine("  Verbose logging enabled");
            Console.WriteLine();

            loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddSimpleConsole(options =>
                    {
                        options.SingleLine = true;
                        options.TimestampFormat = "HH:mm:ss.fff ";
                    });
            });
            logger = loggerFactory.CreateLogger<ServiceClient>();
        }

        using var host = CommandBase.CreateHost([]);
        var config = host.Services.GetRequiredService<IConfiguration>();
        var (connectionString, envName) = CommandBase.ResolveEnvironment(config, "Dev");

        if (string.IsNullOrEmpty(connectionString))
        {
            CommandBase.WriteError("Connection not found. Configure Environments:Dev:ConnectionString in user-secrets.");
            return 1;
        }

        try
        {
            // Create single shared client - ServiceClient is thread-safe for concurrent requests
            using var client = verbose && logger != null
                ? new ServiceClient(connectionString, logger)
                : new ServiceClient(connectionString);

            if (!client.IsReady)
            {
                CommandBase.WriteError($"Connection failed: {client.LastError}");
                return 1;
            }

            Console.WriteLine($"  Connected to: {client.ConnectedOrgFriendlyName} ({envName})");
            Console.WriteLine($"  Batch size: {batchSize}, Parallel batches: {maxParallel}");
            Console.WriteLine();

            // Count records to delete
            var zipCount = await CountRecordsAsync(client, "ppds_zipcode");
            var cityCount = await CountRecordsAsync(client, "ppds_city");
            var stateCount = await CountRecordsAsync(client, "ppds_state");

            Console.WriteLine("  Records to delete:");
            Console.WriteLine($"    ZIP codes: {zipCount:N0}");
            if (!zipOnly)
            {
                Console.WriteLine($"    Cities: {cityCount:N0}");
                Console.WriteLine($"    States: {stateCount:N0}");
            }
            Console.WriteLine();

            var totalToDelete = zipOnly ? zipCount : (zipCount + cityCount + stateCount);
            if (totalToDelete == 0)
            {
                Console.WriteLine("  Nothing to delete.");
                return 0;
            }

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

            var totalStopwatch = Stopwatch.StartNew();
            var totalDeleted = 0;
            var totalErrors = 0;

            // Delete in dependency order: ZIP codes first
            if (zipCount > 0)
            {
                Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
                Console.WriteLine("│ Deleting ZIP Codes                                              │");
                Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

                var (deleted, errors) = await BulkDeleteEntityAsync(client, "ppds_zipcode", batchSize, maxParallel);
                totalDeleted += deleted;
                totalErrors += errors;
                Console.WriteLine($"  Deleted {deleted:N0} ZIP codes" + (errors > 0 ? $" ({errors:N0} errors)" : ""));
                Console.WriteLine();
            }

            if (!zipOnly)
            {
                // Delete cities
                if (cityCount > 0)
                {
                    Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
                    Console.WriteLine("│ Deleting Cities                                                 │");
                    Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

                    var (deleted, errors) = await BulkDeleteEntityAsync(client, "ppds_city", batchSize, maxParallel);
                    totalDeleted += deleted;
                    totalErrors += errors;
                    Console.WriteLine($"  Deleted {deleted:N0} cities" + (errors > 0 ? $" ({errors:N0} errors)" : ""));
                    Console.WriteLine();
                }

                // Delete states last
                if (stateCount > 0)
                {
                    Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
                    Console.WriteLine("│ Deleting States                                                 │");
                    Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

                    var (deleted, errors) = await BulkDeleteEntityAsync(client, "ppds_state", batchSize, maxParallel);
                    totalDeleted += deleted;
                    totalErrors += errors;
                    Console.WriteLine($"  Deleted {deleted:N0} states" + (errors > 0 ? $" ({errors:N0} errors)" : ""));
                    Console.WriteLine();
                }
            }

            totalStopwatch.Stop();

            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            if (totalErrors == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("║              Clean Complete                                   ║");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("║              Clean Complete (with errors)                    ║");
            }
            Console.ResetColor();
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
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
            CommandBase.WriteError($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            loggerFactory?.Dispose();
        }
    }

    private static async Task<int> CountRecordsAsync(ServiceClient client, string entityName)
    {
        try
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(false),
                PageInfo = new PagingInfo { Count = 1, PageNumber = 1, ReturnTotalRecordCount = true }
            };

            var result = await client.RetrieveMultipleAsync(query);
            return result.TotalRecordCount > 0 ? result.TotalRecordCount : result.Entities.Count;
        }
        catch
        {
            return 0; // Table might not exist
        }
    }

    private static async Task<(int deleted, int errors)> BulkDeleteEntityAsync(
        ServiceClient client,
        string entityName,
        int batchSize,
        int maxParallel)
    {
        var stopwatch = Stopwatch.StartNew();

        // Query all record IDs
        var allIds = new List<Guid>();
        var query = new QueryExpression(entityName)
        {
            ColumnSet = new ColumnSet(false), // Just IDs
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };

        Console.Write("  Querying records... ");
        while (true)
        {
            var result = await client.RetrieveMultipleAsync(query);
            allIds.AddRange(result.Entities.Select(e => e.Id));

            if (!result.MoreRecords)
                break;

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
        Console.WriteLine($"found {allIds.Count:N0}");

        if (allIds.Count == 0)
            return (0, 0);

        // Prepare batches
        var batches = allIds.Chunk(batchSize).ToList();
        Console.WriteLine($"  Deleting in {batches.Count:N0} batches ({maxParallel} parallel)...");

        // Thread-safe counters and progress tracking
        var totalDeleted = 0;
        var totalErrors = 0;
        var totalProcessed = 0;
        var overallStopwatch = Stopwatch.StartNew();
        var lastProgressUpdate = DateTime.UtcNow;
        var progressLock = new object();

        // Process batches in parallel using the shared thread-safe client
        await Parallel.ForEachAsync(
            batches,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallel },
            async (batch, ct) =>
            {
                var batchList = batch.ToArray();

                // Use ExecuteMultipleRequest with DeleteRequest for standard tables
                // (DeleteMultipleRequest only exists for elastic tables)
                var executeMultiple = new ExecuteMultipleRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = true,
                        ReturnResponses = false // Faster - don't need responses for delete
                    }
                };

                foreach (var id in batchList)
                {
                    executeMultiple.Requests.Add(new DeleteRequest
                    {
                        Target = new EntityReference(entityName, id)
                    });
                }

                try
                {
                    await client.ExecuteAsync(executeMultiple, ct);

                    Interlocked.Add(ref totalDeleted, batchList.Length);
                    Interlocked.Add(ref totalProcessed, batchList.Length);
                }
                catch (Exception ex)
                {
                    // Extract detailed error info from Dataverse FaultException
                    string errorDetail;
                    if (ex is System.ServiceModel.FaultException<OrganizationServiceFault> fault)
                    {
                        errorDetail = $"[0x{fault.Detail.ErrorCode:X8}] {fault.Detail.Message}";
                    }
                    else
                    {
                        errorDetail = $"{ex.GetType().Name}: {ex.Message}";
                    }

                    lock (progressLock)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"    Batch error: {errorDetail}");
                        Console.ResetColor();
                    }

                    Interlocked.Add(ref totalErrors, batchList.Length);
                    Interlocked.Add(ref totalProcessed, batchList.Length);
                }

                // Progress update (rate-limited to avoid console spam)
                var now = DateTime.UtcNow;
                bool shouldUpdate;
                lock (progressLock)
                {
                    shouldUpdate = (now - lastProgressUpdate).TotalSeconds >= 3;
                    if (shouldUpdate) lastProgressUpdate = now;
                }

                if (shouldUpdate)
                {
                    var processed = Interlocked.CompareExchange(ref totalProcessed, 0, 0);
                    var elapsed = overallStopwatch.Elapsed;
                    var pct = (double)processed / allIds.Count * 100;
                    var rate = elapsed.TotalSeconds > 0.1 ? processed / elapsed.TotalSeconds : 0;
                    var remaining = rate > 0.001 ? (allIds.Count - processed) / rate : 0;
                    var etaDisplay = remaining > 0 ? TimeSpan.FromSeconds(remaining).ToString(@"mm\:ss") : "--:--";

                    lock (progressLock)
                    {
                        Console.WriteLine($"    Progress: {processed:N0}/{allIds.Count:N0} ({pct:F1}%) " +
                                          $"| {rate:F0}/s " +
                                          $"| Elapsed: {elapsed:mm\\:ss} " +
                                          $"| ETA: {etaDisplay}");
                    }
                }
            });

        // Final progress
        var finalElapsed = overallStopwatch.Elapsed;
        var finalRate = finalElapsed.TotalSeconds > 0.1 ? totalProcessed / finalElapsed.TotalSeconds : 0;
        Console.WriteLine($"    Final: {totalProcessed:N0}/{allIds.Count:N0} " +
                          $"| {finalRate:F0}/s overall " +
                          $"| {finalElapsed:mm\\:ss} elapsed");

        return (totalDeleted, totalErrors);
    }
}
