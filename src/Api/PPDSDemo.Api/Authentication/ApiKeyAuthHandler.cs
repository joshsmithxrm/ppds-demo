using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PPDSDemo.Api.Authentication;

/// <summary>
/// Simple API Key authentication handler for demo purposes.
/// For production, consider Azure AD authentication.
/// </summary>
public class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string ApiKeyHeaderName = "X-API-Key";

    public ApiKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check if API key header exists
        if (!Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("API Key header missing"));
        }

        var providedApiKey = apiKeyHeader.ToString();
        var configuredApiKey = Context.RequestServices
            .GetRequiredService<IConfiguration>()
            .GetValue<string>("ApiKey");

        // If no API key configured, allow all requests (development mode)
        if (string.IsNullOrEmpty(configuredApiKey))
        {
            Logger.LogWarning("No API key configured - authentication bypassed. Set 'ApiKey' in configuration for production.");
            var bypassClaims = new[] { new Claim(ClaimTypes.Name, "anonymous") };
            var bypassIdentity = new ClaimsIdentity(bypassClaims, Scheme.Name);
            var bypassPrincipal = new ClaimsPrincipal(bypassIdentity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(bypassPrincipal, Scheme.Name)));
        }

        if (!string.Equals(providedApiKey, configuredApiKey, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "api-client") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
