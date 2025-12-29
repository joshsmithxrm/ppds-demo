using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PPDS.Dataverse.BulkOperations;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Dataverse.Pooling;
using PPDS.Dataverse.Progress;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Full cross-environment migration workflow for geographic reference data.
///
/// This command orchestrates the complete migration workflow:
///   1. export-geo-data: Generate schema + export to ZIP package
///   2. import-geo-data: Import package to target environment
///   3. Verify: Compare source and target counts
///
/// Supports two modes:
/// - CLI Mode (default): Composes export-geo-data and import-geo-data commands
/// - SDK Mode (--use-sdk): Direct bulk operations via IBulkOperationExecutor
///
/// No user mapping required - geo data is reference data without ownership.
/// Alternate keys enable idempotent upsert across environments.
///
/// Usage:
///   dotnet run -- migrate-geo-data --target QA
///   dotnet run -- migrate-geo-data --source Dev --target Prod --clean-target
///   dotnet run -- migrate-geo-data --target QA --use-sdk --parallelism 4
/// </summary>
public static class MigrateGeoDataCommand
{
    private static readonly string DataPath = Path.Combine(AppContext.BaseDirectory, "geo-export.zip");

    public static Command Create()
    {
        var command = new Command("migrate-geo-data", "Migrate geographic data between environments");

        var sourceOption = new Option<string>(
            aliases: ["--source", "-s"],
            getDefaultValue: () => "Dev",
            description: "Source environment name");

        var targetOption = new Option<string?>(
            aliases: ["--target", "-t"],
            description: "Target environment name (required for CLI mode)");

        var dryRunOption = new Option<bool>(
            "--dry-run",
            "Export only, don't import to target");

        var cleanTargetOption = new Option<bool>(
            "--clean-target",
            "Clean target environment before import");

        var useSdkOption = new Option<bool>(
            "--use-sdk",
            "Use direct SDK instead of CLI (for SDK developers)");

        // Use standardized options from GlobalOptionsExtensions
        var parallelismOption = GlobalOptionsExtensions.CreateParallelismOption();
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        command.AddOption(sourceOption);
        command.AddOption(targetOption);
        command.AddOption(dryRunOption);
        command.AddOption(cleanTargetOption);
        command.AddOption(useSdkOption);
        command.AddOption(parallelismOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (string source, string? target, bool dryRun, bool cleanTarget, bool useSdk, int? parallelism, bool verbose, bool debug) =>
        {
            Environment.ExitCode = await ExecuteAsync(source, target, dryRun, cleanTarget, useSdk, parallelism, verbose, debug);
        }, sourceOption, targetOption, dryRunOption, cleanTargetOption, useSdkOption, parallelismOption, verboseOption, debugOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(
        string source,
        string? target,
        bool dryRun,
        bool cleanTarget,
        bool useSdk,
        int? parallelism = null,
        bool verbose = false,
        bool debug = false)
    {
        ConsoleWriter.Header($"Geo Data Migration: {source} -> {target ?? "(dry-run)"}");

        // Validate target is specified unless dry-run
        if (!dryRun && string.IsNullOrEmpty(target))
        {
            ConsoleWriter.Error("Target environment is required. Use --target <env> or --dry-run.");
            return 1;
        }

        // Route to appropriate mode
        if (useSdk)
        {
            var options = new GlobalOptions
            {
                Environment = source, // SDK mode uses source for initial connection
                Verbose = verbose,
                Debug = debug,
                Parallelism = parallelism
            };
            return await ExecuteWithSdkAsync(source, target!, dryRun, cleanTarget, options);
        }
        else
        {
            return await ExecuteWithCliAsync(source, target, dryRun, cleanTarget, verbose, debug);
        }
    }

    #region CLI Mode

    /// <summary>
    /// CLI mode orchestrates export-geo-data and import-geo-data commands.
    /// This demonstrates command composition - building workflows from smaller pieces.
    /// </summary>
    private static async Task<int> ExecuteWithCliAsync(
        string source,
        string? target,
        bool dryRun,
        bool cleanTarget,
        bool verbose,
        bool debug)
    {
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine("  Mode: CLI (Command Composition)");
        Console.WriteLine("  Environments:");
        Console.WriteLine($"    Source: {source}");
        Console.WriteLine($"    Target: {target ?? "(dry-run)"}");
        Console.WriteLine();

        if (dryRun)
        {
            ConsoleWriter.Warning("  [DRY RUN] Will export only, no import");
            Console.WriteLine();
        }

        // ===================================================================
        // STEP 1: Export (calls export-geo-data)
        // ===================================================================
        ConsoleWriter.Section("Step 1: Export from Source (export-geo-data)");
        Console.WriteLine();

        // Create GlobalOptions for source environment
        var sourceOptions = new GlobalOptions
        {
            Environment = source,
            Verbose = verbose,
            Debug = debug
        };

        var exportResult = await ExportGeoDataCommand.ExecuteAsync(
            outputPath: DataPath,
            options: sourceOptions);

        if (exportResult != 0)
        {
            ConsoleWriter.Error("Export step failed");
            return 1;
        }

        Console.WriteLine();

        if (dryRun)
        {
            ConsoleWriter.ResultBanner("DRY RUN COMPLETE - No import performed", success: true);
            Console.WriteLine();
            Console.WriteLine($"  Export file: {DataPath}");
            Console.WriteLine();
            Console.WriteLine("  To import manually:");
            Console.WriteLine($"    dotnet run -- import-geo-data --data \"{DataPath}\" --env {target ?? "<TARGET>"}");
            return 0;
        }

        // ===================================================================
        // STEP 2: Import (calls import-geo-data)
        // ===================================================================
        ConsoleWriter.Section("Step 2: Import to Target (import-geo-data)");
        Console.WriteLine();

        // Create GlobalOptions for target environment
        var targetOptions = new GlobalOptions
        {
            Environment = target!,
            Verbose = verbose,
            Debug = debug
        };

        var importResult = await ImportGeoDataCommand.ExecuteAsync(
            dataPath: DataPath,
            options: targetOptions,
            cleanFirst: cleanTarget);

        if (importResult != 0)
        {
            ConsoleWriter.Error("Import step failed");
            return 1;
        }

        stopwatch.Stop();

        Console.WriteLine();
        ConsoleWriter.ResultBanner($"MIGRATION COMPLETE: {source} -> {target}", success: true);
        Console.WriteLine();
        Console.WriteLine($"  Total time: {stopwatch.Elapsed.TotalSeconds:F2}s");

        return 0;
    }

    #endregion

    #region SDK Mode

    private static async Task<int> ExecuteWithSdkAsync(
        string source,
        string target,
        bool dryRun,
        bool cleanTarget,
        GlobalOptions options)
    {
        Console.WriteLine("  Mode: SDK (Direct API)");
        Console.WriteLine();

        // Create hosts for both environments
        var sourceOptions = options with { Environment = source };
        var targetOptions = options with { Environment = target };

        using var sourceHost = CommandBase.CreateHostForBulkOperations(sourceOptions);
        var sourcePool = sourceHost.Services.GetRequiredService<IDataverseConnectionPool>();

        if (!sourcePool.IsEnabled)
        {
            ConsoleWriter.Error($"{source} environment not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        // Use using statement for exception-safe disposal
        using var targetHost = dryRun ? null : CommandBase.CreateHostForBulkOperations(targetOptions);
        IDataverseConnectionPool? targetPool = null;
        IBulkOperationExecutor? targetBulk = null;

        if (!dryRun)
        {
            targetPool = targetHost!.Services.GetRequiredService<IDataverseConnectionPool>();
            targetBulk = targetHost.Services.GetRequiredService<IBulkOperationExecutor>();

            if (!targetPool.IsEnabled)
            {
                ConsoleWriter.Error($"{target} environment not configured.");
                return 1;
            }
        }

        try
        {
            Console.WriteLine("  Environments:");
            Console.WriteLine($"    Source: {source}");
            Console.WriteLine($"    Target: {(dryRun ? "(dry-run)" : target)}");
            if (options.Parallelism.HasValue)
            {
                Console.WriteLine($"    Parallelism: {options.Parallelism.Value}");
            }
            Console.WriteLine();

            var totalStopwatch = Stopwatch.StartNew();

            // ===================================================================
            // PHASE 1: Query Source Data
            // ===================================================================
            ConsoleWriter.Section("Phase 1: Query Source Data");

            await using var sourceClient = await sourcePool.GetClientAsync();

            Console.Write("  Querying states... ");
            var states = await QueryAllEntitiesAsync(sourcePool, "ppds_state",
                "ppds_stateid", "ppds_name", "ppds_abbreviation");
            Console.WriteLine($"{states.Count:N0} found");

            Console.Write("  Querying cities... ");
            var cities = await QueryAllEntitiesAsync(sourcePool, "ppds_city",
                "ppds_cityid", "ppds_name", "ppds_stateid");
            Console.WriteLine($"{cities.Count:N0} found");

            Console.Write("  Querying ZIP codes... ");
            var zipCodes = await QueryAllEntitiesAsync(sourcePool, "ppds_zipcode",
                "ppds_zipcodeid", "ppds_code", "ppds_stateid", "ppds_cityid",
                "ppds_county", "ppds_latitude", "ppds_longitude");
            Console.WriteLine($"{zipCodes.Count:N0} found");
            Console.WriteLine();

            if (states.Count == 0)
            {
                ConsoleWriter.Error($"No geo data found in {source}. Run load-geo-data first.");
                return 1;
            }

            if (dryRun)
            {
                ConsoleWriter.ResultBanner("DRY RUN COMPLETE - No import performed", success: true);
                Console.WriteLine();
                Console.WriteLine($"  Would import: {states.Count} states, {cities.Count} cities, {zipCodes.Count:N0} ZIP codes");
                return 0;
            }

            // ===================================================================
            // PHASE 2: Clean Target (optional)
            // ===================================================================
            if (cleanTarget)
            {
                ConsoleWriter.Section("Phase 2: Clean Target");

                Console.WriteLine($"  Running clean-geo-data on {target}...");
                // Create options for target environment
                var cleanOptions = options with { Environment = target };
                var cleanResult = await CleanGeoDataCommand.ExecuteAsync(
                    zipOnly: false,
                    confirm: true,
                    cleanOptions);

                if (cleanResult != 0)
                {
                    ConsoleWriter.Error("Clean target failed");
                    return 1;
                }
                Console.WriteLine();
            }

            // ===================================================================
            // PHASE 3: Import to Target
            // ===================================================================
            ConsoleWriter.Section($"Phase {(cleanTarget ? "3" : "2")}: Import to {target}");

            // Build state abbreviation -> Entity map for lookup resolution
            var stateAbbreviationMap = new Dictionary<string, Entity>();
            foreach (var state in states)
            {
                var abbr = state.GetAttributeValue<string>("ppds_abbreviation");
                if (!string.IsNullOrEmpty(abbr))
                {
                    stateAbbreviationMap[abbr] = state;
                }
            }

            // Import states first (no dependencies)
            Console.WriteLine("  Importing states...");
            var stateEntities = states.Select(s =>
            {
                var entity = new Entity("ppds_state");
                // Use alternate key for upsert
                entity.KeyAttributes["ppds_abbreviation"] = s.GetAttributeValue<string>("ppds_abbreviation");
                entity["ppds_name"] = s.GetAttributeValue<string>("ppds_name");
                return entity;
            }).ToList();

            var stateProgress = CreateProgress("states");
            var stateResult = await targetBulk!.UpsertMultipleAsync("ppds_state", stateEntities, progress: stateProgress);
            PrintBulkResult("  States", stateResult);

            // Query target states to get GUIDs for lookup resolution
            Console.Write("  Querying target states for lookup resolution... ");
            await using var targetClient = await targetPool!.GetClientAsync();
            var targetStates = await QueryAllEntitiesAsync(targetPool, "ppds_state",
                "ppds_stateid", "ppds_abbreviation");
            var targetStateMap = targetStates.ToDictionary(
                s => s.GetAttributeValue<string>("ppds_abbreviation") ?? "",
                s => s.Id);
            Console.WriteLine($"{targetStates.Count} found");

            // Build source city map (city ID -> city name + state abbreviation) for ZIP code lookup resolution
            var sourceCityMap = new Dictionary<Guid, (string Name, string StateAbbr)>();
            foreach (var city in cities)
            {
                var stateRef = city.GetAttributeValue<EntityReference>("ppds_stateid");
                if (stateRef == null) continue;

                var sourceState = states.FirstOrDefault(s => s.Id == stateRef.Id);
                if (sourceState == null) continue;

                var abbr = sourceState.GetAttributeValue<string>("ppds_abbreviation") ?? "";
                var name = city.GetAttributeValue<string>("ppds_name") ?? "";
                sourceCityMap[city.Id] = (name, abbr);
            }

            // Import cities (with state lookup resolution)
            if (cities.Count > 0)
            {
                Console.WriteLine("  Importing cities...");
                var cityEntities = new List<Entity>();
                foreach (var city in cities)
                {
                    var stateRef = city.GetAttributeValue<EntityReference>("ppds_stateid");
                    if (stateRef == null) continue;

                    // Find source state abbreviation
                    var sourceState = states.FirstOrDefault(s => s.Id == stateRef.Id);
                    if (sourceState == null) continue;

                    var abbr = sourceState.GetAttributeValue<string>("ppds_abbreviation");
                    if (!targetStateMap.TryGetValue(abbr ?? "", out var targetStateId)) continue;

                    var entity = new Entity("ppds_city");
                    // Use composite alternate key for upsert
                    entity.KeyAttributes["ppds_name"] = city.GetAttributeValue<string>("ppds_name");
                    entity.KeyAttributes["ppds_stateid"] = targetStateId;
                    entity["ppds_stateid"] = new EntityReference("ppds_state", targetStateId);

                    cityEntities.Add(entity);
                }

                var cityProgress = CreateProgress("cities");
                var cityResult = await targetBulk.UpsertMultipleAsync("ppds_city", cityEntities, progress: cityProgress);
                PrintBulkResult("  Cities", cityResult);
            }

            // Query target cities to get GUIDs for lookup resolution
            Console.Write("  Querying target cities for lookup resolution... ");
            var targetCities = await QueryAllEntitiesAsync(targetPool, "ppds_city",
                "ppds_cityid", "ppds_name", "ppds_stateid");

            // Build target city map (name+stateAbbr -> GUID)
            var targetCityMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var city in targetCities)
            {
                var name = city.GetAttributeValue<string>("ppds_name") ?? "";
                var stateRef = city.GetAttributeValue<EntityReference>("ppds_stateid");
                if (stateRef == null) continue;

                // Find state abbreviation from target states
                var state = targetStates.FirstOrDefault(s => s.Id == stateRef.Id);
                if (state == null) continue;

                var abbr = state.GetAttributeValue<string>("ppds_abbreviation") ?? "";
                var key = $"{name}|{abbr}";
                targetCityMap[key] = city.Id;
            }
            Console.WriteLine($"{targetCities.Count} found");

            // Import ZIP codes (with state and city lookup resolution)
            Console.WriteLine("  Importing ZIP codes...");
            var zipEntities = new List<Entity>();
            foreach (var zip in zipCodes)
            {
                var stateRef = zip.GetAttributeValue<EntityReference>("ppds_stateid");
                if (stateRef == null) continue;

                // Find source state abbreviation
                var sourceState = states.FirstOrDefault(s => s.Id == stateRef.Id);
                if (sourceState == null) continue;

                var abbr = sourceState.GetAttributeValue<string>("ppds_abbreviation");
                if (!targetStateMap.TryGetValue(abbr ?? "", out var targetStateId)) continue;

                // Resolve city lookup
                var cityRef = zip.GetAttributeValue<EntityReference>("ppds_cityid");
                Guid? targetCityId = null;
                if (cityRef != null && sourceCityMap.TryGetValue(cityRef.Id, out var cityInfo))
                {
                    var cityKey = $"{cityInfo.Name}|{cityInfo.StateAbbr}";
                    if (targetCityMap.TryGetValue(cityKey, out var resolvedCityId))
                    {
                        targetCityId = resolvedCityId;
                    }
                }

                if (targetCityId == null) continue; // City is required

                var entity = new Entity("ppds_zipcode");
                // Use alternate key for upsert
                entity.KeyAttributes["ppds_code"] = zip.GetAttributeValue<string>("ppds_code");
                entity["ppds_stateid"] = new EntityReference("ppds_state", targetStateId);
                entity["ppds_cityid"] = new EntityReference("ppds_city", targetCityId.Value);
                if (zip.Contains("ppds_county"))
                    entity["ppds_county"] = zip.GetAttributeValue<string>("ppds_county");
                if (zip.Contains("ppds_latitude"))
                    entity["ppds_latitude"] = zip.GetAttributeValue<decimal?>("ppds_latitude");
                if (zip.Contains("ppds_longitude"))
                    entity["ppds_longitude"] = zip.GetAttributeValue<decimal?>("ppds_longitude");

                zipEntities.Add(entity);
            }

            var zipProgress = CreateProgress("ZIP codes");
            var zipResult = await targetBulk.UpsertMultipleAsync("ppds_zipcode", zipEntities, progress: zipProgress);
            PrintBulkResult("  ZIP codes", zipResult);
            Console.WriteLine();

            // ===================================================================
            // PHASE 4: Verify Target
            // ===================================================================
            ConsoleWriter.Section($"Phase {(cleanTarget ? "4" : "3")}: Verify {target}");

            var targetSummary = await QueryGeoSummary(targetClient);
            PrintGeoSummary($"  {target}", targetSummary);
            Console.WriteLine();

            // Compare
            var sourceSummary = new GeoSummary
            {
                StateCount = states.Count,
                CityCount = cities.Count,
                ZipCodeCount = zipCodes.Count
            };

            Console.WriteLine($"  Comparison ({source} -> {target}):");
            var passed = true;

            var stateMatch = sourceSummary.StateCount == targetSummary.StateCount;
            Console.Write($"    States: {sourceSummary.StateCount} -> {targetSummary.StateCount} ");
            ConsoleWriter.PassFail(stateMatch);
            passed &= stateMatch;

            var cityMatch = sourceSummary.CityCount == targetSummary.CityCount;
            Console.Write($"    Cities: {sourceSummary.CityCount} -> {targetSummary.CityCount} ");
            ConsoleWriter.PassFail(cityMatch);
            passed &= cityMatch;

            var zipMatch = sourceSummary.ZipCodeCount == targetSummary.ZipCodeCount;
            Console.Write($"    ZIP Codes: {sourceSummary.ZipCodeCount:N0} -> {targetSummary.ZipCodeCount:N0} ");
            ConsoleWriter.PassFail(zipMatch);
            passed &= zipMatch;

            Console.WriteLine();

            totalStopwatch.Stop();

            // ===================================================================
            // RESULT
            // ===================================================================
            if (passed)
            {
                ConsoleWriter.ResultBanner($"MIGRATION COMPLETE: {source} -> {target} SUCCESS", success: true);
                Console.WriteLine();
                Console.WriteLine($"  Total time: {totalStopwatch.Elapsed.TotalSeconds:F2}s");
                Console.WriteLine($"  Total records: {states.Count + cities.Count + zipCodes.Count:N0}");
                return 0;
            }
            else
            {
                ConsoleWriter.ResultBanner("MIGRATION VERIFICATION FAILED", success: false);
                return 1;
            }
        }
        catch (Exception ex)
        {
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }

    private static async Task<List<Entity>> QueryAllEntitiesAsync(
        IDataverseConnectionPool pool,
        string entityName,
        params string[] attributes)
    {
        var allEntities = new List<Entity>();

        await using var client = await pool.GetClientAsync();

        var query = new QueryExpression(entityName)
        {
            ColumnSet = new ColumnSet(attributes),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };

        while (true)
        {
            var result = await client.RetrieveMultipleAsync(query);
            allEntities.AddRange(result.Entities);

            if (!result.MoreRecords)
                break;

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }

        return allEntities;
    }

    private static Progress<ProgressSnapshot> CreateProgress(string entityName)
    {
        return new Progress<ProgressSnapshot>(s =>
        {
            Console.WriteLine($"    Progress: {s.Processed:N0}/{s.Total:N0} ({s.PercentComplete:F1}%) " +
                $"| {s.RatePerSecond:F0}/s | {s.Elapsed:mm\\:ss} elapsed | ETA: {s.EstimatedRemaining:mm\\:ss}");
        });
    }

    private static void PrintBulkResult(string prefix, BulkOperationResult result)
    {
        var rate = result.Duration.TotalSeconds > 0
            ? result.SuccessCount / result.Duration.TotalSeconds
            : 0;

        // Show created/updated breakdown if available (upsert operations)
        if (result.CreatedCount.HasValue && result.UpdatedCount.HasValue)
        {
            Console.WriteLine($"{prefix}: {result.SuccessCount:N0} upserted " +
                $"({result.CreatedCount:N0} created, {result.UpdatedCount:N0} updated) " +
                $"in {result.Duration.TotalSeconds:F2}s ({rate:F1}/s)");
        }
        else
        {
            Console.WriteLine($"{prefix}: {result.SuccessCount:N0} upserted " +
                $"in {result.Duration.TotalSeconds:F2}s ({rate:F1}/s)");
        }

        if (result.FailureCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{prefix}: {result.FailureCount} failures");
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
    }

    #endregion

    #region Shared Helpers

    private static async Task<GeoSummary> QueryGeoSummary(IPooledClient client)
    {
        var summary = new GeoSummary();

        // Query state count
        var stateQuery = new QueryExpression("ppds_state")
        {
            ColumnSet = new ColumnSet(false),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        var stateResult = await client.RetrieveMultipleAsync(stateQuery);
        summary.StateCount = stateResult.Entities.Count;

        // Query city count (needs paging - 30k+ cities)
        var cityQuery = new QueryExpression("ppds_city")
        {
            ColumnSet = new ColumnSet(false),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
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
        var zipQuery = new QueryExpression("ppds_zipcode")
        {
            ColumnSet = new ColumnSet(false),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
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

    private static void PrintGeoSummary(string prefix, GeoSummary summary)
    {
        Console.WriteLine($"{prefix}: {summary.StateCount} states, {summary.CityCount} cities, {summary.ZipCodeCount:N0} ZIP codes");
    }

    private record GeoSummary
    {
        public int StateCount { get; set; }
        public int CityCount { get; set; }
        public int ZipCodeCount { get; set; }
        public int TotalCount => StateCount + CityCount + ZipCodeCount;
    }

    #endregion
}
