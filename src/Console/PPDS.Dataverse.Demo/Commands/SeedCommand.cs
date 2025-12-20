using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using PPDS.Dataverse.BulkOperations;
using PPDS.Dataverse.Demo.Models;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Seeds sample accounts and contacts into Dataverse.
/// Uses UpsertMultiple for idempotent seeding (safe to run multiple times).
/// </summary>
public static class SeedCommand
{
    public static Command Create()
    {
        var command = new Command("seed", "Create sample accounts and contacts in Dataverse");

        command.SetHandler(async () =>
        {
            Environment.ExitCode = await ExecuteAsync(Array.Empty<string>());
        });

        return command;
    }

    public static async Task<int> ExecuteAsync(string[] args)
    {
        Console.WriteLine("Seeding Sample Data");
        Console.WriteLine("===================");
        Console.WriteLine();

        using var host = CommandBase.CreateHost(args);
        var pool = CommandBase.GetConnectionPool(host);

        if (pool == null)
            return 1;

        var bulkExecutor = host.Services.GetRequiredService<IBulkOperationExecutor>();

        try
        {
            var accounts = SampleData.GetAccounts();
            var accountParentUpdates = SampleData.GetAccountParentUpdates();
            var contacts = SampleData.GetContacts();

            Console.WriteLine($"Sample data to seed:");
            Console.WriteLine($"  Accounts: {accounts.Count} (+ {accountParentUpdates.Count} parent updates)");
            Console.WriteLine($"  Contacts: {contacts.Count}");
            Console.WriteLine();

            var totalSuccess = 0;
            var totalFailure = 0;

            // Phase 1: Upsert accounts (without parent relationships)
            Console.Write("Upserting accounts... ");
            var accountResult = await bulkExecutor.UpsertMultipleAsync(
                "account",
                accounts,
                new BulkOperationOptions { ContinueOnError = true });

            if (accountResult.IsSuccess)
            {
                CommandBase.WriteSuccess($"Done ({accountResult.SuccessCount} succeeded)");
                totalSuccess += accountResult.SuccessCount;
            }
            else
            {
                CommandBase.WriteError($"Partial ({accountResult.SuccessCount} succeeded, {accountResult.FailureCount} failed)");
                totalSuccess += accountResult.SuccessCount;
                totalFailure += accountResult.FailureCount;
                foreach (var error in accountResult.Errors.Take(3))
                {
                    Console.WriteLine($"    - {error.Message}");
                }
            }

            // Phase 2: Update parent relationships (only if accounts succeeded)
            if (accountResult.SuccessCount > 0 && accountParentUpdates.Count > 0)
            {
                Console.Write("Setting parent accounts... ");
                var parentResult = await bulkExecutor.UpdateMultipleAsync(
                    "account",
                    accountParentUpdates,
                    new BulkOperationOptions { ContinueOnError = true });

                if (parentResult.IsSuccess)
                {
                    CommandBase.WriteSuccess($"Done ({parentResult.SuccessCount} updated)");
                }
                else
                {
                    CommandBase.WriteError($"Partial ({parentResult.SuccessCount} updated, {parentResult.FailureCount} failed)");
                    totalFailure += parentResult.FailureCount;
                }
            }

            // Phase 3: Upsert contacts (only if accounts succeeded)
            if (accountResult.SuccessCount > 0)
            {
                Console.Write("Upserting contacts... ");
                var contactResult = await bulkExecutor.UpsertMultipleAsync(
                    "contact",
                    contacts,
                    new BulkOperationOptions { ContinueOnError = true });

                if (contactResult.IsSuccess)
                {
                    CommandBase.WriteSuccess($"Done ({contactResult.SuccessCount} succeeded)");
                    totalSuccess += contactResult.SuccessCount;
                }
                else
                {
                    CommandBase.WriteError($"Partial ({contactResult.SuccessCount} succeeded, {contactResult.FailureCount} failed)");
                    totalSuccess += contactResult.SuccessCount;
                    totalFailure += contactResult.FailureCount;
                    foreach (var error in contactResult.Errors.Take(3))
                    {
                        Console.WriteLine($"    - {error.Message}");
                    }
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Skipping contacts (accounts failed)");
                Console.ResetColor();
                totalFailure += contacts.Count;
            }

            Console.WriteLine();

            if (totalFailure == 0)
            {
                CommandBase.WriteSuccess($"Successfully seeded {totalSuccess} records.");
            }
            else
            {
                CommandBase.WriteError($"Seeded {totalSuccess} records with {totalFailure} failures.");
            }

            Console.WriteLine();
            Console.WriteLine("Next steps:");
            Console.WriteLine("  1. View data in Power Apps (make.powerapps.com)");
            Console.WriteLine("  2. Export with: ppds-migrate export --schema schema.xml --output data.zip");
            Console.WriteLine("  3. Clean up with: dotnet run -- clean");
            Console.WriteLine();

            return totalFailure > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            CommandBase.WriteError($"Error: {ex.Message}");
            Console.WriteLine();
            return 1;
        }
    }
}
