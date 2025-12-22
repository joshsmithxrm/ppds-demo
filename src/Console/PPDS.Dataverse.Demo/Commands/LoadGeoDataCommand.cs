using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

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

        var batchSizeOption = new Option<int>(
            "--batch-size",
            () => 100,
            "Batch size for ExecuteMultiple requests (1-1000)");

        var skipDownloadOption = new Option<bool>(
            "--skip-download",
            "Use cached data file (skip download)");

        var statesOnlyOption = new Option<bool>(
            "--states-only",
            "Only load states (skip ZIP codes)");

        command.AddOption(limitOption);
        command.AddOption(batchSizeOption);
        command.AddOption(skipDownloadOption);
        command.AddOption(statesOnlyOption);

        command.SetHandler(async (int? limit, int batchSize, bool skipDownload, bool statesOnly) =>
        {
            Environment.ExitCode = await ExecuteAsync(limit, batchSize, skipDownload, statesOnly);
        }, limitOption, batchSizeOption, skipDownloadOption, statesOnlyOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(int? limit, int batchSize, bool skipDownload, bool statesOnly)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       Load Geographic Data for Volume Testing                ║");
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

        var totalStopwatch = Stopwatch.StartNew();

        try
        {
            // ═══════════════════════════════════════════════════════════════════
            // PHASE 1: Download/Load CSV
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ Phase 1: Load CSV Data                                          │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

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

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 2: Connect to Dataverse
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ Phase 2: Connect to Dataverse                                   │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

            using var client = new ServiceClient(connectionString);
            if (!client.IsReady)
            {
                CommandBase.WriteError($"Connection failed: {client.LastError}");
                return 1;
            }

            Console.WriteLine($"  Connected to: {client.ConnectedOrgFriendlyName} ({envName})");
            Console.WriteLine($"  Batch size: {batchSize}");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 3: Load States
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ Phase 3: Load States                                            │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

            var stateStopwatch = Stopwatch.StartNew();

            // Extract unique states from ZIP data
            var states = zipCodes
                .GroupBy(z => z.StateId)
                .Select(g => new StateRecord
                {
                    Abbreviation = g.Key,
                    Name = g.First().StateName,
                    Population = g.Sum(z => z.Population ?? 0)
                })
                .OrderBy(s => s.Abbreviation)
                .ToList();

            Console.WriteLine($"  Found {states.Count} unique states/territories");

            // Check existing states and create missing ones
            var stateMap = await LoadOrCreateStatesAsync(client, states, batchSize);

            stateStopwatch.Stop();
            Console.WriteLine($"  State loading completed in {stateStopwatch.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine();

            if (statesOnly)
            {
                Console.WriteLine("  --states-only specified, skipping ZIP codes");
                PrintSummary(totalStopwatch, states.Count, 0, 0, 0);
                return 0;
            }

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 4: Load ZIP Codes
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ Phase 4: Load ZIP Codes                                         │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

            var zipStopwatch = Stopwatch.StartNew();

            var (created, updated, errors) = await LoadZipCodesAsync(client, zipCodes, stateMap, batchSize);

            zipStopwatch.Stop();

            Console.WriteLine();
            Console.WriteLine($"  ZIP code loading completed in {zipStopwatch.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine($"    Created: {created:N0}");
            Console.WriteLine($"    Updated: {updated:N0}");
            if (errors > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    Errors: {errors:N0}");
                Console.ResetColor();
            }

            var throughput = zipCodes.Count / zipStopwatch.Elapsed.TotalSeconds;
            Console.WriteLine($"    Throughput: {throughput:F1} records/second");
            Console.WriteLine();

            PrintSummary(totalStopwatch, states.Count, created, updated, errors);

            return errors > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            CommandBase.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintSummary(Stopwatch totalStopwatch, int states, int created, int updated, int errors)
    {
        totalStopwatch.Stop();

        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        if (errors == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("║              Data Load Complete                               ║");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("║              Data Load Complete (with errors)                 ║");
        }
        Console.ResetColor();
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  Total time: {totalStopwatch.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"  States: {states}");
        Console.WriteLine($"  ZIP codes created: {created:N0}");
        Console.WriteLine($"  ZIP codes updated: {updated:N0}");
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
        ServiceClient client,
        List<StateRecord> states,
        int batchSize)
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

        var requests = missing.Select(s => new CreateRequest
        {
            Target = new Entity("ppds_state")
            {
                ["ppds_name"] = s.Name,
                ["ppds_abbreviation"] = s.Abbreviation,
                ["ppds_population"] = s.Population
            }
        }).ToList();

        // States are few enough to do in one batch
        var executeMultiple = new ExecuteMultipleRequest
        {
            Requests = new OrganizationRequestCollection(),
            Settings = new ExecuteMultipleSettings
            {
                ContinueOnError = true,
                ReturnResponses = true
            }
        };
        executeMultiple.Requests.AddRange(requests);

        var response = (ExecuteMultipleResponse)await client.ExecuteAsync(executeMultiple);

        var created = 0;
        for (int i = 0; i < response.Responses.Count; i++)
        {
            var itemResponse = response.Responses[i];
            if (itemResponse.Fault == null && itemResponse.Response is CreateResponse createResponse)
            {
                stateMap[missing[i].Abbreviation] = createResponse.id;
                created++;
            }
        }

        Console.WriteLine($"created {created}");
        return stateMap;
    }

    private static async Task<(int created, int updated, int errors)> LoadZipCodesAsync(
        ServiceClient client,
        List<ZipCodeRecord> zipCodes,
        Dictionary<string, Guid> stateMap,
        int batchSize)
    {
        int totalCreated = 0, totalUpdated = 0, totalErrors = 0;
        var batches = zipCodes.Chunk(batchSize).ToList();

        Console.WriteLine($"  Processing {zipCodes.Count:N0} ZIP codes in {batches.Count:N0} batches...");

        var overallStopwatch = Stopwatch.StartNew();
        var intervalStopwatch = Stopwatch.StartNew();
        int processed = 0;
        int lastReportedCount = 0;

        foreach (var batch in batches)
        {
            var executeMultiple = new ExecuteMultipleRequest
            {
                Requests = new OrganizationRequestCollection(),
                Settings = new ExecuteMultipleSettings
                {
                    ContinueOnError = true,
                    ReturnResponses = true
                }
            };

            foreach (var zip in batch)
            {
                if (!stateMap.TryGetValue(zip.StateId, out var stateId))
                {
                    totalErrors++;
                    continue;
                }

                var entity = new Entity("ppds_zipcode");
                // Set alternate key for upsert matching
                entity.KeyAttributes["ppds_code"] = zip.Zip;
                // Set other attributes
                entity["ppds_stateid"] = new EntityReference("ppds_state", stateId);
                entity["ppds_cityname"] = zip.City;
                entity["ppds_county"] = zip.County;
                entity["ppds_latitude"] = zip.Lat;
                entity["ppds_longitude"] = zip.Lng;

                // Use Upsert with alternate key to handle existing records
                executeMultiple.Requests.Add(new UpsertRequest
                {
                    Target = entity
                });
            }

            if (executeMultiple.Requests.Count == 0)
                continue;

            try
            {
                var response = (ExecuteMultipleResponse)await client.ExecuteAsync(executeMultiple);

                foreach (var itemResponse in response.Responses)
                {
                    if (itemResponse.Fault != null)
                    {
                        totalErrors++;
                    }
                    else if (itemResponse.Response is UpsertResponse upsertResponse)
                    {
                        if (upsertResponse.RecordCreated)
                            totalCreated++;
                        else
                            totalUpdated++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"    Batch error: {ex.Message}");
                Console.ResetColor();
                totalErrors += batch.Length;
            }

            processed += batch.Length;

            // Progress update every 5 seconds
            if (intervalStopwatch.Elapsed.TotalSeconds >= 5)
            {
                var pct = (double)processed / zipCodes.Count * 100;
                var intervalElapsed = intervalStopwatch.Elapsed.TotalSeconds;
                var intervalCount = processed - lastReportedCount;
                var instantRate = intervalCount / intervalElapsed;
                var overallRate = processed / overallStopwatch.Elapsed.TotalSeconds;
                var remaining = (zipCodes.Count - processed) / overallRate;
                var elapsed = overallStopwatch.Elapsed;

                Console.WriteLine($"    Progress: {processed:N0}/{zipCodes.Count:N0} ({pct:F1}%) | {instantRate:F0}/s | Elapsed: {elapsed:mm\\:ss} | ETA: {remaining:F0}s");

                lastReportedCount = processed;
                intervalStopwatch.Restart();
            }
        }

        return (totalCreated, totalUpdated, totalErrors);
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
        public int Population { get; set; }
    }
}
