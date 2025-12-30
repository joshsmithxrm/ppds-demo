using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PPDSDemo.Api.Models;
using PPDSDemo.Api.Services;

namespace PPDSDemo.Api.Controllers;

/// <summary>
/// Handles Custom API calls from Dataverse plugins.
/// </summary>
[ApiController]
[Authorize]
[Route("api/custom")]
public class CustomApiController : ControllerBase
{
    private readonly IAccountService _accountService;
    private readonly ILogger<CustomApiController> _logger;

    public CustomApiController(IAccountService accountService, ILogger<CustomApiController> logger)
    {
        _accountService = accountService;
        _logger = logger;
    }

    /// <summary>
    /// Process an account with the specified action.
    /// Called by the ProcessAccountPlugin.
    /// </summary>
    /// <param name="request">The account ID and action to perform</param>
    /// <returns>Success status and message</returns>
    [HttpPost("process-account")]
    public async Task<ActionResult<ProcessAccountResponse>> ProcessAccount(
        [FromBody] ProcessAccountRequest request)
    {
        _logger.LogInformation("Processing account {AccountId} with action {Action}",
            request.AccountId, request.Action);

        if (request.AccountId == Guid.Empty)
        {
            return BadRequest(new ProcessAccountResponse
            {
                Success = false,
                Message = "AccountId is required"
            });
        }

        if (string.IsNullOrWhiteSpace(request.Action))
        {
            return BadRequest(new ProcessAccountResponse
            {
                Success = false,
                Message = "Action is required. Valid actions: validate, enrich, sync"
            });
        }

        var response = await _accountService.ProcessAccountAsync(request);

        _logger.LogInformation("Process account result: Success={Success}, Message={Message}",
            response.Success, response.Message);

        return Ok(response);
    }
}
