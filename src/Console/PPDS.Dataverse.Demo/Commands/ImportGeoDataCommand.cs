using System.CommandLine;
using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Dataverse.Pooling;
using PPDS.Dataverse.Resilience;
using PPDS.Migration.Import;
using PPDS.Migration.Progress;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Imports geographic reference data from a portable ZIP package.
///
/// This command uses the PPDS.Migration library directly for:
///   - Real-time progress output via ConsoleProgressReporter
///   - Typed ImportOptions with StripOwnerFields to avoid user reference errors
///   - Error pattern detection with actionable suggestions
///
/// The package should have been created by:
///   - export-geo-data command
///   - ppds-migrate export command
///
/// Supports:
///   - Upsert mode (default) - idempotent via alternate keys
///   - Clean-first option - removes existing data before import
///   - Strip owner fields (default: true) - avoids "systemuser Does Not Exist" errors
///
/// Usage:
///   dotnet run -- import-geo-data --data geo-v1.0.zip --env QA
///   dotnet run -- import-geo-data --data artifacts/geo-data.zip --env Prod --clean-first
/// </summary>
public static class ImportGeoDataCommand
{
    public static Command Create()
    {
        var command = new Command("import-geo-data", "Import geographic data from a ZIP package");

        var dataOption = new Option<string>(
            aliases: ["--data", "-d"],
            description: "Input ZIP file path (required)")
        {
            IsRequired = true
        };

        var cleanFirstOption = new Option<bool>(
            "--clean-first",
            "Run clean-geo-data before import");

        var stripOwnerFieldsOption = new Option<bool>(
            "--strip-owner-fields",
            getDefaultValue: () => true,
            description: "Strip owner fields to avoid user reference errors (default: true)");

        // Use standardized options from GlobalOptionsExtensions
        var envOption = GlobalOptionsExtensions.CreateEnvironmentOption(isRequired: true);
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        command.AddOption(dataOption);
        command.AddOption(envOption);
        command.AddOption(cleanFirstOption);
        command.AddOption(stripOwnerFieldsOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (string data, string? environment, bool cleanFirst, bool stripOwnerFields, bool verbose, bool debug) =>
        {
            var options = new GlobalOptions
            {
                Environment = environment!,
                Verbose = verbose,
                Debug = debug
            };
            Environment.ExitCode = await ExecuteAsync(data, options, cleanFirst, stripOwnerFields);
        }, dataOption, envOption, cleanFirstOption, stripOwnerFieldsOption, verboseOption, debugOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(
        string dataPath,
        GlobalOptions options,
        bool cleanFirst = false,
        bool stripOwnerFields = true)
    {
        ConsoleWriter.Header("Import Geographic Data");

        // Verify data package exists
        if (!File.Exists(dataPath))
        {
            ConsoleWriter.Error($"Data package not found: {dataPath}");
            Console.WriteLine("Create a package first: dotnet run -- export-geo-data --output geo-export.zip");
            return 1;
        }

        // Create host with migration services (uses library directly, no CLI)
        using var host = HostFactory.CreateHostForMigration(options);
        var pool = HostFactory.GetConnectionPool(host, options.Environment);

        if (pool == null)
        {
            ConsoleWriter.Error($"{options.Environment} environment not configured.");
            ConsoleWriter.ConnectionSetupInstructions(options.Environment);
            return 1;
        }

        // Get importer from DI
        var importer = host.Services.GetRequiredService<IImporter>();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Console.WriteLine($"  Environment: {options.Environment}");
            Console.WriteLine($"  Package: {Path.GetFullPath(dataPath)}");
            Console.WriteLine($"  Size: {new FileInfo(dataPath).Length / 1024} KB");
            Console.WriteLine($"  Strip owner fields: {stripOwnerFields}");
            Console.WriteLine();

            // ===================================================================
            // STEP 1: Inspect Package
            // ===================================================================
            ConsoleWriter.Section("Step 1: Inspect Package");

            var packageSummary = InspectPackage(dataPath);
            Console.WriteLine($"  States: {packageSummary.StateCount}");
            Console.WriteLine($"  Cities: {packageSummary.CityCount}");
            Console.WriteLine($"  ZIP Codes: {packageSummary.ZipCodeCount:N0}");
            Console.WriteLine($"  Total: {packageSummary.TotalCount:N0} records");
            Console.WriteLine();

            // ===================================================================
            // STEP 2: Check Target (before)
            // ===================================================================
            ConsoleWriter.Section("Step 2: Check Target (before import)");

            await using var client = await pool.GetClientAsync();
            var beforeSummary = await QueryGeoSummary(client);

            Console.WriteLine($"  States: {beforeSummary.StateCount}");
            Console.WriteLine($"  Cities: {beforeSummary.CityCount}");
            Console.WriteLine($"  ZIP Codes: {beforeSummary.ZipCodeCount:N0}");
            Console.WriteLine($"  Total: {beforeSummary.TotalCount:N0} records");
            Console.WriteLine();

            // ===================================================================
            // STEP 3: Clean Target (optional)
            // ===================================================================
            if (cleanFirst)
            {
                ConsoleWriter.Section("Step 3: Clean Target");

                if (beforeSummary.TotalCount > 0)
                {
                    Console.WriteLine($"  Removing {beforeSummary.TotalCount:N0} existing records...");

                    // Pass through GlobalOptions to CleanGeoDataCommand (with Conservative preset for deletes)
                    var cleanOptions = options with { RatePreset = RateControlPreset.Conservative };
                    var cleanResult = await CleanGeoDataCommand.ExecuteAsync(
                        zipOnly: false,
                        confirm: true,
                        cleanOptions);

                    if (cleanResult != 0)
                    {
                        ConsoleWriter.Error("Clean failed");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine("  Target is empty, nothing to clean.");
                }
                Console.WriteLine();
            }

            // ===================================================================
            // STEP 4: Import Data (using library directly)
            // ===================================================================
            var importStep = cleanFirst ? "4" : "3";
            ConsoleWriter.Section($"Step {importStep}: Import Data (PPDS.Migration library)");

            Console.WriteLine("  Importing data package with real-time progress...");
            Console.WriteLine();

            // Create progress reporter for real-time output
            var progress = new ConsoleProgressReporter();

            // Configure import options - StripOwnerFields is the key fix!
            var importOptions = new ImportOptions
            {
                Mode = ImportMode.Upsert,
                StripOwnerFields = stripOwnerFields,  // Fixes "systemuser Does Not Exist" errors
                ContinueOnError = true
            };

            // Direct library call - no more CLI wrapper!
            var importResult = await importer.ImportAsync(
                dataPath,
                importOptions,
                progress,
                CancellationToken.None);

            Console.WriteLine();

            if (!importResult.Success)
            {
                ConsoleWriter.Error($"Import completed with {importResult.Errors.Count} errors");
                // ConsoleProgressReporter already displayed error details and suggestions
                return 1;
            }

            ConsoleWriter.Success($"Import complete: {importResult.RecordsImported:N0} records in {importResult.Duration.TotalSeconds:F2}s");
            Console.WriteLine();

            // ===================================================================
            // STEP 5: Verify Target (after)
            // ===================================================================
            var verifyStep = cleanFirst ? "5" : "4";
            ConsoleWriter.Section($"Step {verifyStep}: Verify Target (after import)");

            // Need a fresh client for accurate counts
            await using var verifyClient = await pool.GetClientAsync();
            var afterSummary = await QueryGeoSummary(verifyClient);

            Console.WriteLine($"  States: {afterSummary.StateCount}");
            Console.WriteLine($"  Cities: {afterSummary.CityCount}");
            Console.WriteLine($"  ZIP Codes: {afterSummary.ZipCodeCount:N0}");
            Console.WriteLine($"  Total: {afterSummary.TotalCount:N0} records");
            Console.WriteLine();

            // Compare package vs target
            Console.WriteLine("  Verification (package vs target):");
            var passed = true;

            var stateMatch = packageSummary.StateCount == afterSummary.StateCount;
            Console.Write($"    States: {packageSummary.StateCount} vs {afterSummary.StateCount} ");
            ConsoleWriter.PassFail(stateMatch);
            passed &= stateMatch;

            var cityMatch = packageSummary.CityCount == afterSummary.CityCount;
            Console.Write($"    Cities: {packageSummary.CityCount} vs {afterSummary.CityCount} ");
            ConsoleWriter.PassFail(cityMatch);
            passed &= cityMatch;

            var zipMatch = packageSummary.ZipCodeCount == afterSummary.ZipCodeCount;
            Console.Write($"    ZIP Codes: {packageSummary.ZipCodeCount:N0} vs {afterSummary.ZipCodeCount:N0} ");
            ConsoleWriter.PassFail(zipMatch);
            passed &= zipMatch;

            Console.WriteLine();

            stopwatch.Stop();

            // ===================================================================
            // RESULT
            // ===================================================================
            if (passed)
            {
                ConsoleWriter.ResultBanner("Import Complete", success: true);
                Console.WriteLine();
                Console.WriteLine($"  Environment: {options.Environment}");
                Console.WriteLine($"  Records: {afterSummary.TotalCount:N0}");
                Console.WriteLine($"  Time: {stopwatch.Elapsed.TotalSeconds:F2}s");
                return 0;
            }
            else
            {
                ConsoleWriter.ResultBanner("Import Complete (with verification warnings)", success: false);
                Console.WriteLine();
                Console.WriteLine("  Some counts don't match. This may be expected if:");
                Console.WriteLine("    - Target had existing data (use --clean-first)");
                Console.WriteLine("    - Some records failed validation");
                return 1;
            }
        }
        catch (Exception ex)
        {
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }

    private static PackageSummary InspectPackage(string zipPath)
    {
        var summary = new PackageSummary();

        using var archive = ZipFile.OpenRead(zipPath);
        var dataEntry = archive.GetEntry("data.xml");
        if (dataEntry == null)
        {
            return summary;
        }

        using var stream = dataEntry.Open();
        var doc = XDocument.Load(stream);

        foreach (var entity in doc.Descendants("entity"))
        {
            var entityName = entity.Attribute("name")?.Value ?? "";
            var records = entity.Descendants("record").Count();

            switch (entityName)
            {
                case "ppds_state":
                    summary.StateCount = records;
                    break;
                case "ppds_city":
                    summary.CityCount = records;
                    break;
                case "ppds_zipcode":
                    summary.ZipCodeCount = records;
                    break;
            }
        }

        return summary;
    }

    private static async Task<GeoSummary> QueryGeoSummary(IPooledClient client)
    {
        var summary = new GeoSummary();

        var stateQuery = new Microsoft.Xrm.Sdk.Query.QueryExpression("ppds_state")
        {
            ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false),
            PageInfo = new Microsoft.Xrm.Sdk.Query.PagingInfo { Count = 5000, PageNumber = 1 }
        };
        var stateResult = await client.RetrieveMultipleAsync(stateQuery);
        summary.StateCount = stateResult.Entities.Count;

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

    private record PackageSummary
    {
        public int StateCount { get; set; }
        public int CityCount { get; set; }
        public int ZipCodeCount { get; set; }
        public int TotalCount => StateCount + CityCount + ZipCodeCount;
    }

    private record GeoSummary
    {
        public int StateCount { get; set; }
        public int CityCount { get; set; }
        public int ZipCodeCount { get; set; }
        public int TotalCount => StateCount + CityCount + ZipCodeCount;
    }
}
