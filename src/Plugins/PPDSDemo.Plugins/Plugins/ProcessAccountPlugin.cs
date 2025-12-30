using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using PPDS.Plugins;

namespace PPDSDemo.Plugins.Plugins
{
    /// <summary>
    /// Implements the ppds_ProcessAccount Custom API.
    /// Calls Azure Web API to process the account based on the specified action.
    /// </summary>
    /// <remarks>
    /// Registration:
    /// - Message: ppds_ProcessAccount
    /// - Stage: MainOperation (30)
    /// - Mode: Synchronous
    ///
    /// Secure Configuration Format: "apiKey|baseUrl"
    /// Example: "my-secret-key|https://api-ppds-demo.azurewebsites.net"
    /// </remarks>
    [PluginStep(
        Message = "ppds_ProcessAccount",
        Stage = (PluginStage)30,  // MainOperation for Custom API
        Mode = PluginMode.Synchronous)]
    public class ProcessAccountPlugin : PluginBase
    {
        // Static client cache to prevent socket exhaustion
        // HttpClient is designed to be reused across requests
        private static readonly ConcurrentDictionary<string, HttpClient> _httpClients =
            new ConcurrentDictionary<string, HttpClient>();

        private readonly string _apiKey;
        private readonly string _apiBaseUrl;

        /// <summary>
        /// Initializes a new instance of the ProcessAccountPlugin class.
        /// </summary>
        /// <param name="unsecureConfig">Unsecure configuration (not used).</param>
        /// <param name="secureConfig">Secure configuration in format "apiKey|baseUrl".</param>
        public ProcessAccountPlugin(string unsecureConfig, string secureConfig)
        {
            var parts = secureConfig?.Split('|') ?? Array.Empty<string>();
            _apiKey = parts.Length > 0 ? parts[0] : "";
            _apiBaseUrl = parts.Length > 1 ? parts[1] : "";
        }

        protected override void ExecutePlugin(LocalPluginContext context)
        {
            // Validate configuration
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiBaseUrl))
            {
                throw new InvalidPluginExecutionException(
                    "Plugin is not configured. Secure configuration must contain 'apiKey|baseUrl'.");
            }

            // Get input parameters
            if (!context.PluginExecutionContext.InputParameters.Contains("AccountId"))
            {
                throw new InvalidPluginExecutionException("AccountId input parameter is required.");
            }

            if (!context.PluginExecutionContext.InputParameters.Contains("Action"))
            {
                throw new InvalidPluginExecutionException("Action input parameter is required.");
            }

            var accountRef = context.PluginExecutionContext.InputParameters["AccountId"] as EntityReference;
            var action = context.PluginExecutionContext.InputParameters["Action"] as string;

            if (accountRef == null || string.IsNullOrEmpty(action))
            {
                throw new InvalidPluginExecutionException("AccountId and Action are required.");
            }

            context.Trace($"Processing account {accountRef.Id} with action: {action}");

            // Call Web API
            ProcessAccountResponse result;
            try
            {
                result = CallWebApi(accountRef.Id, action, context);
            }
            catch (Exception ex)
            {
                context.Trace($"API call failed: {ex.Message}");
                throw new InvalidPluginExecutionException($"Failed to process account: {ex.Message}", ex);
            }

            // Set output parameters
            context.PluginExecutionContext.OutputParameters["Success"] = result.Success;
            context.PluginExecutionContext.OutputParameters["Message"] = result.Message;

            context.Trace($"API response: Success={result.Success}, Message={result.Message}");
        }

        private HttpClient GetHttpClient()
        {
            return _httpClients.GetOrAdd(_apiBaseUrl, url =>
            {
                var client = new HttpClient
                {
                    BaseAddress = new Uri(url),
                    Timeout = TimeSpan.FromSeconds(30)
                };
                return client;
            });
        }

        private ProcessAccountResponse CallWebApi(Guid accountId, string action, LocalPluginContext context)
        {
            var client = GetHttpClient();

            var request = new ProcessAccountRequest
            {
                AccountId = accountId,
                Action = action
            };

            var json = JsonSerializer.Serialize(request);

            // Create request message to set per-request headers (API key may vary by config)
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/custom/process-account"))
            {
                requestMessage.Headers.Add("X-API-Key", _apiKey);
                requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");

                context.Trace($"Calling {_apiBaseUrl}/api/custom/process-account");

                var response = client.SendAsync(requestMessage).Result;
                var responseBody = response.Content.ReadAsStringAsync().Result;

                context.Trace($"Response: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"API returned {response.StatusCode}: {responseBody}");
                }

                var result = JsonSerializer.Deserialize<ProcessAccountResponse>(responseBody,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return result ?? new ProcessAccountResponse { Success = false, Message = "Empty response" };
            }
        }

        private class ProcessAccountRequest
        {
            public Guid AccountId { get; set; }
            public string Action { get; set; } = "";
        }

        private class ProcessAccountResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
        }
    }
}
