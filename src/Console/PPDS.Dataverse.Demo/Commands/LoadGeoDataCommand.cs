using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using PPDS.Dataverse.BulkOperations;
using PPDS.Dataverse.Pooling;
using PPDS.Dataverse.Progress;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Downloads and loads geographic reference data (US ZIP codes) for volume testing.
/// Data source: GitHub free_zipcode_data (GeoNames-based, CC license).
/// </summary>
public static class LoadGeoDataCommand
{
    // GitHub raw CSV - free_zipcode_data project (GeoNames data, Creative Commons)
    private const string DataUrl = "https://raw.githubusercontent.com/midwire/free_zipcode_data/master/all_us_zipcodes.csv";
    private const string CsvFileName = "all_us_zipcodes.csv";

    private static readonly string CacheDir = Path.Combine(AppContext.BaseDirectory, "geo-data");
    private static readonly string CachePath = Path.Combine(CacheDir, CsvFileName);

    public static Command Create()
    {
        var command = new Command("load-geo-data", "Download and load US ZIP code data for volume testing");

        var limitOption = new Option<int?>(
            "--limit",
            "Limit number of ZIP codes to load (for testing)");

        var skipDownloadOption = new Option<bool>(
            "--skip-download",
            "Use cached data file (skip download)");

        var statesOnlyOption = new Option<bool>(
            "--states-only",
            "Only load states (skip ZIP codes)");

        var parallelismOption = new Option<int?>(
            "--parallelism",
            "Max parallel batches (uses SDK default if not specified)");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            "Enable verbose logging (operational: Connecting..., Processing...)");

        var debugOption = new Option<bool>(
            "--debug",
            "Enable debug logging (diagnostic: parallelism, ceiling, internal state)");

        var envOption = new Option<string?>(
            aliases: ["--environment", "--env", "-e"],
            description: "Target environment name (e.g., 'Dev', 'QA'). Uses DefaultEnvironment from config if not specified.");

        command.AddOption(limitOption);
        command.AddOption(skipDownloadOption);
        command.AddOption(statesOnlyOption);
        command.AddOption(parallelismOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);
        command.AddOption(envOption);

        command.SetHandler(async (int? limit, bool skipDownload, bool statesOnly, int? parallelism, bool verbose, bool debug, string? environment) =>
        {
            Environment.ExitCode = await ExecuteAsync(limit, skipDownload, statesOnly, parallelism, verbose, debug, environment);
        }, limitOption, skipDownloadOption, statesOnlyOption, parallelismOption, verboseOption, debugOption, envOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(int? limit, bool skipDownload, bool statesOnly, int? parallelism = null, bool verbose = false, bool debug = false, string? environment = null)
    {
        Console.WriteLine("+==============================================================+");
        Console.WriteLine("|       Load Geographic Data for Volume Testing                |");
        Console.WriteLine("+==============================================================+");
        Console.WriteLine();

        // Create host with SDK services for bulk operations
        using var host = CommandBase.CreateHostForBulkOperations(environment, parallelism, verbose, debug);
        var pool = host.Services.GetRequiredService<IDataverseConnectionPool>();
        var bulkExecutor = host.Services.GetRequiredService<IBulkOperationExecutor>();

        if (!pool.IsEnabled)
        {
            CommandBase.WriteError("Connection pool not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        var envDisplay = CommandBase.ResolveEnvironment(host, environment);
        Console.WriteLine($"  Environment: {envDisplay}");

        if (parallelism.HasValue)
        {
            Console.WriteLine($"  Parallelism: {parallelism.Value}");
        }
        if (debug)
        {
            Console.WriteLine("  Logging: Debug (diagnostic details)");
        }
        else if (verbose)
        {
            Console.WriteLine("  Logging: Verbose (operational messages)");
        }
        Console.WriteLine();

        var totalStopwatch = Stopwatch.StartNew();

        try
        {
            // ===================================================================
            // PHASE 1: Download/Load CSV
            // ===================================================================
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine("| Phase 1: Load CSV Data                                          |");
            Console.WriteLine("+-----------------------------------------------------------------+");

            List<ZipCodeRecord> zipCodes;
            if (!skipDownload || !File.Exists(CachePath))
            {
                zipCodes = await DownloadAndParseDataAsync();
            }
            else
            {
                Console.WriteLine($"  Using cached data: {CachePath}");
                zipCodes = await ParseCsvAsync(CachePath);
            }

            Console.WriteLine($"  Loaded {zipCodes.Count:N0} ZIP codes from CSV");

            if (limit.HasValue && limit.Value < zipCodes.Count)
            {
                zipCodes = zipCodes.Take(limit.Value).ToList();
                Console.WriteLine($"  Limited to {zipCodes.Count:N0} records (--limit {limit})");
            }
            Console.WriteLine();

            // ===================================================================
            // PHASE 2: Connect to Dataverse
            // ===================================================================
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine("| Phase 2: Connect to Dataverse                                   |");
            Console.WriteLine("+-----------------------------------------------------------------+");

            // Get a client from the pool for state operations
            await using var pooledClient = await pool.GetClientAsync();

            Console.WriteLine($"  Connected to: {pooledClient.ConnectedOrgFriendlyName} (Pool: {pool.Statistics.TotalConnections} connections)");
            Console.WriteLine();

            // ===================================================================
            // PHASE 3: Load States
            // ===================================================================
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine("| Phase 3: Load States                                            |");
            Console.WriteLine("+-----------------------------------------------------------------+");

            var stateStopwatch = Stopwatch.StartNew();

            // Extract unique states from ZIP data
            var states = zipCodes
                .GroupBy(z => z.StateId)
                .Select(g => new StateRecord
                {
                    Abbreviation = g.Key,
                    Name = g.First().StateName
                })
                .OrderBy(s => s.Abbreviation)
                .ToList();

            Console.WriteLine($"  Found {states.Count} unique states/territories");

            // Check existing states and create missing ones
            var stateMap = await LoadOrCreateStatesAsync(pooledClient, states);

            stateStopwatch.Stop();
            Console.WriteLine($"  State loading completed in {stateStopwatch.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine();

            if (statesOnly)
            {
                Console.WriteLine("  --states-only specified, skipping cities and ZIP codes");
                PrintSummary(totalStopwatch, states.Count, 0, 0, 0, 0);
                return 0;
            }

            // ===================================================================
            // PHASE 4: Load Cities
            // ===================================================================
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine("| Phase 4: Load Cities                                            |");
            Console.WriteLine("+-----------------------------------------------------------------+");

            var cityStopwatch = Stopwatch.StartNew();

            // Extract unique cities from ZIP data (city+state combinations)
            var cities = zipCodes
                .GroupBy(z => $"{z.City}|{z.StateId}")
                .Select(g => new CityRecord
                {
                    Name = g.First().City,
                    StateAbbreviation = g.First().StateId
                })
                .OrderBy(c => c.StateAbbreviation)
                .ThenBy(c => c.Name)
                .ToList();

            Console.WriteLine($"  Found {cities.Count:N0} unique cities");

            // Create cities and build city map for ZIP code creation
            var cityMap = await LoadOrCreateCitiesAsync(pooledClient, cities, stateMap, bulkExecutor);

            cityStopwatch.Stop();
            Console.WriteLine($"  City loading completed in {cityStopwatch.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine();

            // ===================================================================
            // PHASE 5: Load ZIP Codes (using PPDS.Dataverse SDK)
            // ===================================================================
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine("| Phase 5: Load ZIP Codes                                         |");
            Console.WriteLine("+-----------------------------------------------------------------+");

            // Build entities for upsert
            var (entities, skipped) = BuildZipCodeEntities(zipCodes, stateMap, cityMap, verbose);

            if (skipped > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Skipped {skipped} records (unknown state)");
                Console.ResetColor();
            }

            Console.WriteLine($"  Processing {entities.Count:N0} ZIP codes...");

            var progress = new Progress<ProgressSnapshot>(s =>
            {
                Console.WriteLine($"    Progress: {s.Processed:N0}/{s.Total:N0} ({s.PercentComplete:F1}%) " +
                    $"| {s.RatePerSecond:F0}/s | {s.Elapsed:mm\\:ss} elapsed | ETA: {s.EstimatedRemaining:mm\\:ss}");
            });

            var result = await bulkExecutor.UpsertMultipleAsync("ppds_zipcode", entities, progress: progress);

            Console.WriteLine();
            Console.WriteLine($"  ZIP code loading completed in {result.Duration.TotalSeconds:F2}s");

            // Show created/updated breakdown if available
            if (result.CreatedCount.HasValue && result.UpdatedCount.HasValue)
            {
                Console.WriteLine($"    Upserted: {result.SuccessCount:N0} ({result.CreatedCount:N0} created, {result.UpdatedCount:N0} updated)");
            }
            else
            {
                Console.WriteLine($"    Succeeded: {result.SuccessCount:N0}");
            }
            Console.WriteLine($"    Failed: {result.FailureCount:N0}");

            if (result.FailureCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                foreach (var error in result.Errors.Take(5))
                {
                    Console.WriteLine($"      Error at index {error.Index}: {error.Message}");
                }
                if (result.Errors.Count > 5)
                {
                    Console.WriteLine($"      ... and {result.Errors.Count - 5} more errors");
                }
                Console.ResetColor();
            }

            var throughput = result.Duration.TotalSeconds > 0 ? result.SuccessCount / result.Duration.TotalSeconds : 0;
            Console.WriteLine($"    Throughput: {throughput:F1} records/second");
            Console.WriteLine();

            // Pass actual created/updated counts to summary
            var createdCount = result.CreatedCount ?? result.SuccessCount;
            var updatedCount = result.UpdatedCount ?? 0;
            PrintSummary(totalStopwatch, states.Count, cities.Count, createdCount, updatedCount, result.FailureCount, skipped);

            return result.FailureCount > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            CommandBase.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintSummary(Stopwatch totalStopwatch, int states, int cities, int created, int updated, int errors, int skipped = 0)
    {
        totalStopwatch.Stop();

        Console.WriteLine("+==============================================================+");
        if (errors == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("|              Data Load Complete                               |");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("|              Data Load Complete (with errors)                 |");
        }
        Console.ResetColor();
        Console.WriteLine("+==============================================================+");
        Console.WriteLine();
        Console.WriteLine($"  Total time: {totalStopwatch.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"  States: {states}");
        Console.WriteLine($"  Cities: {cities:N0}");
        Console.WriteLine($"  ZIP codes created: {created:N0}");
        Console.WriteLine($"  ZIP codes updated: {updated:N0}");
        if (skipped > 0)
            Console.WriteLine($"  ZIP codes skipped: {skipped:N0}");
        if (errors > 0)
            Console.WriteLine($"  ZIP codes failed: {errors:N0}");
    }

    private static async Task<List<ZipCodeRecord>> DownloadAndParseDataAsync()
    {
        Directory.CreateDirectory(CacheDir);

        Console.Write("  Downloading ZIP code data from GitHub... ");
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PPDS-Demo/1.0");
        httpClient.Timeout = TimeSpan.FromMinutes(5);

        var response = await httpClient.GetAsync(DataUrl);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Download failed: {response.StatusCode}");
        }

        await using (var fs = File.Create(CachePath))
        {
            await response.Content.CopyToAsync(fs);
        }
        Console.WriteLine("Done");

        return await ParseCsvAsync(CachePath);
    }

    private static async Task<List<ZipCodeRecord>> ParseCsvAsync(string path)
    {
        Console.Write("  Parsing CSV... ");

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null
        });

        var records = new List<ZipCodeRecord>();
        await foreach (var record in csv.GetRecordsAsync<ZipCodeRecord>())
        {
            records.Add(record);
        }

        Console.WriteLine("Done");
        return records;
    }

    private static async Task<Dictionary<string, Guid>> LoadOrCreateStatesAsync(
        IPooledClient client,
        List<StateRecord> states)
    {
        var stateMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // Query existing states
        Console.Write("  Checking existing states... ");
        var query = new QueryExpression("ppds_state")
        {
            ColumnSet = new ColumnSet("ppds_abbreviation"),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };

        var existing = await client.RetrieveMultipleAsync(query);
        foreach (var entity in existing.Entities)
        {
            var abbr = entity.GetAttributeValue<string>("ppds_abbreviation");
            if (!string.IsNullOrEmpty(abbr))
            {
                stateMap[abbr] = entity.Id;
            }
        }
        Console.WriteLine($"found {stateMap.Count}");

        // Create missing states
        var missing = states.Where(s => !stateMap.ContainsKey(s.Abbreviation)).ToList();
        if (missing.Count == 0)
        {
            Console.WriteLine("  All states already exist");
            return stateMap;
        }

        Console.Write($"  Creating {missing.Count} states... ");

        // Build entities for CreateMultiple
        var stateEntities = missing.Select(s => new Entity("ppds_state")
        {
            ["ppds_name"] = s.Name,
            ["ppds_abbreviation"] = s.Abbreviation
        }).ToList();

        var targets = new EntityCollection(stateEntities) { EntityName = "ppds_state" };
        var response = (CreateMultipleResponse)await client.ExecuteAsync(
            new CreateMultipleRequest { Targets = targets });

        // Map created IDs back to abbreviations
        for (int i = 0; i < response.Ids.Length; i++)
        {
            stateMap[missing[i].Abbreviation] = response.Ids[i];
        }

        Console.WriteLine($"created {response.Ids.Length}");
        return stateMap;
    }

    /// <summary>
    /// Loads or creates city records and returns a map of city+state key to GUID.
    /// Uses UpsertMultiple with composite alternate key (name, stateid).
    /// </summary>
    private static async Task<Dictionary<string, Guid>> LoadOrCreateCitiesAsync(
        IPooledClient client,
        List<CityRecord> cities,
        Dictionary<string, Guid> stateMap,
        IBulkOperationExecutor bulkExecutor)
    {
        var cityMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // Query existing cities
        Console.Write("  Checking existing cities... ");
        var query = new QueryExpression("ppds_city")
        {
            ColumnSet = new ColumnSet("ppds_name", "ppds_stateid"),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };

        // Build reverse state map (GUID -> abbreviation)
        var reverseStateMap = stateMap.ToDictionary(kv => kv.Value, kv => kv.Key);

        while (true)
        {
            var result = await client.RetrieveMultipleAsync(query);
            foreach (var entity in result.Entities)
            {
                var name = entity.GetAttributeValue<string>("ppds_name");
                var stateRef = entity.GetAttributeValue<EntityReference>("ppds_stateid");
                if (!string.IsNullOrEmpty(name) && stateRef != null && reverseStateMap.TryGetValue(stateRef.Id, out var stateAbbr))
                {
                    var key = $"{name}|{stateAbbr}";
                    cityMap[key] = entity.Id;
                }
            }

            if (!result.MoreRecords)
                break;

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
        Console.WriteLine($"found {cityMap.Count:N0}");

        // Find cities that need to be created
        var missing = cities.Where(c => !cityMap.ContainsKey(c.Key) && stateMap.ContainsKey(c.StateAbbreviation)).ToList();
        if (missing.Count == 0)
        {
            Console.WriteLine("  All cities already exist");
            return cityMap;
        }

        Console.WriteLine($"  Creating {missing.Count:N0} cities...");

        // Build entities for upsert with composite alternate key
        var cityEntities = missing.Select(c =>
        {
            var entity = new Entity("ppds_city");
            // Use composite alternate key for upsert
            entity.KeyAttributes["ppds_name"] = c.Name;
            entity.KeyAttributes["ppds_stateid"] = stateMap[c.StateAbbreviation];
            // Only set the state lookup in Attributes (name is in KeyAttributes already)
            entity["ppds_stateid"] = new EntityReference("ppds_state", stateMap[c.StateAbbreviation]);
            return entity;
        }).ToList();

        var progress = new Progress<ProgressSnapshot>(s =>
        {
            Console.WriteLine($"    Progress: {s.Processed:N0}/{s.Total:N0} ({s.PercentComplete:F1}%) " +
                $"| {s.RatePerSecond:F0}/s | {s.Elapsed:mm\\:ss} elapsed | ETA: {s.EstimatedRemaining:mm\\:ss}");
        });

        var upsertResult = await bulkExecutor.UpsertMultipleAsync("ppds_city", cityEntities, progress: progress);

        if (upsertResult.CreatedCount.HasValue && upsertResult.UpdatedCount.HasValue)
        {
            Console.WriteLine($"    Upserted: {upsertResult.SuccessCount:N0} ({upsertResult.CreatedCount:N0} created, {upsertResult.UpdatedCount:N0} updated)");
        }
        else
        {
            Console.WriteLine($"    Succeeded: {upsertResult.SuccessCount:N0}");
        }

        if (upsertResult.FailureCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            foreach (var error in upsertResult.Errors.Take(5))
            {
                Console.WriteLine($"      Error at index {error.Index}: {error.Message}");
            }
            if (upsertResult.Errors.Count > 5)
            {
                Console.WriteLine($"      ... and {upsertResult.Errors.Count - 5} more errors");
            }
            Console.ResetColor();
        }

        // Re-query to get all city IDs (including newly created ones)
        Console.Write("  Rebuilding city map... ");
        cityMap.Clear();
        query.PageInfo.PageNumber = 1;
        query.PageInfo.PagingCookie = null;

        while (true)
        {
            var result = await client.RetrieveMultipleAsync(query);
            foreach (var entity in result.Entities)
            {
                var name = entity.GetAttributeValue<string>("ppds_name");
                var stateRef = entity.GetAttributeValue<EntityReference>("ppds_stateid");
                if (!string.IsNullOrEmpty(name) && stateRef != null && reverseStateMap.TryGetValue(stateRef.Id, out var stateAbbr))
                {
                    var key = $"{name}|{stateAbbr}";
                    cityMap[key] = entity.Id;
                }
            }

            if (!result.MoreRecords)
                break;

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
        Console.WriteLine($"{cityMap.Count:N0} cities mapped");

        return cityMap;
    }

    /// <summary>
    /// Builds Entity objects for ZIP code upsert from CSV records.
    /// Normalizes, deduplicates, and maps state and city references.
    /// </summary>
    private static (List<Entity> Entities, int Skipped) BuildZipCodeEntities(
        List<ZipCodeRecord> zipCodes,
        Dictionary<string, Guid> stateMap,
        Dictionary<string, Guid> cityMap,
        bool verbose)
    {
        // Normalize and deduplicate by ZIP code (CSV may contain duplicates or whitespace)
        var uniqueZipCodes = zipCodes
            .Select(z => new { Record = z, NormalizedZip = z.Zip?.Trim() ?? "" })
            .Where(x => !string.IsNullOrEmpty(x.NormalizedZip))
            .GroupBy(x => x.NormalizedZip)
            .Select(g =>
            {
                var first = g.First();
                first.Record.Zip = first.NormalizedZip;
                return first.Record;
            })
            .ToList();

        var duplicateCount = zipCodes.Count - uniqueZipCodes.Count;
        if (duplicateCount > 0)
        {
            Console.WriteLine($"  Deduplicated: {duplicateCount:N0} duplicate/empty ZIP codes removed");

            if (verbose)
            {
                var duplicates = zipCodes
                    .GroupBy(z => z.Zip?.Trim() ?? "")
                    .Where(g => g.Count() > 1)
                    .Take(5)
                    .ToList();

                if (duplicates.Count > 0)
                {
                    Console.WriteLine("    Sample duplicates:");
                    foreach (var dup in duplicates)
                    {
                        Console.WriteLine($"      '{dup.Key}' appears {dup.Count()} times");
                    }
                }
            }
        }

        // Build entities
        var entities = new List<Entity>();
        var skippedCount = 0;

        foreach (var zip in uniqueZipCodes)
        {
            if (!stateMap.TryGetValue(zip.StateId, out var stateId))
            {
                skippedCount++;
                continue;
            }

            // Build city key and lookup city ID
            var cityKey = $"{zip.City}|{zip.StateId}";
            if (!cityMap.TryGetValue(cityKey, out var cityId))
            {
                skippedCount++;
                continue;
            }

            var entity = new Entity("ppds_zipcode");
            // Set alternate key for upsert (do NOT also set in Attributes - see SDK docs)
            entity.KeyAttributes["ppds_code"] = zip.Zip;
            entity["ppds_stateid"] = new EntityReference("ppds_state", stateId);
            entity["ppds_cityid"] = new EntityReference("ppds_city", cityId);
            entity["ppds_county"] = zip.County;
            entity["ppds_latitude"] = zip.Lat;
            entity["ppds_longitude"] = zip.Lng;

            entities.Add(entity);
        }

        return (entities, skippedCount);
    }

    // CSV record class matching GitHub free_zipcode_data format
    // Headers: code,city,state,county,area_code,lat,lon
    private class ZipCodeRecord
    {
        [Name("code")]
        public string Zip { get; set; } = "";

        [Name("city")]
        public string City { get; set; } = "";

        [Name("state")]
        public string StateId { get; set; } = "";

        [Name("county")]
        public string? County { get; set; }

        [Name("lat")]
        public decimal? Lat { get; set; }

        [Name("lon")]
        public decimal? Lng { get; set; }

        // No population in this dataset
        public int? Population => null;

        // State name is derived from abbreviation
        public string StateName => StateAbbreviations.TryGetValue(StateId, out var name) ? name : StateId;
    }

    // State abbreviation to name mapping
    private static readonly Dictionary<string, string> StateAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona", ["AR"] = "Arkansas",
        ["CA"] = "California", ["CO"] = "Colorado", ["CT"] = "Connecticut", ["DE"] = "Delaware",
        ["FL"] = "Florida", ["GA"] = "Georgia", ["HI"] = "Hawaii", ["ID"] = "Idaho",
        ["IL"] = "Illinois", ["IN"] = "Indiana", ["IA"] = "Iowa", ["KS"] = "Kansas",
        ["KY"] = "Kentucky", ["LA"] = "Louisiana", ["ME"] = "Maine", ["MD"] = "Maryland",
        ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota", ["MS"] = "Mississippi",
        ["MO"] = "Missouri", ["MT"] = "Montana", ["NE"] = "Nebraska", ["NV"] = "Nevada",
        ["NH"] = "New Hampshire", ["NJ"] = "New Jersey", ["NM"] = "New Mexico", ["NY"] = "New York",
        ["NC"] = "North Carolina", ["ND"] = "North Dakota", ["OH"] = "Ohio", ["OK"] = "Oklahoma",
        ["OR"] = "Oregon", ["PA"] = "Pennsylvania", ["RI"] = "Rhode Island", ["SC"] = "South Carolina",
        ["SD"] = "South Dakota", ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah",
        ["VT"] = "Vermont", ["VA"] = "Virginia", ["WA"] = "Washington", ["WV"] = "West Virginia",
        ["WI"] = "Wisconsin", ["WY"] = "Wyoming", ["DC"] = "District of Columbia",
        ["PR"] = "Puerto Rico", ["VI"] = "Virgin Islands", ["GU"] = "Guam",
        ["AS"] = "American Samoa", ["MP"] = "Northern Mariana Islands"
    };

    private class StateRecord
    {
        public string Abbreviation { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private class CityRecord
    {
        public string Name { get; set; } = "";
        public string StateAbbreviation { get; set; } = "";

        /// <summary>
        /// Composite key for city uniqueness (city names can repeat across states).
        /// </summary>
        public string Key => $"{Name}|{StateAbbreviation}";
    }
}
