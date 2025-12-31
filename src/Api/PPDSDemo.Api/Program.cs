using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using PPDS.Dataverse.DependencyInjection;
using PPDSDemo.Api.Authentication;
using PPDSDemo.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Dataverse connection pool
builder.Services.AddDataverseConnectionPool(builder.Configuration);

// Add services
builder.Services.AddSingleton<IProductService, ProductService>();
builder.Services.AddScoped<IAccountService, AccountService>();

// Configure dual authentication: API Key (plugins) + JWT Bearer (Azure Functions with Managed Identity)
// The default policy accepts either scheme.
var azureAdSection = builder.Configuration.GetSection("AzureAd");
var isAzureAdConfigured = !string.IsNullOrEmpty(azureAdSection["ClientId"]);

builder.Services.AddAuthentication(options =>
{
    // Default to API Key for backwards compatibility
    options.DefaultScheme = "ApiKey";
    options.DefaultChallengeScheme = "ApiKey";
})
.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>("ApiKey", null)
.AddJwtBearer("Bearer", options =>
{
    if (isAzureAdConfigured)
    {
        // Configure JWT Bearer using Azure AD settings
        options.Authority = $"https://login.microsoftonline.com/{azureAdSection["TenantId"]}/v2.0";
        options.Audience = azureAdSection["ClientId"];
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidIssuer = $"https://login.microsoftonline.com/{azureAdSection["TenantId"]}/v2.0";
    }
    else
    {
        // Azure AD not configured - Bearer scheme will fail gracefully
        // API Key scheme will be used instead
        options.TokenValidationParameters.ValidateIssuer = false;
        options.TokenValidationParameters.ValidateAudience = false;
    }
});

// Configure authorization policy that accepts either authentication scheme
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ApiAccess", policy =>
    {
        policy.AddAuthenticationSchemes("ApiKey", "Bearer");
        policy.RequireAuthenticatedUser();
    });

// Add controllers
builder.Services.AddControllers();

// Add OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Log authentication configuration
var logger = app.Services.GetRequiredService<ILogger<Program>>();
if (isAzureAdConfigured)
{
    logger.LogInformation("Azure AD authentication configured for tenant {TenantId}", azureAdSection["TenantId"]);
}
else
{
    logger.LogWarning("Azure AD not configured - only API Key authentication available. Set AzureAd:ClientId and AzureAd:TenantId for Managed Identity support.");
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
