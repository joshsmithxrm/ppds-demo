using System.CommandLine;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PPDS.Dataverse.Demo.Models;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Cross-environment migration test: Export from Dev, Import to QA.
///
/// Requires two environment connections in User Secrets:
///   Environments:Dev:ConnectionString = Dev (source)
///   Environments:QA:ConnectionString = QA (target)
/// </summary>
public static class CrossEnvMigrationCommand
{
    private static readonly string CliPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..",
            "sdk", "src", "PPDS.Migration.Cli", "bin", "Debug", "net8.0", "ppds-migrate.exe"));

    private static readonly string SchemaPath = Path.Combine(AppContext.BaseDirectory, "cross-env-schema.xml");
    private static readonly string DataPath = Path.Combine(AppContext.BaseDirectory, "cross-env-export.zip");
    private static readonly string UserMappingPath = Path.Combine(AppContext.BaseDirectory, "user-mapping.xml");

    public static Command Create()
    {
        var command = new Command("migrate-to-qa", "Export from Dev and import to QA environment");

        var skipSeedOption = new Option<bool>(
            "--skip-seed",
            "Skip seeding data in Dev (use existing data)");

        var dryRunOption = new Option<bool>(
            "--dry-run",
            "Export only, don't import to QA");

        var includeM2MOption = new Option<bool>(
            "--include-m2m",
            "Include M2M relationships (systemuserroles, teammembership)");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            "Show detailed output including CLI commands");

        command.AddOption(skipSeedOption);
        command.AddOption(dryRunOption);
        command.AddOption(includeM2MOption);
        command.AddOption(verboseOption);

        command.SetHandler(async (bool skipSeed, bool dryRun, bool includeM2M, bool verbose) =>
        {
            Environment.ExitCode = await ExecuteAsync(skipSeed, dryRun, includeM2M, verbose);
        }, skipSeedOption, dryRunOption, includeM2MOption, verboseOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(bool skipSeed, bool dryRun, bool includeM2M, bool verbose = false)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       Cross-Environment Migration: Dev → QA                  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Verify CLI exists
        if (!File.Exists(CliPath))
        {
            CommandBase.WriteError($"CLI not found: {CliPath}");
            Console.WriteLine("Build the CLI first: dotnet build ../sdk/src/PPDS.Migration.Cli");
            return 1;
        }

        using var host = CommandBase.CreateHost([]);
        var config = host.Services.GetRequiredService<IConfiguration>();

        // Get both environment connection strings
        var (devConnectionString, devName) = CommandBase.ResolveEnvironment(config, "Dev");
        var (qaConnectionString, qaName) = CommandBase.ResolveEnvironment(config, "QA");

        if (string.IsNullOrEmpty(devConnectionString))
        {
            CommandBase.WriteError("Dev connection not found. Configure Environments:Dev:ConnectionString in user-secrets.");
            return 1;
        }

        if (string.IsNullOrEmpty(qaConnectionString))
        {
            CommandBase.WriteError("QA connection not found.");
            Console.WriteLine();
            Console.WriteLine("Set up QA connection:");
            Console.WriteLine("  dotnet user-secrets set \"Environments:QA:Name\" \"QA\"");
            Console.WriteLine("  dotnet user-secrets set \"Environments:QA:ConnectionString\" \"AuthType=...\"");
            return 1;
        }

        // Display environment info
        Console.WriteLine("  Environments:");
        Console.WriteLine($"    Source: {devName} ({ExtractOrgFromConnectionString(devConnectionString)})");
        Console.WriteLine($"    Target: {qaName} ({ExtractOrgFromConnectionString(qaConnectionString)})");
        Console.WriteLine();

        if (dryRun)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  [DRY RUN] Will export only, no import to QA");
            Console.ResetColor();
            Console.WriteLine();
        }

        try
        {
            // ═══════════════════════════════════════════════════════════════════
            // PHASE 1: Seed test data in Dev (optional)
            // ═══════════════════════════════════════════════════════════════════
            if (!skipSeed)
            {
                Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
                Console.WriteLine("│ Phase 1: Seed Test Data in Dev                                 │");
                Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

                var seedResult = await SeedCommand.ExecuteAsync([]);
                if (seedResult != 0)
                {
                    CommandBase.WriteError("Seed failed");
                    return 1;
                }
                Console.WriteLine();
            }

            // Verify source data
            Console.WriteLine("  Verifying source data in Dev...");
            using var devClient = new ServiceClient(devConnectionString);
            if (!devClient.IsReady)
            {
                CommandBase.WriteError($"Failed to connect to Dev: {devClient.LastError}");
                return 1;
            }

            var sourceData = await QueryTestData(devClient);
            PrintDataSummary("  Dev", sourceData);
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 2: Generate schema and export from Dev
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ Phase 2: Export from Dev                                        │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

            // Generate schema
            // NOTE: Never include systemuser/team in cross-env migration - they're system entities
            // managed by the platform. User references (ownerid) are handled by user mapping.
            Console.Write("  Generating schema... ");
            var entities = "account,contact";
            var schemaArgs = $"schema generate -e {entities} -o \"{SchemaPath}\" --connection \"{devConnectionString}\"";
            if (includeM2M)
            {
                // Only include M2M relationships between business entities (e.g., account-contact N:N)
                schemaArgs += " --include-relationships";
            }

            var schemaResult = await RunCliAsync(schemaArgs, verbose);
            if (schemaResult != 0)
            {
                CommandBase.WriteError("Schema generation failed");
                return 1;
            }
            CommandBase.WriteSuccess("Done");

            // Export data
            Console.Write("  Exporting data... ");
            var exportResult = await RunCliAsync(
                $"export --schema \"{SchemaPath}\" --output \"{DataPath}\" --connection \"{devConnectionString}\"", verbose);
            if (exportResult != 0)
            {
                CommandBase.WriteError("Export failed");
                return 1;
            }
            CommandBase.WriteSuccess($"Done ({new FileInfo(DataPath).Length / 1024} KB)");

            // Inspect exported data
            Console.WriteLine("  Exported data summary:");
            InspectExportedData(DataPath);
            Console.WriteLine();

            if (dryRun)
            {
                Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("║           DRY RUN COMPLETE - No import performed             ║");
                Console.ResetColor();
                Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
                Console.WriteLine();
                Console.WriteLine($"  Export file: {DataPath}");
                Console.WriteLine($"  Schema file: {SchemaPath}");
                Console.WriteLine();
                Console.WriteLine("  To import manually:");
                Console.WriteLine($"    ppds-migrate import --data \"{DataPath}\" --connection \"<QA connection>\"");
                return 0;
            }

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 3: Generate user mapping
            // ═══════════════════════════════════════════════════════════════════
            // Always generate user mapping for cross-env migration - ownerid fields need remapping
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ Phase 3: Generate User Mapping                                  │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

            var mappingResult = await GenerateUserMappingCommand.ExecuteAsync(UserMappingPath, analyzeOnly: false);
            if (mappingResult != 0)
            {
                CommandBase.WriteError("User mapping generation failed");
                return 1;
            }
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 4: Import to QA
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ Phase 4: Import to QA                                           │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

            var importArgs = $"import --data \"{DataPath}\" --mode Upsert --connection \"{qaConnectionString}\"";
            if (File.Exists(UserMappingPath))
            {
                importArgs += $" --user-mapping \"{UserMappingPath}\"";
                Console.WriteLine($"  Using user mapping: {UserMappingPath}");
            }

            Console.Write("  Importing data to QA... ");
            var importResult = await RunCliAsync(importArgs, verbose);
            if (importResult != 0)
            {
                CommandBase.WriteError("Import failed");
                return 1;
            }
            CommandBase.WriteSuccess("Done");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 5: Verify in QA
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ Phase 5: Verify in QA                                           │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

            using var qaClient = new ServiceClient(qaConnectionString);
            if (!qaClient.IsReady)
            {
                CommandBase.WriteError($"Failed to connect to QA: {qaClient.LastError}");
                return 1;
            }

            var targetData = await QueryTestData(qaClient);
            PrintDataSummary("  QA", targetData);
            Console.WriteLine();

            // Compare
            Console.WriteLine("  Comparison (Dev → QA):");
            var passed = true;

            // Account count
            var accountMatch = sourceData.Accounts.Count == targetData.Accounts.Count;
            Console.Write($"    Accounts: {sourceData.Accounts.Count} → {targetData.Accounts.Count} ");
            WritePassFail(accountMatch);
            passed &= accountMatch;

            // Contact count
            var contactMatch = sourceData.Contacts.Count == targetData.Contacts.Count;
            Console.Write($"    Contacts: {sourceData.Contacts.Count} → {targetData.Contacts.Count} ");
            WritePassFail(contactMatch);
            passed &= contactMatch;

            // Parent account relationships
            var sourceParents = sourceData.Accounts.Count(a => a.ParentAccountId.HasValue);
            var targetParents = targetData.Accounts.Count(a => a.ParentAccountId.HasValue);
            var parentMatch = sourceParents == targetParents;
            Console.Write($"    Parent refs: {sourceParents} → {targetParents} ");
            WritePassFail(parentMatch);
            passed &= parentMatch;

            // Contact company relationships
            var sourceCompany = sourceData.Contacts.Count(c => c.ParentCustomerId.HasValue);
            var targetCompany = targetData.Contacts.Count(c => c.ParentCustomerId.HasValue);
            var companyMatch = sourceCompany == targetCompany;
            Console.Write($"    Company refs: {sourceCompany} → {targetCompany} ");
            WritePassFail(companyMatch);
            passed &= companyMatch;

            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // RESULT
            // ═══════════════════════════════════════════════════════════════════
            if (passed)
            {
                Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("║         MIGRATION COMPLETE: Dev → QA SUCCESS                 ║");
                Console.ResetColor();
                Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
                return 0;
            }
            else
            {
                Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("║         MIGRATION VERIFICATION FAILED                        ║");
                Console.ResetColor();
                Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
                return 1;
            }
        }
        catch (Exception ex)
        {
            CommandBase.WriteError($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static string ExtractOrgFromConnectionString(string connectionString)
    {
        // Extract org URL from connection string
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.Trim().StartsWith("Url=", StringComparison.OrdinalIgnoreCase))
            {
                var url = part.Substring(4).Trim();
                var uri = new Uri(url);
                return uri.Host;
            }
        }
        return "unknown";
    }

    private static void WritePassFail(bool passed)
    {
        if (passed)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("✗");
        }
        Console.ResetColor();
    }

    private static async Task<int> RunCliAsync(string arguments, bool verbose = false)
    {
        if (verbose)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"\n    > ppds-migrate {RedactSecrets(arguments)}");
            Console.ResetColor();
        }

        var psi = new ProcessStartInfo
        {
            FileName = CliPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return 1;

        // Always drain streams to prevent deadlock
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;

        if (verbose && !string.IsNullOrEmpty(output))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            foreach (var line in output.Split('\n').Take(20))
            {
                Console.WriteLine($"    {line}");
            }
            if (output.Split('\n').Length > 20)
            {
                Console.WriteLine($"    ... ({output.Split('\n').Length - 20} more lines)");
            }
            Console.ResetColor();
        }

        if (!string.IsNullOrEmpty(error) && process.ExitCode != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n    CLI Error: {error}");
            Console.ResetColor();
        }

        return process.ExitCode;
    }

    private static async Task<TestData> QueryTestData(ServiceClient client)
    {
        var result = new TestData();

        // Query accounts with parent reference
        var accountQuery = new QueryExpression("account")
        {
            ColumnSet = new ColumnSet("accountid", "name", "parentaccountid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.BeginsWith, "PPDS-")
                }
            }
        };
        var accounts = await client.RetrieveMultipleAsync(accountQuery);
        result.Accounts = accounts.Entities.Select(e => new AccountInfo
        {
            Id = e.Id,
            Name = e.GetAttributeValue<string>("name"),
            ParentAccountId = e.GetAttributeValue<EntityReference>("parentaccountid")?.Id
        }).ToList();

        // Query contacts with company reference
        var contactQuery = new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet("contactid", "fullname", "parentcustomerid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("contactid", ConditionOperator.In,
                        SampleData.GetContacts().Select(c => c.Id).Cast<object>().ToArray())
                }
            }
        };
        var contacts = await client.RetrieveMultipleAsync(contactQuery);
        result.Contacts = contacts.Entities.Select(e => new ContactInfo
        {
            Id = e.Id,
            FullName = e.GetAttributeValue<string>("fullname"),
            ParentCustomerId = e.GetAttributeValue<EntityReference>("parentcustomerid")?.Id
        }).ToList();

        return result;
    }

    private static void PrintDataSummary(string prefix, TestData data)
    {
        Console.WriteLine($"{prefix}: {data.Accounts.Count} accounts, {data.Contacts.Count} contacts");
        var withParent = data.Accounts.Count(a => a.ParentAccountId.HasValue);
        var withCompany = data.Contacts.Count(c => c.ParentCustomerId.HasValue);
        Console.WriteLine($"{prefix}: {withParent} with parent, {withCompany} with company");
    }

    private static void InspectExportedData(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        var dataEntry = archive.GetEntry("data.xml");
        if (dataEntry == null)
        {
            Console.WriteLine("    No data.xml found");
            return;
        }

        using var stream = dataEntry.Open();
        var doc = XDocument.Load(stream);

        foreach (var entity in doc.Descendants("entity"))
        {
            var entityName = entity.Attribute("name")?.Value ?? "unknown";
            var records = entity.Descendants("record").Count();
            var m2m = entity.Element("m2mrelationships")?.Elements("m2mrelationship").Count() ?? 0;

            Console.Write($"    {entityName}: {records} records");
            if (m2m > 0)
            {
                Console.Write($", {m2m} M2M associations");
            }
            Console.WriteLine();
        }
    }

    private class TestData
    {
        public List<AccountInfo> Accounts { get; set; } = [];
        public List<ContactInfo> Contacts { get; set; } = [];
    }

    private class AccountInfo
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public Guid? ParentAccountId { get; set; }
    }

    private class ContactInfo
    {
        public Guid Id { get; set; }
        public string? FullName { get; set; }
        public Guid? ParentCustomerId { get; set; }
    }

    /// <summary>
    /// Redacts sensitive values from CLI arguments for safe logging.
    /// </summary>
    private static string RedactSecrets(string arguments)
    {
        // Redact ClientSecret, Password, and similar sensitive values in connection strings
        var result = Regex.Replace(
            arguments,
            @"(ClientSecret|Password|Secret|Key)=([^;""]+)",
            "$1=***REDACTED***",
            RegexOptions.IgnoreCase);

        return result;
    }
}
