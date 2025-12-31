using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace PPDSDemo.Functions;

/// <summary>
/// HTTP message handler that authenticates requests using Azure Managed Identity.
/// </summary>
/// <remarks>
/// <para>
/// This handler acquires Azure AD tokens using <see cref="DefaultAzureCredential"/>, which
/// supports multiple authentication methods in order of precedence:
/// </para>
/// <list type="number">
///   <item>Environment variables (AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_CLIENT_SECRET)</item>
///   <item>Workload Identity (for Kubernetes)</item>
///   <item>Managed Identity (when running in Azure)</item>
///   <item>Azure CLI (for local development)</item>
///   <item>Azure PowerShell (for local development)</item>
///   <item>Visual Studio / VS Code credentials</item>
/// </list>
/// <para>
/// For local development, ensure you're logged in via <c>az login</c> or Visual Studio.
/// In Azure, the Function App's system-assigned or user-assigned managed identity is used.
/// </para>
/// <para>
/// The Web API must be registered in Azure AD and configured to accept tokens with the
/// specified audience. The Function App's managed identity needs the appropriate app role
/// or API permission to call the Web API.
/// </para>
/// </remarks>
public class ManagedIdentityAuthHandler : DelegatingHandler
{
    private readonly TokenCredential _credential;
    private readonly string[] _scopes;
    private readonly ILogger<ManagedIdentityAuthHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagedIdentityAuthHandler"/> class.
    /// </summary>
    /// <param name="audience">
    /// The Azure AD audience (application ID URI or client ID) of the target Web API.
    /// Example: "api://ppds-demo-api" or "00000000-0000-0000-0000-000000000000"
    /// </param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public ManagedIdentityAuthHandler(string audience, ILogger<ManagedIdentityAuthHandler> logger)
    {
        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            // Exclude interactive browser credential as this runs in automated context
            ExcludeInteractiveBrowserCredential = true
        });

        // The ".default" scope requests all statically configured permissions
        _scopes = new[] { $"{audience}/.default" };
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var tokenResult = await _credential.GetTokenAsync(
                new TokenRequestContext(_scopes),
                cancellationToken);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);

            _logger.LogDebug(
                "Acquired token for {Audience}, expires at {ExpiresOn}",
                _scopes[0],
                tokenResult.ExpiresOn);
        }
        catch (CredentialUnavailableException ex)
        {
            _logger.LogError(ex,
                "Failed to acquire token - no credential available. " +
                "Ensure Managed Identity is configured in Azure or run 'az login' locally.");
            throw;
        }
        catch (AuthenticationFailedException ex)
        {
            _logger.LogError(ex,
                "Authentication failed when acquiring token for {Audience}",
                _scopes[0]);
            throw;
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
