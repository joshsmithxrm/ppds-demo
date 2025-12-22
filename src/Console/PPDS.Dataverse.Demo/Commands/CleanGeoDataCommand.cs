using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Bulk deletes geographic reference data for clean volume testing.
/// Deletes in dependency order: ZIP codes → cities → states.
/// </summary>
public static class CleanGeoDataCommand
{
    public static Command Create()
    {
        var command = new Command("clean-geo-data", "Bulk delete geographic reference data");

        var batchSizeOption = new Option<int>(
            "--batch-size",
            () => 500,
            "Batch size for ExecuteMultiple delete requests (1-1000)");

        var zipOnlyOption = new Option<bool>(
            "--zip-only",
            "Only delete ZIP codes (preserve states)");

        var confirmOption = new Option<bool>(
            "--confirm",
            "Skip confirmation prompt");

        command.AddOption(batchSizeOption);
        command.AddOption(zipOnlyOption);
        command.AddOption(confirmOption);

        command.SetHandler(async (int batchSize, bool zipOnly, bool confirm) =>
        {
            Environment.ExitCode = await ExecuteAsync(batchSize, zipOnly, confirm);
        }, batchSizeOption, zipOnlyOption, confirmOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(int batchSize, bool zipOnly, bool confirm)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       Clean Geographic Data                                  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        batchSize = Math.Clamp(batchSize, 1, 1000);

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
            using var client = new ServiceClient(connectionString);
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

            // Delete in dependency order: ZIP codes first
            if (zipCount > 0)
            {
                Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
                Console.WriteLine("│ Deleting ZIP Codes                                              │");
                Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

                var deleted = await BulkDeleteEntityAsync(client, "ppds_zipcode", "ppds_code", batchSize);
                totalDeleted += deleted;
                Console.WriteLine($"  Deleted {deleted:N0} ZIP codes");
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

                    var deleted = await BulkDeleteEntityAsync(client, "ppds_city", "ppds_name", batchSize);
                    totalDeleted += deleted;
                    Console.WriteLine($"  Deleted {deleted:N0} cities");
                    Console.WriteLine();
                }

                // Delete states last
                if (stateCount > 0)
                {
                    Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
                    Console.WriteLine("│ Deleting States                                                 │");
                    Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

                    var deleted = await BulkDeleteEntityAsync(client, "ppds_state", "ppds_name", batchSize);
                    totalDeleted += deleted;
                    Console.WriteLine($"  Deleted {deleted:N0} states");
                    Console.WriteLine();
                }
            }

            totalStopwatch.Stop();

            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("║              Clean Complete                                   ║");
            Console.ResetColor();
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"  Total deleted: {totalDeleted:N0}");
            Console.WriteLine($"  Total time: {totalStopwatch.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine($"  Throughput: {totalDeleted / totalStopwatch.Elapsed.TotalSeconds:F1} deletes/second");

            return 0;
        }
        catch (Exception ex)
        {
            CommandBase.WriteError($"Error: {ex.Message}");
            return 1;
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

    private static async Task<int> BulkDeleteEntityAsync(
        ServiceClient client,
        string entityName,
        string primaryNameAttribute,
        int batchSize)
    {
        var totalDeleted = 0;
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
            return 0;

        // Delete in batches
        var batches = allIds.Chunk(batchSize).ToList();
        Console.WriteLine($"  Deleting in {batches.Count:N0} batches (batch size: {batchSize})...");

        var progressStopwatch = Stopwatch.StartNew();

        foreach (var batch in batches)
        {
            var executeMultiple = new ExecuteMultipleRequest
            {
                Requests = new OrganizationRequestCollection(),
                Settings = new ExecuteMultipleSettings
                {
                    ContinueOnError = true,
                    ReturnResponses = false // Faster - don't need responses for delete
                }
            };

            foreach (var id in batch)
            {
                executeMultiple.Requests.Add(new DeleteRequest
                {
                    Target = new EntityReference(entityName, id)
                });
            }

            try
            {
                await client.ExecuteAsync(executeMultiple);
                totalDeleted += batch.Length;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    Batch error: {ex.Message}");
                Console.ResetColor();
            }

            // Progress update every 5 seconds
            if (progressStopwatch.Elapsed.TotalSeconds >= 5)
            {
                var pct = (double)totalDeleted / allIds.Count * 100;
                var rate = totalDeleted / stopwatch.Elapsed.TotalSeconds;
                Console.WriteLine($"    Progress: {totalDeleted:N0}/{allIds.Count:N0} ({pct:F1}%) - {rate:F1} rec/s");
                progressStopwatch.Restart();
            }
        }

        stopwatch.Stop();
        var throughput = totalDeleted / stopwatch.Elapsed.TotalSeconds;
        Console.WriteLine($"  Completed in {stopwatch.Elapsed.TotalSeconds:F2}s ({throughput:F1} rec/s)");

        return totalDeleted;
    }
}
