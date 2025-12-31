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
    /// <para><strong>Registration:</strong></para>
    /// <list type="bullet">
    ///   <item>Message: ppds_ProcessAccount</item>
    ///   <item>Stage: MainOperation (30)</item>
    ///   <item>Mode: Synchronous</item>
    /// </list>
    ///
    /// <para><strong>Secure Configuration Format:</strong> "apiKey|baseUrl"</para>
    /// <para><strong>Example:</strong> "my-secret-key|https://api-ppds-demo.azurewebsites.net"</para>
    ///
    /// <para><strong>HttpClient Pattern:</strong></para>
    /// <para>
    /// This plugin demonstrates the correct pattern for HTTP calls from Dataverse plugins.
    /// HttpClient instances are cached statically to prevent socket exhaustion - creating
    /// an HttpClient per request causes TCP connections to linger in TIME_WAIT state,
    /// exhausting available sockets under load.
    /// </para>
    ///
    /// <para><strong>Why static is safe here:</strong></para>
    /// <list type="bullet">
    ///   <item>HttpClient is thread-safe for concurrent requests</item>
    ///   <item>Plugin instance lifecycle is unpredictable; static guarantees reuse</item>
    ///   <item>Sandbox worker processes recycle periodically, clearing static state</item>
    ///   <item>Per-request headers (API key) are set on HttpRequestMessage, not the shared client</item>
    /// </list>
    ///
    /// <para><strong>DNS caching tradeoff:</strong></para>
    /// <para>
    /// HttpClient caches DNS indefinitely. If the API endpoint IP changes, the cached client
    /// won't pick it up until sandbox recycling. This is acceptable because sandbox workers
    /// recycle periodically (minutes to hours), and .NET Framework 4.6.2 (plugin target)
    /// lacks SocketsHttpHandler for fine-grained connection lifetime control.
    /// </para>
    ///
    /// <para><strong>Synchronous execution:</strong></para>
    /// <para>
    /// Plugins must be synchronous. Using .Result on async methods is safe here because
    /// the Dataverse sandbox has no SynchronizationContext - the deadlock scenarios that
    /// affect ASP.NET or UI contexts do not apply.
    /// </para>
    /// </remarks>
    [PluginStep(
        Message = "ppds_ProcessAccount",
        Stage = (PluginStage)30,  // MainOperation for Custom API
        Mode = PluginMode.Synchronous)]
    public class ProcessAccountPlugin : PluginBase
    {
        /// <summary>
        /// Static cache of HttpClient instances keyed by base URL.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This pattern prevents socket exhaustion while supporting multiple plugin registrations
        /// with different secure configurations (different API endpoints). Each unique base URL
        /// gets its own HttpClient instance, shared across all executions targeting that endpoint.
        /// </para>
        /// <para>
        /// Do NOT use instance fields for HttpClient - plugin instance pooling is unpredictable,
        /// and constructors run during registration discovery, not just execution.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Gets or creates a cached HttpClient for the configured base URL.
        /// </summary>
        /// <returns>A shared HttpClient instance for the API endpoint.</returns>
        /// <remarks>
        /// The 30-second timeout is well under the 2-minute plugin execution limit,
        /// leaving time for retry logic or graceful error handling if needed.
        /// </remarks>
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
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            {
                requestMessage.Headers.Add("X-API-Key", _apiKey);
                requestMessage.Content = content;

                context.Trace($"Calling {_apiBaseUrl}/api/custom/process-account");

                // Plugins must be synchronous - .Result is safe here (no SynchronizationContext in sandbox)
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
