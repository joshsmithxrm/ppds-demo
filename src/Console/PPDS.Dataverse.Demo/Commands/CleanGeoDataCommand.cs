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

        var zipOnlyOption = new Option<bool>(
            "--zip-only",
            "Only delete ZIP codes (preserve states)");

        var confirmOption = new Option<bool>(
            "--confirm",
            "Skip confirmation prompt");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            "Enable verbose logging (shows SDK debug output)");

        command.AddOption(zipOnlyOption);
        command.AddOption(confirmOption);
        command.AddOption(verboseOption);

        command.SetHandler(async (bool zipOnly, bool confirm, bool verbose) =>
        {
            Environment.ExitCode = await ExecuteAsync(zipOnly, confirm, verbose);
        }, zipOnlyOption, confirmOption, verboseOption);

        return command;
    }

    // Default batch size for delete operations (parallelism uses SDK default)
    private const int DefaultBatchSize = 100;

    public static async Task<int> ExecuteAsync(bool zipOnly, bool confirm, bool verbose)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       Clean Geographic Data                                  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

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

                var (deleted, errors) = await BulkDeleteEntityAsync(client, "ppds_zipcode", DefaultBatchSize);
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

                    var (deleted, errors) = await BulkDeleteEntityAsync(client, "ppds_city", DefaultBatchSize);
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

                    var (deleted, errors) = await BulkDeleteEntityAsync(client, "ppds_state", DefaultBatchSize);
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
        int batchSize)
    {
        var totalDeleted = 0;
        var totalErrors = 0;
        var overallStopwatch = Stopwatch.StartNew();
        var lastProgressUpdate = DateTime.UtcNow;
        var progressLock = new object();

        // Delete in waves: query batch, delete, repeat until empty
        // This approach avoids paging cookie issues and is more memory efficient
        var waveNumber = 0;
        while (true)
        {
            waveNumber++;

            // Query next batch of IDs (always page 1 since we're deleting as we go)
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(false),
                PageInfo = new PagingInfo { Count = 1000, PageNumber = 1 }
            };

            var result = await client.RetrieveMultipleAsync(query);
            var ids = result.Entities.Select(e => e.Id).ToList();

            if (ids.Count == 0)
                break;

            Console.WriteLine($"  Wave {waveNumber}: processing {ids.Count:N0} records...");

            // Process batches in parallel
            var batches = ids.Chunk(batchSize).ToList();
            var waveDeleted = 0;
            var waveErrors = 0;

            await Parallel.ForEachAsync(
                batches,
                async (batch, ct) =>
                {
                    var batchList = batch.ToArray();

                    var executeMultiple = new ExecuteMultipleRequest
                    {
                        Requests = new OrganizationRequestCollection(),
                        Settings = new ExecuteMultipleSettings
                        {
                            ContinueOnError = true,
                            ReturnResponses = false
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
                        Interlocked.Add(ref waveDeleted, batchList.Length);
                    }
                    catch (Exception ex)
                    {
                        string errorDetail = ex is System.ServiceModel.FaultException<OrganizationServiceFault> fault
                            ? $"[0x{fault.Detail.ErrorCode:X8}] {fault.Detail.Message}"
                            : $"{ex.GetType().Name}: {ex.Message}";

                        lock (progressLock)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"    Batch error: {errorDetail}");
                            Console.ResetColor();
                        }

                        Interlocked.Add(ref waveErrors, batchList.Length);
                    }
                });

            totalDeleted += waveDeleted;
            totalErrors += waveErrors;

            // Progress update
            var now = DateTime.UtcNow;
            if ((now - lastProgressUpdate).TotalSeconds >= 3)
            {
                lastProgressUpdate = now;
                var elapsed = overallStopwatch.Elapsed;
                var rate = elapsed.TotalSeconds > 0.1 ? totalDeleted / elapsed.TotalSeconds : 0;
                Console.WriteLine($"    Total: {totalDeleted:N0} deleted | {rate:F0}/s | {elapsed:mm\\:ss} elapsed");
            }
        }

        // Final summary
        var finalElapsed = overallStopwatch.Elapsed;
        var finalRate = finalElapsed.TotalSeconds > 0.1 ? totalDeleted / finalElapsed.TotalSeconds : 0;
        Console.WriteLine($"    Final: {totalDeleted:N0} deleted | {finalRate:F0}/s | {finalElapsed:mm\\:ss} elapsed");

        return (totalDeleted, totalErrors);
    }
}
