using Microsoft.AspNetCore.Authentication;
using PPDS.Dataverse.DependencyInjection;
using PPDSDemo.Api.Authentication;
using PPDSDemo.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Dataverse connection pool
builder.Services.AddDataverseConnectionPool(builder.Configuration);

// Add services
builder.Services.AddSingleton<IProductService, ProductService>();
builder.Services.AddScoped<IAccountService, AccountService>();

// Add API Key authentication
// Set "ApiKey" in configuration (User Secrets, environment variable, or Key Vault)
// If not set, authentication is bypassed (development mode)
builder.Services.AddAuthentication("ApiKey")
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>("ApiKey", null);

// Add controllers
builder.Services.AddControllers();

// Add OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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
