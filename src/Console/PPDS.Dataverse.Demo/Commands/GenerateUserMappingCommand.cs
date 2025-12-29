using System.CommandLine;
using Microsoft.Extensions.Logging;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Migration.UserMapping;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Generates a user mapping file for cross-environment migration.
///
/// This command uses the PPDS.Migration library directly:
///   - IUserMappingGenerator to query users and match across environments
///   - Matches by Azure AD Object ID (primary) or domain name (fallback)
///   - Generates XML mapping file for use with import operations
///
/// Usage:
///   dotnet run -- generate-user-mapping
///   dotnet run -- generate-user-mapping --output user-mapping.xml
///   dotnet run -- generate-user-mapping --analyze
/// </summary>
public static class GenerateUserMappingCommand
{
    private static readonly string OutputPath = Path.Combine(AppContext.BaseDirectory, "user-mapping.xml");

    public static Command Create()
    {
        var command = new Command("generate-user-mapping", "Generate user mapping file for cross-environment migration");

        var outputOption = new Option<string>(
            "--output",
            () => OutputPath,
            "Output path for the user mapping XML file");

        var analyzeOnlyOption = new Option<bool>(
            "--analyze",
            "Analyze user differences without generating mapping file");

        // Use standardized options from GlobalOptionsExtensions
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        command.AddOption(outputOption);
        command.AddOption(analyzeOnlyOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (string output, bool analyzeOnly, bool verbose, bool debug) =>
        {
            var options = new GlobalOptions
            {
                Verbose = verbose,
                Debug = debug
            };
            Environment.ExitCode = await ExecuteAsync(output, analyzeOnly, options);
        }, outputOption, analyzeOnlyOption, verboseOption, debugOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(string outputPath, bool analyzeOnly, GlobalOptions options)
    {
        ConsoleWriter.Header("Generate User Mapping: Dev -> QA");

        // Create pools for both environments
        var devOptions = options with { Environment = "Dev" };
        var qaOptions = options with { Environment = "QA" };

        using var devHost = HostFactory.CreateHostForMigration(devOptions);
        using var qaHost = HostFactory.CreateHostForMigration(qaOptions);

        var devPool = HostFactory.GetConnectionPool(devHost, "Dev");
        var qaPool = HostFactory.GetConnectionPool(qaHost, "QA");

        if (devPool == null)
        {
            ConsoleWriter.Error("Dev environment not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        if (qaPool == null)
        {
            ConsoleWriter.Error("QA environment not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        try
        {
            Console.WriteLine("  Mode: Library (PPDS.Migration.UserMapping)");
            Console.WriteLine();

            // Create logger if verbose/debug is enabled
            ILogger<UserMappingGenerator>? logger = null;
            if (options.EffectiveVerbose)
            {
                var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.AddSimpleConsole(opts =>
                    {
                        opts.SingleLine = true;
                        opts.TimestampFormat = "HH:mm:ss.fff ";
                    });
                    builder.SetMinimumLevel(options.Debug ? LogLevel.Debug : LogLevel.Information);
                });
                logger = loggerFactory.CreateLogger<UserMappingGenerator>();
            }

            // Use the PPDS.Migration library directly
            var generator = new UserMappingGenerator(logger!);

            Console.WriteLine("  Generating user mapping...");
            Console.WriteLine();

            // Generate mappings using the library
            var result = await generator.GenerateAsync(
                devPool,
                qaPool,
                new UserMappingOptions(),
                CancellationToken.None);

            // Report results
            Console.WriteLine("  Source (Dev):");
            Console.WriteLine($"    Users: {result.SourceUserCount}");
            Console.WriteLine();

            Console.WriteLine("  Target (QA):");
            Console.WriteLine($"    Users: {result.TargetUserCount}");
            Console.WriteLine();

            Console.WriteLine("  Mapping Results:");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"    Matched: {result.Mappings.Count}");
            Console.ResetColor();

            Console.WriteLine($"      By AAD Object ID: {result.MatchedByAadId}");
            Console.WriteLine($"      By Domain Name: {result.MatchedByDomain}");

            if (result.UnmappedUsers.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    Unmapped: {result.UnmappedUsers.Count}");
                Console.ResetColor();
            }
            Console.WriteLine();

            // Show sample mappings
            Console.WriteLine("  Sample Mappings (first 5):");
            foreach (var mapping in result.Mappings.Take(5))
            {
                Console.WriteLine($"    {mapping.Source.FullName}");
                Console.WriteLine($"      Dev: {mapping.Source.SystemUserId}");
                Console.WriteLine($"      QA:  {mapping.Target.SystemUserId} (matched by {mapping.MatchedBy})");
            }
            Console.WriteLine();

            // Show unmapped users
            if (result.UnmappedUsers.Count > 0)
            {
                Console.WriteLine("  Unmapped Users (first 10):");
                foreach (var user in result.UnmappedUsers.Take(10))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"    {user.FullName} ({user.DomainName ?? "no domain"})");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }

            if (analyzeOnly)
            {
                Console.WriteLine("  [ANALYZE ONLY] No mapping file generated.");
                return 0;
            }

            // Write mapping file using the library
            Console.WriteLine($"  Generating mapping file: {outputPath}");
            await generator.WriteAsync(result, outputPath, CancellationToken.None);

            ConsoleWriter.Success($"  Generated {result.Mappings.Count} mappings");
            Console.WriteLine();

            Console.WriteLine("  Usage:");
            Console.WriteLine($"    ppds-migrate import --data <file> --user-mapping \"{outputPath}\"");

            return 0;
        }
        catch (Exception ex)
        {
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }
}
