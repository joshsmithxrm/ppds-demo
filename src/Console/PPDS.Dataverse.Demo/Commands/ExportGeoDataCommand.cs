using System.CommandLine;
using System.Diagnostics;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Exports geographic reference data to a portable ZIP package.
///
/// This command demonstrates the ppds-migrate CLI export workflow:
///   1. Generate schema: ppds-migrate schema generate -e ppds_state,ppds_city,ppds_zipcode
///   2. Export data: ppds-migrate export --schema schema.xml --output data.zip
///
/// The resulting package can be:
///   - Stored in artifact repositories (Azure Artifacts, Git LFS, S3)
///   - Versioned alongside solution exports
///   - Imported to other environments using import-geo-data command
///
/// Usage:
///   dotnet run -- export-geo-data --output geo-v1.0.zip
///   dotnet run -- export-geo-data --output artifacts/geo-data.zip --env Dev --verbose
/// </summary>
public static class ExportGeoDataCommand
{
    private static readonly string DefaultSchemaPath = Path.Combine(AppContext.BaseDirectory, "migration", "geo-schema.xml");
    private static readonly string DefaultOutputPath = Path.Combine(AppContext.BaseDirectory, "geo-export.zip");
    private static readonly string[] GeoEntities = ["ppds_state", "ppds_city", "ppds_zipcode"];

    /// <summary>
    /// Attribute filter for schema generation - only export fields we actually populate.
    /// </summary>
    private static readonly Dictionary<string, string[]> GeoEntityAttributes = new()
    {
        ["ppds_state"] = ["ppds_stateid", "ppds_name", "ppds_abbreviation"],
        ["ppds_city"] = ["ppds_cityid", "ppds_name", "ppds_stateid"],
        ["ppds_zipcode"] = ["ppds_zipcodeid", "ppds_code", "ppds_stateid", "ppds_cityid", "ppds_county", "ppds_latitude", "ppds_longitude"]
    };

    public static Command Create()
    {
        var command = new Command("export-geo-data", "Export geographic data to a portable ZIP package");

        var outputOption = new Option<string?>(
            aliases: ["--output", "-o"],
            description: $"Output ZIP file path (default: geo-export.zip)");

        // Use standardized options from GlobalOptionsExtensions
        var envOption = GlobalOptionsExtensions.CreateEnvironmentOption();
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        command.AddOption(outputOption);
        command.AddOption(envOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (string? output, string? environment, bool verbose, bool debug) =>
        {
            var options = new GlobalOptions
            {
                Environment = environment ?? "Dev",
                Verbose = verbose,
                Debug = debug
            };
            Environment.ExitCode = await ExecuteAsync(output, options);
        }, outputOption, envOption, verboseOption, debugOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(
        string? outputPath,
        GlobalOptions options)
    {
        var output = outputPath ?? DefaultOutputPath;

        ConsoleWriter.Header("Export Geographic Data");

        // Create CLI client with logging if verbose
        var cli = options.EffectiveVerbose
            ? MigrationCli.CreateWithConsoleLogging()
            : new MigrationCli();

        // Verify CLI exists
        if (!cli.Exists)
        {
            ConsoleWriter.Error($"CLI not found: {cli.CliPath}");
            Console.WriteLine("Build the CLI first: dotnet build ../sdk/src/PPDS.Migration.Cli");
            return 1;
        }

        // Create connection pool to verify source data
        using var host = CommandBase.CreateHost(options);
        var pool = CommandBase.GetConnectionPool(host);

        if (pool == null)
        {
            ConsoleWriter.Error($"{options.Environment} environment not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        // Ensure migration directory exists
        var schemaDir = Path.GetDirectoryName(DefaultSchemaPath);
        if (!string.IsNullOrEmpty(schemaDir) && !Directory.Exists(schemaDir))
        {
            Directory.CreateDirectory(schemaDir);
        }

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Console.WriteLine($"  Environment: {options.Environment}");
            Console.WriteLine($"  Output: {Path.GetFullPath(output)}");
            Console.WriteLine();

            // ===================================================================
            // STEP 1: Verify Source Data
            // ===================================================================
            ConsoleWriter.Section("Step 1: Verify Source Data");

            await using var client = await pool.GetClientAsync();
            var summary = await QueryGeoSummary(client);

            Console.WriteLine($"  States: {summary.StateCount}");
            Console.WriteLine($"  Cities: {summary.CityCount}");
            Console.WriteLine($"  ZIP Codes: {summary.ZipCodeCount:N0}");
            Console.WriteLine($"  Total: {summary.TotalCount:N0} records");
            Console.WriteLine();

            if (summary.TotalCount == 0)
            {
                ConsoleWriter.Error($"No geo data found in {options.Environment}. Run load-geo-data first.");
                return 1;
            }

            // ===================================================================
            // STEP 2: Generate Schema
            // ===================================================================
            ConsoleWriter.Section("Step 2: Generate Schema (ppds-migrate schema generate)");

            Console.Write($"  Generating schema for: {string.Join(", ", GeoEntities)}... ");

            var schemaResult = await cli.SchemaGenerateAsync(
                GeoEntities,
                DefaultSchemaPath,
                options,
                includeRelationships: true,
                includeAttributes: GeoEntityAttributes);

            if (schemaResult.Failed)
            {
                ConsoleWriter.Error("Schema generation failed");
                return 1;
            }
            ConsoleWriter.Success("Done");
            Console.WriteLine($"  Schema file: {DefaultSchemaPath}");
            Console.WriteLine();

            // ===================================================================
            // STEP 3: Export Data
            // ===================================================================
            ConsoleWriter.Section("Step 3: Export Data (ppds-migrate export)");

            Console.Write("  Exporting data package... ");
            var exportResult = await cli.ExportAsync(
                DefaultSchemaPath,
                output,
                options);

            if (exportResult.Failed)
            {
                ConsoleWriter.Error("Export failed");
                return 1;
            }

            var fileInfo = new FileInfo(output);
            ConsoleWriter.Success($"Done ({fileInfo.Length / 1024} KB)");
            Console.WriteLine();

            stopwatch.Stop();

            // ===================================================================
            // RESULT
            // ===================================================================
            ConsoleWriter.ResultBanner("Export Complete", success: true);
            Console.WriteLine();
            Console.WriteLine($"  Package: {Path.GetFullPath(output)}");
            Console.WriteLine($"  Size: {fileInfo.Length / 1024} KB");
            Console.WriteLine($"  Records: {summary.TotalCount:N0}");
            Console.WriteLine($"  Time: {stopwatch.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine();
            Console.WriteLine("  Next steps:");
            Console.WriteLine($"    Import to QA:   dotnet run -- import-geo-data --data \"{output}\" --env QA");
            Console.WriteLine($"    Import to Prod: ppds-migrate import --data \"{output}\" --mode Upsert --env Prod");

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
        var cityResult = await client.RetrieveMultipleAsync(cityQuery);
        summary.CityCount = cityResult.Entities.Count;

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
