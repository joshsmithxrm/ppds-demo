using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace PPDSDemo.Functions;

/// <summary>
/// Service Bus trigger function that processes account update messages from Dataverse.
/// </summary>
public class ServiceBusProcessor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServiceBusProcessor> _logger;

    public ServiceBusProcessor(IHttpClientFactory httpClientFactory, ILogger<ServiceBusProcessor> logger)
    {
        _httpClient = httpClientFactory.CreateClient("WebApi");
        _logger = logger;
    }

    /// <summary>
    /// Processes account update messages from Service Bus queue.
    /// Messages are sent by Dataverse service endpoint configured for Service Bus.
    /// </summary>
    [Function("AccountUpdatedProcessor")]
    public async Task Run(
        [ServiceBusTrigger("account-updates", Connection = "ServiceBusConnection")] string message)
    {
        _logger.LogInformation("Processing Service Bus message");

        try
        {
            if (string.IsNullOrEmpty(message))
            {
                _logger.LogWarning("Received empty Service Bus message");
                return;
            }

            _logger.LogDebug("Message content: {Message}", message);

            // Forward to Web API
            using var content = new StringContent(message, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/api/webhook/account-updated", content);

            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully processed message via Web API");
            }
            else
            {
                var statusCode = (int)response.StatusCode;
                _logger.LogWarning("Web API returned {StatusCode}: {Body}",
                    response.StatusCode, responseBody);

                // Only retry transient errors (5xx). Client errors (4xx) are permanent failures
                // that won't succeed on retry - complete the message to avoid infinite retry loops.
                if (statusCode >= 500)
                {
                    throw new InvalidOperationException($"Web API returned {response.StatusCode}: {responseBody}");
                }
                else
                {
                    // 4xx errors: Log as error (not warning) since data is being lost, but don't retry
                    _logger.LogError("Permanent failure processing message - Web API returned {StatusCode}. Message will not be retried. Body: {Body}",
                        response.StatusCode, responseBody);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Service Bus message");
            throw; // Re-throw to trigger Service Bus retry
        }
    }
}
