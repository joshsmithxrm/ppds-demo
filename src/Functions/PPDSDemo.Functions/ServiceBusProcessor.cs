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
                _logger.LogWarning("Web API returned {StatusCode}: {Body}",
                    response.StatusCode, responseBody);

                // Throw to trigger retry/dead-letter based on Service Bus config
                throw new InvalidOperationException($"Web API returned {response.StatusCode}: {responseBody}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Service Bus message");
            throw; // Re-throw to trigger Service Bus retry
        }
    }
}
