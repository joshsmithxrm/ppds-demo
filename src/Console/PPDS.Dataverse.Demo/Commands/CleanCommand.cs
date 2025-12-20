using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using PPDS.Dataverse.BulkOperations;
using PPDS.Dataverse.Demo.Models;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Removes sample accounts and contacts from Dataverse.
/// </summary>
public static class CleanCommand
{
    public static Command Create()
    {
        var forceOption = new Option<bool>(
            aliases: new[] { "--force", "-f" },
            description: "Skip confirmation prompt");

        var command = new Command("clean", "Remove sample accounts and contacts from Dataverse")
        {
            forceOption
        };

        command.SetHandler(async (bool force) =>
        {
            Environment.ExitCode = await ExecuteAsync(force);
        }, forceOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(bool force)
    {
        Console.WriteLine("Cleaning Sample Data");
        Console.WriteLine("====================");
        Console.WriteLine();

        var contactIds = SampleData.GetContactIds();
        var accountIds = SampleData.GetAccountIds();

        Console.WriteLine($"Records to delete:");
        Console.WriteLine($"  Contacts: {contactIds.Count}");
        Console.WriteLine($"  Accounts: {accountIds.Count}");
        Console.WriteLine();

        if (!force)
        {
            Console.Write("Are you sure you want to delete these records? (y/N): ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("Cancelled.");
                return 0;
            }
            Console.WriteLine();
        }

        using var host = CommandBase.CreateHost(Array.Empty<string>());
        var pool = CommandBase.GetConnectionPool(host);

        if (pool == null)
            return 1;

        var bulkExecutor = host.Services.GetRequiredService<IBulkOperationExecutor>();

        try
        {
            // Delete contacts first (they reference accounts)
            Console.Write("Deleting contacts... ");
            var contactResult = await bulkExecutor.DeleteMultipleAsync(
                "contact",
                contactIds,
                new BulkOperationOptions { ContinueOnError = true });

            if (contactResult.IsSuccess)
            {
                CommandBase.WriteSuccess($"Done ({contactResult.SuccessCount} deleted)");
            }
            else if (contactResult.SuccessCount == 0 && contactResult.FailureCount == contactIds.Count)
            {
                // All failed - likely already deleted
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Skipped (records not found)");
                Console.ResetColor();
            }
            else
            {
                CommandBase.WriteError($"Partial ({contactResult.SuccessCount} deleted, {contactResult.FailureCount} failed)");
            }

            // Delete accounts
            Console.Write("Deleting accounts... ");
            var accountResult = await bulkExecutor.DeleteMultipleAsync(
                "account",
                accountIds,
                new BulkOperationOptions { ContinueOnError = true });

            if (accountResult.IsSuccess)
            {
                CommandBase.WriteSuccess($"Done ({accountResult.SuccessCount} deleted)");
            }
            else if (accountResult.SuccessCount == 0 && accountResult.FailureCount == accountIds.Count)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Skipped (records not found)");
                Console.ResetColor();
            }
            else
            {
                CommandBase.WriteError($"Partial ({accountResult.SuccessCount} deleted, {accountResult.FailureCount} failed)");
            }

            Console.WriteLine();

            var totalDeleted = contactResult.SuccessCount + accountResult.SuccessCount;
            CommandBase.WriteSuccess($"Cleanup complete. {totalDeleted} records deleted.");
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
}
