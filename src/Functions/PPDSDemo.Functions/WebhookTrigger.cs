using System.Net;
using System.Text;
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
        return await ForwardWebhookAsync(req, "/api/webhook/account-created", "account-created");
    }

    /// <summary>
    /// Receives webhook from Dataverse service endpoint for account updates.
    /// </summary>
    [Function("AccountUpdatedWebhook")]
    public async Task<HttpResponseData> AccountUpdated(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "webhook/account-updated")] HttpRequestData req)
    {
        return await ForwardWebhookAsync(req, "/api/webhook/account-updated", "account-updated");
    }

    /// <summary>
    /// Forwards a webhook request to the Web API.
    /// </summary>
    private async Task<HttpResponseData> ForwardWebhookAsync(
        HttpRequestData req,
        string apiEndpoint,
        string webhookType)
    {
        _logger.LogInformation("Received {WebhookType} webhook", webhookType);

        try
        {
            var body = await req.ReadAsStringAsync();

            if (string.IsNullOrEmpty(body))
            {
                _logger.LogWarning("Received empty webhook body for {WebhookType}", webhookType);
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Request body is required" });
                return badRequest;
            }

            _logger.LogDebug("Webhook body received: {BodyLength} chars", body.Length);

            // Forward to Web API
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var apiResponse = await _httpClient.PostAsync(apiEndpoint, content);
            var responseBody = await apiResponse.Content.ReadAsStringAsync();

            _logger.LogInformation("Web API response for {WebhookType}: {StatusCode}",
                webhookType, apiResponse.StatusCode);

            // Return the API response
            var response = req.CreateResponse(apiResponse.StatusCode);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(responseBody);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {WebhookType} webhook", webhookType);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Error processing webhook" });
            return errorResponse;
        }
    }
}
