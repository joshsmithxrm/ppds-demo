using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using PPDS.Dataverse.Demo.Models;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Removes sample accounts and contacts from Dataverse.
/// Supports targeting specific environments for cross-env migration testing.
/// </summary>
public static class CleanCommand
{
    public static Command Create()
    {
        var forceOption = new Option<bool>(
            aliases: ["--force", "-f"],
            description: "Skip confirmation prompt");

        var envOption = new Option<string?>(
            aliases: ["--environment", "--env", "-e"],
            description: "Target environment name (e.g., 'Dev', 'QA'). Defaults to Dev.");

        var command = new Command("clean", "Remove sample accounts and contacts from Dataverse")
        {
            forceOption,
            envOption
        };

        command.SetHandler(async (bool force, string? environment) =>
        {
            Environment.ExitCode = await ExecuteAsync(force, environment);
        }, forceOption, envOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(bool force, string? environment = null)
    {
        Console.WriteLine("Cleaning Sample Data");
        Console.WriteLine("====================");
        Console.WriteLine();

        using var host = CommandBase.CreateHost([]);
        var config = host.Services.GetRequiredService<IConfiguration>();

        // Resolve connection string based on environment parameter
        var (connectionString, envName) = CommandBase.ResolveEnvironmentByNameOrIndex(config, environment);
        if (string.IsNullOrEmpty(connectionString))
        {
            CommandBase.WriteError($"Connection not found for environment: {environment ?? "Dev"}. Configure Environments:{environment ?? "Dev"}:ConnectionString in user-secrets.");
            return 1;
        }

        Console.WriteLine($"  Target: {envName}");
        Console.WriteLine();

        var contactIds = SampleData.GetContactIds();
        var accountIds = SampleData.GetAccountIds();

        Console.WriteLine($"Records to delete:");
        Console.WriteLine($"  Contacts: {contactIds.Count}");
        Console.WriteLine($"  Accounts: {accountIds.Count}");
        Console.WriteLine();

        if (!force)
        {
            Console.Write($"Delete these records from {envName}? (y/N): ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("Cancelled.");
                return 0;
            }
            Console.WriteLine();
        }

        try
        {
            using var client = new ServiceClient(connectionString);
            if (!client.IsReady)
            {
                CommandBase.WriteError($"Failed to connect: {client.LastError}");
                return 1;
            }

            // Delete contacts first (they reference accounts)
            Console.Write("Deleting contacts... ");
            var (contactSuccess, contactFail) = await DeleteMultipleAsync(client, "contact", contactIds);
            PrintDeleteResult(contactSuccess, contactFail, contactIds.Count);

            // Delete accounts
            Console.Write("Deleting accounts... ");
            var (accountSuccess, accountFail) = await DeleteMultipleAsync(client, "account", accountIds);
            PrintDeleteResult(accountSuccess, accountFail, accountIds.Count);

            Console.WriteLine();

            var totalDeleted = contactSuccess + accountSuccess;
            CommandBase.WriteSuccess($"Cleanup complete. {totalDeleted} records deleted from {envName}.");
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

    private static async Task<(int success, int failure)> DeleteMultipleAsync(
        ServiceClient client,
        string entityName,
        List<Guid> ids)
    {
        if (ids.Count == 0)
            return (0, 0);

        var request = new ExecuteMultipleRequest
        {
            Settings = new ExecuteMultipleSettings
            {
                ContinueOnError = true,
                ReturnResponses = true
            },
            Requests = new OrganizationRequestCollection()
        };

        foreach (var id in ids)
        {
            request.Requests.Add(new DeleteRequest
            {
                Target = new EntityReference(entityName, id)
            });
        }

        var response = (ExecuteMultipleResponse)await client.ExecuteAsync(request);

        var success = 0;
        var failure = 0;

        foreach (var item in response.Responses)
        {
            if (item.Fault == null)
                success++;
            else
                failure++;
        }

        return (success, failure);
    }

    private static void PrintDeleteResult(int success, int failure, int total)
    {
        if (failure == 0 && success > 0)
        {
            CommandBase.WriteSuccess($"Done ({success} deleted)");
        }
        else if (success == 0 && failure == total)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Skipped (records not found)");
            Console.ResetColor();
        }
        else
        {
            CommandBase.WriteError($"Partial ({success} deleted, {failure} failed)");
        }
    }
}
