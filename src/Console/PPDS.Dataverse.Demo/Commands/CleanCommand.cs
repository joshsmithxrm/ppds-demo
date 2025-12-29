using System.CommandLine;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Dataverse.Demo.Models;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Removes sample accounts and contacts from Dataverse.
/// Supports targeting specific environments for cross-env migration testing.
/// </summary>
public static class CleanCommand
{
    public static Command Create()
    {
        var command = new Command("clean", "Remove sample accounts and contacts from Dataverse");

        var forceOption = new Option<bool>(
            aliases: ["--force", "-f"],
            description: "Skip confirmation prompt");

        // Use standardized options from GlobalOptionsExtensions
        var envOption = GlobalOptionsExtensions.CreateEnvironmentOption();
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        command.AddOption(forceOption);
        command.AddOption(envOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (bool force, string? environment, bool verbose, bool debug) =>
        {
            var options = new GlobalOptions
            {
                Environment = environment,
                Verbose = verbose,
                Debug = debug
            };
            Environment.ExitCode = await ExecuteAsync(force, options);
        }, forceOption, envOption, verboseOption, debugOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(bool force, GlobalOptions options)
    {
        ConsoleWriter.Header("Cleaning Sample Data");

        using var host = HostFactory.CreateHostForMigration(options);
        var pool = HostFactory.GetConnectionPool(host, options.Environment);

        if (pool == null)
        {
            ConsoleWriter.Error("Connection pool not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        Console.WriteLine($"  Environment: {options.Environment ?? "Dev (default)"}");
        Console.WriteLine();

        var contactIds = SampleData.GetContactIds();
        var accountIds = SampleData.GetAccountIds();

        Console.WriteLine($"Records to delete:");
        Console.WriteLine($"  Contacts: {contactIds.Count}");
        Console.WriteLine($"  Accounts: {accountIds.Count}");
        Console.WriteLine();

        if (!force)
        {
            Console.Write($"Delete these records from {options.Environment ?? "Dev"}? (y/N): ");
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
            await using var client = await pool.GetClientAsync();

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
            ConsoleWriter.Success($"Cleanup complete. {totalDeleted} records deleted.");
            Console.WriteLine();

            return 0;
        }
        catch (Exception ex)
        {
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }

    private static async Task<(int success, int failure)> DeleteMultipleAsync(
        IPooledClient client,
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
            ConsoleWriter.Success($"Done ({success} deleted)");
        }
        else if (success == 0 && failure == total)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Skipped (records not found)");
            Console.ResetColor();
        }
        else
        {
            ConsoleWriter.Error($"Partial ({success} deleted, {failure} failed)");
        }
    }
}
