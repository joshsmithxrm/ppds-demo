using System.CommandLine;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PPDS.Dataverse.BulkOperations;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Dataverse.Demo.Models;
using PPDS.Dataverse.Pooling;
using PPDS.Migration.Export;
using PPDS.Migration.Formats;
using PPDS.Migration.Import;
using PPDS.Migration.Progress;
using PPDS.Migration.Schema;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// End-to-end test of ppds-migrate CLI.
/// Seeds data, exports, cleans, imports, and verifies relationships are restored.
///
/// This command provides a comprehensive round-trip test:
///   1. Seed test data with relationships (accounts, contacts)
///   2. Generate schema and export to ZIP
///   3. Delete the source data
///   4. Import from ZIP
///   5. Verify all records and relationships restored
///
/// Usage:
///   dotnet run -- test-migration
///   dotnet run -- test-migration --skip-seed --skip-clean
///   dotnet run -- test-migration --env QA --verbose
/// </summary>
public static class TestMigrationCommand
{
    private static readonly string SchemaPath = Path.Combine(AppContext.BaseDirectory, "test-schema.xml");
    private static readonly string DataPath = Path.Combine(AppContext.BaseDirectory, "test-export.zip");

    public static Command Create()
    {
        var command = new Command("test-migration", "End-to-end test of ppds-migrate export/import");

        var skipSeedOption = new Option<bool>("--skip-seed", "Skip seeding (use existing data)");
        var skipCleanOption = new Option<bool>("--skip-clean", "Skip cleaning after export");

        // Use standardized options from GlobalOptionsExtensions
        var envOption = GlobalOptionsExtensions.CreateEnvironmentOption();
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        command.AddOption(skipSeedOption);
        command.AddOption(skipCleanOption);
        command.AddOption(envOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (bool skipSeed, bool skipClean, string? environment, bool verbose, bool debug) =>
        {
            var options = new GlobalOptions
            {
                Environment = environment,
                Verbose = verbose,
                Debug = debug
            };
            Environment.ExitCode = await ExecuteAsync(skipSeed, skipClean, options);
        }, skipSeedOption, skipCleanOption, envOption, verboseOption, debugOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(
        bool skipSeed,
        bool skipClean,
        GlobalOptions options)
    {
        ConsoleWriter.Header("PPDS.Migration End-to-End Test");

        // Create host with migration services (uses library directly, no CLI)
        using var host = HostFactory.CreateHostForMigration(options);
        var pool = HostFactory.GetConnectionPool(host, options.Environment);
        if (pool == null) return 1;

        // Get migration services from DI
        var bulkExecutor = host.Services.GetRequiredService<IBulkOperationExecutor>();
        var schemaGenerator = host.Services.GetRequiredService<ISchemaGenerator>();
        var schemaWriter = host.Services.GetRequiredService<ICmtSchemaWriter>();
        var exporter = host.Services.GetRequiredService<IExporter>();
        var importer = host.Services.GetRequiredService<IImporter>();

        var envName = HostFactory.ResolveEnvironment(host, options);

        // Update options with resolved environment
        options = options with { Environment = envName };

        Console.WriteLine($"  Environment: {envName}");
        Console.WriteLine();

        try
        {
            // ===================================================================
            // PHASE 1: Seed test data with relationships
            // ===================================================================
            if (!skipSeed)
            {
                ConsoleWriter.Section("Phase 1: Seed Test Data");

                var accounts = SampleData.GetAccounts();
                var accountParentUpdates = SampleData.GetAccountParentUpdates();
                var contacts = SampleData.GetContacts();

                // Create accounts
                Console.Write("  Creating accounts... ");
                var accountResult = await bulkExecutor.UpsertMultipleAsync("account", accounts,
                    new BulkOperationOptions { ContinueOnError = true });
                if (accountResult.CreatedCount.HasValue && accountResult.UpdatedCount.HasValue)
                {
                    ConsoleWriter.Success($"{accountResult.SuccessCount} upserted ({accountResult.CreatedCount} created, {accountResult.UpdatedCount} updated)");
                }
                else
                {
                    ConsoleWriter.Success($"{accountResult.SuccessCount} upserted");
                }

                // Set parent relationships
                Console.Write("  Setting parent relationships... ");
                var parentResult = await bulkExecutor.UpdateMultipleAsync("account", accountParentUpdates,
                    new BulkOperationOptions { ContinueOnError = true });
                ConsoleWriter.Success($"{parentResult.SuccessCount} updated");

                // Create contacts
                Console.Write("  Creating contacts... ");
                var contactResult = await bulkExecutor.UpsertMultipleAsync("contact", contacts,
                    new BulkOperationOptions { ContinueOnError = true });
                if (contactResult.CreatedCount.HasValue && contactResult.UpdatedCount.HasValue)
                {
                    ConsoleWriter.Success($"{contactResult.SuccessCount} upserted ({contactResult.CreatedCount} created, {contactResult.UpdatedCount} updated)");
                }
                else
                {
                    ConsoleWriter.Success($"{contactResult.SuccessCount} upserted");
                }

                Console.WriteLine();
            }

            // Verify source data before export
            Console.WriteLine("  Verifying source data...");
            var sourceData = await QueryTestData(pool);
            PrintDataSummary("  Source", sourceData);
            Console.WriteLine();

            // ===================================================================
            // PHASE 2: Generate schema and export (using library directly)
            // ===================================================================
            ConsoleWriter.Section("Phase 2: Generate Schema & Export (PPDS.Migration)");

            // Generate schema
            Console.Write("  Generating schema... ");
            var schemaOptions = new SchemaGeneratorOptions
            {
                IncludeRelationships = true,
                IncludeAllFields = true
            };
            var schema = await schemaGenerator.GenerateAsync(
                new[] { "account", "contact" },
                schemaOptions,
                progress: null,
                CancellationToken.None);
            await schemaWriter.WriteAsync(schema, SchemaPath, CancellationToken.None);
            ConsoleWriter.Success("Done");

            // Export data
            Console.Write("  Exporting data... ");
            var exportOptions = new ExportOptions();
            var exportResult = await exporter.ExportAsync(
                schema,
                DataPath,
                exportOptions,
                progress: null,
                CancellationToken.None);
            if (!exportResult.Success)
            {
                ConsoleWriter.Error("Export failed");
                return 1;
            }
            ConsoleWriter.Success($"Done ({new FileInfo(DataPath).Length / 1024} KB)");

            // Inspect exported data
            Console.WriteLine("  Inspecting exported data...");
            InspectExportedData(DataPath);
            Console.WriteLine();

            // ===================================================================
            // PHASE 3: Clean data
            // ===================================================================
            if (!skipClean)
            {
                ConsoleWriter.Section("Phase 3: Clean Test Data");

                // Delete contacts first (foreign key constraint)
                var contactIds = SampleData.GetContacts().Select(c => c.Id).ToList();
                Console.Write($"  Deleting {contactIds.Count} contacts... ");
                var deleteContactResult = await bulkExecutor.DeleteMultipleAsync("contact", contactIds,
                    new BulkOperationOptions { ContinueOnError = true });
                ConsoleWriter.Success($"{deleteContactResult.SuccessCount} deleted");

                // Delete accounts
                var accountIds = SampleData.GetAccounts().Select(a => a.Id).ToList();
                Console.Write($"  Deleting {accountIds.Count} accounts... ");
                var deleteAccountResult = await bulkExecutor.DeleteMultipleAsync("account", accountIds,
                    new BulkOperationOptions { ContinueOnError = true });
                ConsoleWriter.Success($"{deleteAccountResult.SuccessCount} deleted");

                // Verify clean
                var cleanData = await QueryTestData(pool);
                Console.WriteLine($"  Verified: {cleanData.Accounts.Count} accounts, {cleanData.Contacts.Count} contacts remaining");
                Console.WriteLine();
            }

            // ===================================================================
            // PHASE 4: Import data (using library directly)
            // ===================================================================
            ConsoleWriter.Section("Phase 4: Import Data (PPDS.Migration)");

            Console.Write("  Importing data... ");
            var importOptions = new ImportOptions
            {
                Mode = ImportMode.Upsert,
                StripOwnerFields = true,
                ContinueOnError = true
            };
            var importResult = await importer.ImportAsync(
                DataPath,
                importOptions,
                progress: null,
                CancellationToken.None);
            if (!importResult.Success)
            {
                ConsoleWriter.Error("Import failed");
                return 1;
            }
            ConsoleWriter.Success($"Done ({importResult.RecordsImported} records)");
            Console.WriteLine();

            // ===================================================================
            // PHASE 5: Verify imported data
            // ===================================================================
            ConsoleWriter.Section("Phase 5: Verify Import");

            var importedData = await QueryTestData(pool);
            PrintDataSummary("  Imported", importedData);
            Console.WriteLine();

            // Compare
            var passed = true;
            Console.WriteLine("  Comparison:");

            // Account count
            var accountMatch = sourceData.Accounts.Count == importedData.Accounts.Count;
            Console.Write($"    Accounts: {sourceData.Accounts.Count} -> {importedData.Accounts.Count} ");
            ConsoleWriter.PassFail(accountMatch);
            passed &= accountMatch;

            // Contact count
            var contactMatch = sourceData.Contacts.Count == importedData.Contacts.Count;
            Console.Write($"    Contacts: {sourceData.Contacts.Count} -> {importedData.Contacts.Count} ");
            ConsoleWriter.PassFail(contactMatch);
            passed &= contactMatch;

            // Parent account relationships
            var sourceParents = sourceData.Accounts.Count(a => a.ParentAccountId.HasValue);
            var importedParents = importedData.Accounts.Count(a => a.ParentAccountId.HasValue);
            var parentMatch = sourceParents == importedParents;
            Console.Write($"    Parent Account refs: {sourceParents} -> {importedParents} ");
            ConsoleWriter.PassFail(parentMatch);
            passed &= parentMatch;

            // Contact company relationships
            var sourceCompany = sourceData.Contacts.Count(c => c.ParentCustomerId.HasValue);
            var importedCompany = importedData.Contacts.Count(c => c.ParentCustomerId.HasValue);
            var companyMatch = sourceCompany == importedCompany;
            Console.Write($"    Contact->Account refs: {sourceCompany} -> {importedCompany} ");
            ConsoleWriter.PassFail(companyMatch);
            passed &= companyMatch;

            Console.WriteLine();

            // ===================================================================
            // RESULT
            // ===================================================================
            if (passed)
            {
                ConsoleWriter.ResultBanner("TEST PASSED", success: true);
                return 0;
            }
            else
            {
                ConsoleWriter.ResultBanner("TEST FAILED", success: false);
                return 1;
            }
        }
        catch (Exception ex)
        {
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }

    private static async Task<TestData> QueryTestData(IDataverseConnectionPool pool)
    {
        var result = new TestData();

        await using var client = await pool.GetClientAsync();

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
        Console.WriteLine($"{prefix}: {withParent} accounts with parent, {withCompany} contacts with company");
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

    private static void InspectExportedData(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        // Check schema format
        var schemaEntry = archive.GetEntry("data_schema.xml");
        if (schemaEntry != null)
        {
            using var schemaStream = schemaEntry.Open();
            var schemaDoc = XDocument.Load(schemaStream);
            var root = schemaDoc.Root;

            Console.WriteLine("    Schema format:");
            var dateMode = root?.Attribute("dateMode")?.Value;
            Console.WriteLine($"      dateMode: {dateMode ?? "(missing)"}");

            var importOrder = root?.Element("entityImportOrder");
            if (importOrder != null)
            {
                var entities = importOrder.Elements("entityName").Select(e => e.Value).ToList();
                Console.WriteLine($"      entityImportOrder: {string.Join(", ", entities)}");
            }
            else
            {
                Console.WriteLine("      entityImportOrder: (missing)");
            }
        }

        // Check data format
        var dataEntry = archive.GetEntry("data.xml");
        if (dataEntry == null)
        {
            Console.WriteLine("    No data.xml found in archive");
            return;
        }

        using var stream = dataEntry.Open();
        var doc = XDocument.Load(stream);

        Console.WriteLine("    Data format:");

        // Check if field values are element content (CMT) or attributes
        var firstField = doc.Descendants("field").FirstOrDefault();
        if (firstField != null)
        {
            var hasValueAttr = firstField.Attribute("value") != null;
            var hasContent = !string.IsNullOrEmpty(firstField.Value);
            Console.WriteLine($"      Field format: {(hasContent ? "element content (CMT)" : hasValueAttr ? "attribute" : "unknown")}");

            // Show sample field
            var name = firstField.Attribute("name")?.Value;
            var value = hasContent ? firstField.Value : firstField.Attribute("value")?.Value;
            Console.WriteLine($"      Sample: <field name=\"{name}\">{(hasContent ? value : $" value=\"{value}\"")}");
        }

        // Check for lookup fields
        var lookupFields = new[] { "parentaccountid", "parentcustomerid", "primarycontactid" };

        foreach (var entity in doc.Descendants("entity"))
        {
            var entityName = entity.Attribute("name")?.Value ?? "unknown";
            var records = entity.Descendants("record").ToList();

            Console.WriteLine($"    {entityName}: {records.Count} records");

            foreach (var lookupField in lookupFields)
            {
                var withValue = records.Count(r =>
                {
                    var field = r.Elements("field")
                        .FirstOrDefault(f => f.Attribute("name")?.Value == lookupField);
                    if (field == null) return false;
                    // Check both element content and attribute for value
                    var value = !string.IsNullOrEmpty(field.Value) ? field.Value : field.Attribute("value")?.Value;
                    return !string.IsNullOrEmpty(value) && value != Guid.Empty.ToString();
                });

                if (withValue > 0)
                {
                    Console.WriteLine($"      {lookupField}: {withValue} with values");
                }
            }
        }
    }
}
