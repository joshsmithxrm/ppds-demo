using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using PPDS.Dataverse.Demo.Models;

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

        command.SetHandler(async () =>
        {
            Environment.ExitCode = await ExecuteAsync([]);
        });

        return command;
    }

    public static async Task<int> ExecuteAsync(string[] args)
    {
        Console.WriteLine("Seeding Sample Data");
        Console.WriteLine("===================");
        Console.WriteLine();

        // Get Dev environment connection directly (avoid pool which may route to wrong env)
        using var host = CommandBase.CreateHost(args);
        var config = host.Services.GetRequiredService<IConfiguration>();
        var (connectionString, envName) = CommandBase.ResolveEnvironment(config, "Dev");

        if (string.IsNullOrEmpty(connectionString))
        {
            CommandBase.WriteError("Connection not found. Configure Environments:Dev:ConnectionString in user-secrets.");
            return 1;
        }

        Console.WriteLine($"  Target: {envName}");
        Console.WriteLine();

        try
        {
            using var client = new ServiceClient(connectionString);
            if (!client.IsReady)
            {
                CommandBase.WriteError($"Failed to connect: {client.LastError}");
                return 1;
            }

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

            CommandBase.WriteSuccess($"Done ({existingContacts.Count} contacts, {existingAccounts.Count} accounts)");

            // Phase 2: Create accounts with deterministic GUIDs
            Console.Write("Creating accounts... ");
            var accountResult = await CreateMultipleAsync(client, "account", accounts);
            if (accountResult.success == accounts.Count)
            {
                CommandBase.WriteSuccess($"Done ({accountResult.success} created)");
            }
            else
            {
                CommandBase.WriteError($"Partial ({accountResult.success} created, {accountResult.failure} failed)");
                return 1;
            }

            // Phase 3: Update parent relationships
            if (accountParentUpdates.Count > 0)
            {
                Console.Write("Setting parent accounts... ");
                var parentResult = await UpdateMultipleAsync(client, "account", accountParentUpdates);
                if (parentResult.failure == 0)
                {
                    CommandBase.WriteSuccess($"Done ({parentResult.success} updated)");
                }
                else
                {
                    CommandBase.WriteError($"Partial ({parentResult.success} updated, {parentResult.failure} failed)");
                }
            }

            // Phase 4: Create contacts with deterministic GUIDs
            Console.Write("Creating contacts... ");
            var contactResult = await CreateMultipleAsync(client, "contact", contacts);
            if (contactResult.success == contacts.Count)
            {
                CommandBase.WriteSuccess($"Done ({contactResult.success} created)");
            }
            else
            {
                CommandBase.WriteError($"Partial ({contactResult.success} created, {contactResult.failure} failed)");
            }

            Console.WriteLine();
            var totalSuccess = accountResult.success + contactResult.success;
            CommandBase.WriteSuccess($"Successfully seeded {totalSuccess} records.");

            Console.WriteLine();
            Console.WriteLine("Next steps:");
            Console.WriteLine("  1. View data in Power Apps (make.powerapps.com)");
            Console.WriteLine("  2. Export with: ppds-migrate export --schema schema.xml --output data.zip");
            Console.WriteLine("  3. Clean up with: dotnet run -- clean");
            Console.WriteLine();

            return 0;
        }
        catch (Exception ex)
        {
            CommandBase.WriteError($"Error: {ex.Message}");
            Console.WriteLine();
            return 1;
        }
    }

    private static async Task DeleteMultipleAsync(ServiceClient client, string entityName, List<Guid> ids)
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
        ServiceClient client, string entityName, List<Entity> entities)
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
        ServiceClient client, string entityName, List<Entity> entities)
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
