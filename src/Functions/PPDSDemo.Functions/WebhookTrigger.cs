using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace PPDSDemo.Functions;

/// <summary>
/// HTTP trigger function that receives Dataverse webhooks and forwards to Web API.
/// </summary>
public class WebhookTrigger
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookTrigger> _logger;

    public WebhookTrigger(IHttpClientFactory httpClientFactory, ILogger<WebhookTrigger> logger)
    {
        _httpClient = httpClientFactory.CreateClient("WebApi");
        _logger = logger;
    }

    /// <summary>
    /// Receives webhook from Dataverse service endpoint and forwards to Web API.
    /// </summary>
    [Function("AccountCreatedWebhook")]
    public async Task<HttpResponseData> AccountCreated(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "webhook/account-created")] HttpRequestData req)
    {
        _logger.LogInformation("Received account-created webhook");

        try
        {
            // Read the request body (RemoteExecutionContext from Dataverse)
            var body = await req.ReadAsStringAsync();

            if (string.IsNullOrEmpty(body))
            {
                _logger.LogWarning("Received empty webhook body");
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Request body is required" });
                return badRequest;
            }

            _logger.LogDebug("Webhook body: {Body}", body);

            // Forward to Web API
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var apiResponse = await _httpClient.PostAsync("/api/webhook/account-created", content);

            var responseBody = await apiResponse.Content.ReadAsStringAsync();

            _logger.LogInformation("Web API response: {StatusCode} - {Body}",
                apiResponse.StatusCode, responseBody);

            // Return the API response
            var response = req.CreateResponse(apiResponse.StatusCode);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(responseBody);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing account-created webhook");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Error processing webhook" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Receives webhook from Dataverse service endpoint for account updates.
    /// </summary>
    [Function("AccountUpdatedWebhook")]
    public async Task<HttpResponseData> AccountUpdated(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "webhook/account-updated")] HttpRequestData req)
    {
        _logger.LogInformation("Received account-updated webhook");

        try
        {
            var body = await req.ReadAsStringAsync();

            if (string.IsNullOrEmpty(body))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Request body is required" });
                return badRequest;
            }

            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var apiResponse = await _httpClient.PostAsync("/api/webhook/account-updated", content);

            var responseBody = await apiResponse.Content.ReadAsStringAsync();

            _logger.LogInformation("Web API response: {StatusCode}", apiResponse.StatusCode);

            var response = req.CreateResponse(apiResponse.StatusCode);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(responseBody);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing account-updated webhook");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Error processing webhook" });
            return errorResponse;
        }
    }
}
