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

// Configure authentication: API Key (always) + JWT Bearer (only when Azure AD configured)
var azureAdSection = builder.Configuration.GetSection("AzureAd");
var isAzureAdConfigured = !string.IsNullOrEmpty(azureAdSection["ClientId"])
    && !string.IsNullOrEmpty(azureAdSection["TenantId"]);

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "ApiKey";
    options.DefaultChallengeScheme = "ApiKey";
})
.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>("ApiKey", null);

// Only register JWT Bearer scheme when Azure AD is properly configured
if (isAzureAdConfigured)
{
    authBuilder.AddJwtBearer("Bearer", options =>
    {
        options.Authority = $"https://login.microsoftonline.com/{azureAdSection["TenantId"]}/v2.0";
        options.Audience = azureAdSection["ClientId"];
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidIssuer = $"https://login.microsoftonline.com/{azureAdSection["TenantId"]}/v2.0";
    });
}

// Configure authorization policy - include Bearer only if Azure AD is configured
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ApiAccess", policy =>
    {
        if (isAzureAdConfigured)
        {
            policy.AddAuthenticationSchemes("ApiKey", "Bearer");
        }
        else
        {
            policy.AddAuthenticationSchemes("ApiKey");
        }
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
