using PPDSDemo.Api.Models;

namespace PPDSDemo.Api.Services;

/// <summary>
/// Service interface for account-related operations with Dataverse.
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Creates a note on the specified account recording Azure processing.
    /// </summary>
    Task CreateProcessingNoteAsync(Guid accountId, string noteText);

    /// <summary>
    /// Updates the ppds_lastazuresync field on the account.
    /// </summary>
    Task UpdateLastAzureSyncAsync(Guid accountId);

    /// <summary>
    /// Processes an account based on the specified action.
    /// </summary>
    Task<ProcessAccountResponse> ProcessAccountAsync(ProcessAccountRequest request);
}
