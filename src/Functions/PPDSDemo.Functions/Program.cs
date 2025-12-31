using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PPDSDemo.Functions;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Get Web API configuration
        var webApiBaseUrl = context.Configuration["WebApiBaseUrl"]
            ?? throw new InvalidOperationException("WebApiBaseUrl configuration is required");

        var webApiAudience = context.Configuration["WebApiAudience"];

        // Configure HttpClient for Web API calls with managed identity authentication
        services.AddHttpClient("WebApi", client =>
        {
            client.BaseAddress = new Uri(webApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddHttpMessageHandler(sp =>
        {
            // If WebApiAudience is configured, use managed identity authentication
            // Otherwise, skip auth (for local development without Azure AD)
            if (!string.IsNullOrEmpty(webApiAudience))
            {
                var logger = sp.GetRequiredService<ILogger<ManagedIdentityAuthHandler>>();
                return new ManagedIdentityAuthHandler(webApiAudience, logger);
            }

            // No-op handler for local development without Azure AD
            return new NoOpDelegatingHandler();
        });
    })
    .Build();

host.Run();

/// <summary>
/// A no-op delegating handler for local development when Azure AD is not configured.
/// </summary>
internal class NoOpDelegatingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return base.SendAsync(request, cancellationToken);
    }
}
