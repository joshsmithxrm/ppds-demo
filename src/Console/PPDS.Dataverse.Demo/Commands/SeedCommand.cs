using System.CommandLine;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Dataverse.Demo.Models;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Seeds sample accounts and contacts into Dataverse.
/// Uses delete-then-create to ensure deterministic GUIDs are preserved.
/// </summary>
/// <remarks>
/// UpsertMultiple ignores provided GUIDs when creating new records - it always
/// generates new IDs. To ensure our deterministic GUIDs are used (required for
/// cross-environment migration testing), we delete existing records first then
/// use CreateMultiple which preserves the provided IDs.
/// </remarks>
public static class SeedCommand
{
    public static Command Create()
    {
        var command = new Command("seed", "Create sample accounts and contacts in Dataverse");

        // Use standardized options from GlobalOptionsExtensions
        var envOption = GlobalOptionsExtensions.CreateEnvironmentOption();
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        command.AddOption(envOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (string? environment, bool verbose, bool debug) =>
        {
            var options = new GlobalOptions
            {
                Environment = environment,
                Verbose = verbose,
                Debug = debug
            };
            Environment.ExitCode = await ExecuteAsync(options);
        }, envOption, verboseOption, debugOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(GlobalOptions options)
    {
        ConsoleWriter.Header("Seeding Sample Data");

        using var host = HostFactory.CreateHostForMigration(options);
        var pool = HostFactory.GetConnectionPool(host, options.Environment);

        if (pool == null)
        {
            ConsoleWriter.Error("Connection pool not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        Console.WriteLine($"  Environment: {options.Environment ?? "Dev (default)"}");
        Console.WriteLine();

        try
        {
            await using var client = await pool.GetClientAsync();

            var accounts = SampleData.GetAccounts();
            var accountParentUpdates = SampleData.GetAccountParentUpdates();
            var contacts = SampleData.GetContacts();

            Console.WriteLine($"Sample data to seed:");
            Console.WriteLine($"  Accounts: {accounts.Count} (+ {accountParentUpdates.Count} parent updates)");
            Console.WriteLine($"  Contacts: {contacts.Count}");
            Console.WriteLine();

            // Phase 1: Delete existing records by querying name/email prefix
            Console.Write("Cleaning existing records... ");

            // Delete contacts first (query by email domain since fullname has no prefix)
            var contactQuery = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid"),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.EndsWith, ".example.com") }
                }
            };
            var existingContacts = (await client.RetrieveMultipleAsync(contactQuery))
                .Entities.Select(e => e.Id).ToList();

            if (existingContacts.Count > 0)
            {
                await DeleteMultipleAsync(client, "contact", existingContacts);
            }

            // Delete accounts (query by name prefix)
            var accountQuery = new QueryExpression("account")
            {
                ColumnSet = new ColumnSet("accountid"),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression("name", ConditionOperator.BeginsWith, SampleData.Prefix) }
                }
            };
            var existingAccounts = (await client.RetrieveMultipleAsync(accountQuery))
                .Entities.Select(e => e.Id).ToList();

            if (existingAccounts.Count > 0)
            {
                await DeleteMultipleAsync(client, "account", existingAccounts);
            }

            ConsoleWriter.Success($"Done ({existingContacts.Count} contacts, {existingAccounts.Count} accounts)");

            // Phase 2: Create accounts with deterministic GUIDs
            Console.Write("Creating accounts... ");
            var accountResult = await CreateMultipleAsync(client, "account", accounts);
            if (accountResult.success == accounts.Count)
            {
                ConsoleWriter.Success($"Done ({accountResult.success} created)");
            }
            else
            {
                ConsoleWriter.Error($"Partial ({accountResult.success} created, {accountResult.failure} failed)");
                return 1;
            }

            // Phase 3: Update parent relationships
            if (accountParentUpdates.Count > 0)
            {
                Console.Write("Setting parent accounts... ");
                var parentResult = await UpdateMultipleAsync(client, "account", accountParentUpdates);
                if (parentResult.failure == 0)
                {
                    ConsoleWriter.Success($"Done ({parentResult.success} updated)");
                }
                else
                {
                    ConsoleWriter.Error($"Partial ({parentResult.success} updated, {parentResult.failure} failed)");
                }
            }

            // Phase 4: Create contacts with deterministic GUIDs
            Console.Write("Creating contacts... ");
            var contactResult = await CreateMultipleAsync(client, "contact", contacts);
            if (contactResult.success == contacts.Count)
            {
                ConsoleWriter.Success($"Done ({contactResult.success} created)");
            }
            else
            {
                ConsoleWriter.Error($"Partial ({contactResult.success} created, {contactResult.failure} failed)");
            }

            Console.WriteLine();
            var totalSuccess = accountResult.success + contactResult.success;
            ConsoleWriter.Success($"Successfully seeded {totalSuccess} records.");

            Console.WriteLine();
            Console.WriteLine("Next steps:");
            Console.WriteLine("  1. View data in Power Apps (make.powerapps.com)");
            Console.WriteLine("  2. Export with: ppds-migrate export --env Dev --output data.zip");
            Console.WriteLine("  3. Clean up with: dotnet run -- clean");
            Console.WriteLine();

            return 0;
        }
        catch (Exception ex)
        {
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }

    private static async Task DeleteMultipleAsync(IPooledClient client, string entityName, List<Guid> ids)
    {
        var request = new ExecuteMultipleRequest
        {
            Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = false },
            Requests = new OrganizationRequestCollection()
        };

        foreach (var id in ids)
        {
            request.Requests.Add(new DeleteRequest { Target = new EntityReference(entityName, id) });
        }

        await client.ExecuteAsync(request);
    }

    private static async Task<(int success, int failure)> CreateMultipleAsync(
        IPooledClient client, string entityName, List<Entity> entities)
    {
        var targets = new EntityCollection(entities) { EntityName = entityName };
        var request = new CreateMultipleRequest { Targets = targets };

        try
        {
            var response = (CreateMultipleResponse)await client.ExecuteAsync(request);
            return (response.Ids.Length, entities.Count - response.Ids.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n    Error: {ex.Message}");
            return (0, entities.Count);
        }
    }

    private static async Task<(int success, int failure)> UpdateMultipleAsync(
        IPooledClient client, string entityName, List<Entity> entities)
    {
        var targets = new EntityCollection(entities) { EntityName = entityName };
        var request = new UpdateMultipleRequest { Targets = targets };

        try
        {
            await client.ExecuteAsync(request);
            return (entities.Count, 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n    Error: {ex.Message}");
            return (0, entities.Count);
        }
    }
}
