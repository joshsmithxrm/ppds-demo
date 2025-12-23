using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PPDS.Dataverse.DependencyInjection;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Base class for commands that need Dataverse connectivity.
/// </summary>
public abstract class CommandBase
{
    /// <summary>
    /// Creates and configures the host with Dataverse connection pool.
    /// </summary>
    public static IHost CreateHost(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddDataverseConnectionPool(context.Configuration);
            })
            .Build();
    }

    /// <summary>
    /// Creates a host configured for bulk operations using connections from Dataverse:Connections:* config.
    /// Supports multiple service principals for connection pooling and quota multiplying.
    /// </summary>
    /// <param name="config">The configuration instance containing Dataverse:Connections:* settings.</param>
    /// <param name="parallelism">Optional max parallel batches. If null, uses SDK default.</param>
    /// <param name="verbose">Enable debug-level logging for PPDS.Dataverse namespace.</param>
    public static IHost CreateHostForBulkOperations(IConfiguration config, int? parallelism = null, bool verbose = false)
    {
        return Host.CreateDefaultBuilder([])
            .ConfigureLogging(logging =>
            {
                if (verbose)
                {
                    // Enable debug logging for PPDS.Dataverse namespace
                    logging.AddFilter("PPDS.Dataverse", LogLevel.Debug);
                    logging.AddSimpleConsole(options =>
                    {
                        options.SingleLine = true;
                        options.TimestampFormat = "HH:mm:ss.fff ";
                    });
                }
            })
            .ConfigureServices((context, services) =>
            {
                // Read connections from Dataverse:Connections:* config section
                services.AddDataverseConnectionPool(config);

                // Apply overrides (SDK defaults to MaxPoolSize=50)
                services.Configure<DataverseOptions>(options =>
                {
                    options.Pool.DisableAffinityCookie = true;
                    if (parallelism.HasValue)
                    {
                        options.BulkOperations.MaxParallelBatches = parallelism.Value;
                    }
                });
            })
            .Build();
    }

    /// <summary>
    /// Validates the connection pool is enabled and returns it.
    /// </summary>
    public static IDataverseConnectionPool? GetConnectionPool(IHost host)
    {
        var pool = host.Services.GetRequiredService<IDataverseConnectionPool>();

        if (!pool.IsEnabled)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Connection pool is not enabled.");
            Console.WriteLine();
            Console.ResetColor();
            Console.WriteLine("Configure using .NET User Secrets:");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  dotnet user-secrets set \"Dataverse:Connections:0:Name\" \"Primary\"");
            Console.WriteLine("  dotnet user-secrets set \"Dataverse:Connections:0:ConnectionString\" \"AuthType=ClientSecret;Url=...\"");
            Console.ResetColor();
            Console.WriteLine();
            return null;
        }

        return pool;
    }

    /// <summary>
    /// Writes a success message in green.
    /// </summary>
    public static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes an error message in red.
    /// </summary>
    public static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes an info message in cyan.
    /// </summary>
    public static void WriteInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Resolves a connection string for a specific environment.
    /// Looks in Environments:{envName}:ConnectionString first, then falls back to pool config.
    /// </summary>
    /// <param name="config">The configuration instance.</param>
    /// <param name="envName">Environment name (e.g., "Dev", "QA"). If null, uses "Dev".</param>
    /// <returns>Tuple of (connectionString, displayName). ConnectionString is null if not found.</returns>
    public static (string? ConnectionString, string DisplayName) ResolveEnvironment(
        IConfiguration config,
        string? envName = null)
    {
        envName ??= "Dev";

        // Try Environments:{name}:ConnectionString first
        var connectionString = config[$"Environments:{envName}:ConnectionString"];
        var displayName = config[$"Environments:{envName}:Name"] ?? envName;

        if (!string.IsNullOrEmpty(connectionString))
        {
            return (connectionString, displayName);
        }

        // Fall back to Dataverse:Connections:0 for backwards compatibility
        // Only if envName is "Dev" or default
        if (envName.Equals("Dev", StringComparison.OrdinalIgnoreCase))
        {
            connectionString = config["Dataverse:Connections:0:ConnectionString"];
            displayName = config["Dataverse:Connections:0:Name"] ?? "Primary";

            if (!string.IsNullOrEmpty(connectionString))
            {
                return (connectionString, displayName);
            }
        }

        return (null, envName);
    }

    /// <summary>
    /// Resolves connection for environment by name or index.
    /// Supports: "Dev", "QA", "0", "1", etc.
    /// </summary>
    public static (string? ConnectionString, string DisplayName) ResolveEnvironmentByNameOrIndex(
        IConfiguration config,
        string? environment)
    {
        // Default to Dev
        if (string.IsNullOrEmpty(environment))
        {
            return ResolveEnvironment(config, "Dev");
        }

        // Try as environment name first (Dev, QA, Prod, etc.)
        var (connStr, name) = ResolveEnvironment(config, environment);
        if (!string.IsNullOrEmpty(connStr))
        {
            return (connStr, name);
        }

        // Try as index (for backwards compatibility)
        if (int.TryParse(environment, out var index))
        {
            connStr = config[$"Dataverse:Connections:{index}:ConnectionString"];
            name = config[$"Dataverse:Connections:{index}:Name"] ?? $"Connection {index}";
            return (connStr, name);
        }

        return (null, environment);
    }
}
