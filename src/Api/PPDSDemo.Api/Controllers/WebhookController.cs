using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PPDSDemo.Api.Models;
using PPDSDemo.Api.Services;

namespace PPDSDemo.Api.Controllers;

/// <summary>
/// Handles webhook callbacks from Dataverse via Azure Functions.
/// </summary>
[ApiController]
[Authorize]
[Route("api/webhook")]
public class WebhookController : ControllerBase
{
    private readonly IAccountService _accountService;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(IAccountService accountService, ILogger<WebhookController> logger)
    {
        _accountService = accountService;
        _logger = logger;
    }

    /// <summary>
    /// Called by Azure Function when an Account is created in Dataverse.
    /// Creates a Note on the Account documenting the webhook processing.
    /// </summary>
    [HttpPost("account-created")]
    public async Task<IActionResult> AccountCreated([FromBody] RemoteExecutionContext context)
    {
        _logger.LogInformation(
            "Received account-created webhook: Message={Message}, Entity={Entity}, Id={Id}",
            context.MessageName, context.PrimaryEntityName, context.PrimaryEntityId);

        if (context.PrimaryEntityId == Guid.Empty)
        {
            _logger.LogWarning("Account-created webhook received with empty PrimaryEntityId");
            return BadRequest(new { error = "PrimaryEntityId is required" });
        }

        try
        {
            var noteText = $"Account created webhook processed.\n" +
                          $"Message: {context.MessageName}\n" +
                          $"Stage: {context.Stage}\n" +
                          $"User: {context.UserId}\n" +
                          $"Processed at: {DateTime.UtcNow:O}";

            await _accountService.CreateProcessingNoteAsync(context.PrimaryEntityId, noteText);

            _logger.LogInformation("Successfully processed account-created webhook for {AccountId}",
                context.PrimaryEntityId);

            return Ok(new { success = true, message = "Webhook processed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing account-created webhook for {AccountId}",
                context.PrimaryEntityId);
            return StatusCode(500, new { error = "Error processing webhook" });
        }
    }

    /// <summary>
    /// Called by Azure Function when an Account is updated in Dataverse.
    /// Updates the ppds_lastazuresync field on the Account.
    /// </summary>
    [HttpPost("account-updated")]
    public async Task<IActionResult> AccountUpdated([FromBody] RemoteExecutionContext context)
    {
        _logger.LogInformation(
            "Received account-updated webhook: Message={Message}, Entity={Entity}, Id={Id}",
            context.MessageName, context.PrimaryEntityName, context.PrimaryEntityId);

        if (context.PrimaryEntityId == Guid.Empty)
        {
            _logger.LogWarning("Account-updated webhook received with empty PrimaryEntityId");
            return BadRequest(new { error = "PrimaryEntityId is required" });
        }

        try
        {
            await _accountService.UpdateLastAzureSyncAsync(context.PrimaryEntityId);

            _logger.LogInformation("Successfully processed account-updated webhook for {AccountId}",
                context.PrimaryEntityId);

            return Ok(new { success = true, message = "Account sync timestamp updated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing account-updated webhook for {AccountId}",
                context.PrimaryEntityId);
            return StatusCode(500, new { error = "Error processing webhook" });
        }
    }
}
