using Microsoft.Xrm.Sdk;
using PPDS.Dataverse.Pooling;
using PPDSDemo.Api.Infrastructure;
using PPDSDemo.Api.Models;

namespace PPDSDemo.Api.Services;

/// <summary>
/// Service for account-related operations using the Dataverse connection pool.
/// </summary>
public class AccountService : IAccountService
{
    private readonly IDataverseConnectionPool _pool;
    private readonly ILogger<AccountService> _logger;

    public AccountService(IDataverseConnectionPool pool, ILogger<AccountService> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    public async Task CreateProcessingNoteAsync(Guid accountId, string noteText)
    {
        _logger.LogInformation("Creating processing note for account {AccountId}", accountId);

        await using var client = await _pool.GetClientAsync();

        var note = new Entity("annotation")
        {
            ["subject"] = "Azure Processing Note",
            ["notetext"] = noteText,
            ["objectid"] = new EntityReference("account", accountId),
            ["objecttypecode"] = "account"
        };

        await client.CreateAsync(note);
        _logger.LogInformation("Created processing note for account {AccountId}", accountId);
    }

    public async Task UpdateLastAzureSyncAsync(Guid accountId)
    {
        _logger.LogInformation("Updating last Azure sync for account {AccountId}", accountId);

        await using var client = await _pool.GetClientAsync();

        var account = new Entity("account", accountId)
        {
            ["ppds_lastazuresync"] = DateTime.UtcNow
        };

        await client.UpdateAsync(account);
        _logger.LogInformation("Updated last Azure sync for account {AccountId}", accountId);
    }

    public async Task<ProcessAccountResponse> ProcessAccountAsync(ProcessAccountRequest request)
    {
        _logger.LogInformation("Processing account {AccountId} with action {Action}",
            request.AccountId, LogSanitizer.SanitizeShort(request.Action));

        try
        {
            await using var client = await _pool.GetClientAsync();

            // Retrieve the account
            var account = await client.RetrieveAsync("account", request.AccountId,
                new Microsoft.Xrm.Sdk.Query.ColumnSet("name", "telephone1", "emailaddress1", "ppds_lastazuresync"));

            var accountName = account.GetAttributeValue<string>("name") ?? "Unknown";
            _logger.LogInformation("Retrieved account '{AccountName}' for processing", accountName);

            switch (request.Action.ToLowerInvariant())
            {
                case "validate":
                    return await ValidateAccountAsync(client, account);

                case "enrich":
                    return await EnrichAccountAsync(client, account);

                case "sync":
                    return await SyncAccountAsync(client, account);

                default:
                    return new ProcessAccountResponse
                    {
                        Success = false,
                        Message = $"Unknown action: {request.Action}. Valid actions are: validate, enrich, sync."
                    };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing account {AccountId}", request.AccountId);
            return new ProcessAccountResponse
            {
                Success = false,
                Message = "An error occurred while processing the account. Please try again or contact support."
            };
        }
    }

    private async Task<ProcessAccountResponse> ValidateAccountAsync(IPooledClient client, Entity account)
    {
        var accountName = account.GetAttributeValue<string>("name");
        var phone = account.GetAttributeValue<string>("telephone1");
        var email = account.GetAttributeValue<string>("emailaddress1");

        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(accountName))
            issues.Add("Account name is missing");

        if (string.IsNullOrWhiteSpace(phone))
            issues.Add("Phone number is missing");

        if (string.IsNullOrWhiteSpace(email))
            issues.Add("Email address is missing");
        else if (!IsValidEmail(email))
            issues.Add("Email address format is invalid");

        if (issues.Count > 0)
        {
            return new ProcessAccountResponse
            {
                Success = false,
                Message = $"Validation failed: {string.Join("; ", issues)}"
            };
        }

        return new ProcessAccountResponse
        {
            Success = true,
            Message = $"Account '{accountName}' passed validation."
        };
    }

    private async Task<ProcessAccountResponse> EnrichAccountAsync(IPooledClient client, Entity account)
    {
        var accountName = account.GetAttributeValue<string>("name");

        // Simulate enrichment - in real scenario would call external APIs
        var note = new Entity("annotation")
        {
            ["subject"] = "Account Enrichment",
            ["notetext"] = $"Account enriched via Azure API at {DateTime.UtcNow:O}. " +
                          "In production, this would include data from external sources.",
            ["objectid"] = new EntityReference("account", account.Id),
            ["objecttypecode"] = "account"
        };

        await client.CreateAsync(note);

        return new ProcessAccountResponse
        {
            Success = true,
            Message = $"Account '{accountName}' enriched successfully."
        };
    }

    private async Task<ProcessAccountResponse> SyncAccountAsync(IPooledClient client, Entity account)
    {
        var accountName = account.GetAttributeValue<string>("name");

        // Update last sync timestamp
        var update = new Entity("account", account.Id)
        {
            ["ppds_lastazuresync"] = DateTime.UtcNow
        };

        await client.UpdateAsync(update);

        return new ProcessAccountResponse
        {
            Success = true,
            Message = $"Account '{accountName}' synchronized at {DateTime.UtcNow:O}."
        };
    }

    /// <summary>
    /// Validates email address format using .NET's MailAddress parser.
    /// </summary>
    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
