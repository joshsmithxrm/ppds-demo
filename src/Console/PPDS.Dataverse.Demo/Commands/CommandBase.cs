using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Base class for commands providing Dataverse connectivity via connection pool.
///
/// MIGRATION NOTE: This class delegates to the new Infrastructure layer.
/// New commands should use GlobalOptions and HostFactory directly.
/// These static methods remain for backward compatibility during the transition.
///
/// Configuration is read from appsettings.json with environment variable overrides:
/// - Dataverse:DefaultEnvironment - which environment to use when --env not specified
/// - Dataverse:Environments:{Name}:Url - environment URL
/// - Dataverse:Environments:{Name}:Connections:N:ClientId - app registration
/// - Dataverse:Environments:{Name}:Connections:N:ClientSecret - secret (or env var name)
/// </summary>
public abstract class CommandBase
{
    /// <summary>
    /// Creates a host with Dataverse connection pool configured.
    /// </summary>
    /// <param name="environment">Environment name (e.g., "Dev", "QA"). If null, uses DefaultEnvironment from config.</param>
    public static IHost CreateHost(string? environment = null)
    {
        return HostFactory.CreateHost(new GlobalOptions { Environment = environment });
    }

    /// <summary>
    /// Creates a host with Dataverse connection pool configured using GlobalOptions.
    /// This is the preferred overload for new code.
    /// </summary>
    public static IHost CreateHost(GlobalOptions options)
    {
        return HostFactory.CreateHost(options);
    }

    /// <summary>
    /// Creates a host configured for bulk operations with optional parallelism and logging.
    /// </summary>
    /// <param name="environment">Environment name. If null, uses DefaultEnvironment from config.</param>
    /// <param name="parallelism">Max parallel batches. If null, uses SDK default.</param>
    /// <param name="verbose">Enable info-level logging for operational messages (Connecting..., Processing...).</param>
    /// <param name="debug">Enable debug-level logging for diagnostic details (parallelism, ceiling, internal state).</param>
    public static IHost CreateHostForBulkOperations(
        string? environment = null,
        int? parallelism = null,
        bool verbose = false,
        bool debug = false)
    {
        return HostFactory.CreateHostForBulkOperations(new GlobalOptions
        {
            Environment = environment,
            Parallelism = parallelism,
            Verbose = verbose,
            Debug = debug
        });
    }

    /// <summary>
    /// Creates a host configured for bulk operations using GlobalOptions.
    /// This is the preferred overload for new code.
    /// </summary>
    public static IHost CreateHostForBulkOperations(GlobalOptions options)
    {
        return HostFactory.CreateHostForBulkOperations(options);
    }

    /// <summary>
    /// Validates the connection pool is enabled and returns it.
    /// Prints setup instructions if not configured.
    /// </summary>
    public static IDataverseConnectionPool? GetConnectionPool(IHost host)
    {
        return HostFactory.GetConnectionPool(host);
    }

    /// <summary>
    /// Gets the default environment name from configuration.
    /// </summary>
    public static string GetDefaultEnvironment(IConfiguration config)
    {
        return HostFactory.GetDefaultEnvironment(config);
    }

    /// <summary>
    /// Resolves the environment name, falling back to DefaultEnvironment from config.
    /// Use this for display purposes instead of showing "(default)".
    /// </summary>
    public static string ResolveEnvironment(IHost host, string? environment)
    {
        return HostFactory.ResolveEnvironment(host, new GlobalOptions { Environment = environment });
    }

    /// <summary>
    /// Resolves the environment name using GlobalOptions.
    /// This is the preferred overload for new code.
    /// </summary>
    public static string ResolveEnvironment(IHost host, GlobalOptions options)
    {
        return HostFactory.ResolveEnvironment(host, options);
    }

    /// <summary>
    /// Gets the environment URL from configuration.
    /// </summary>
    public static string? GetEnvironmentUrl(IConfiguration config, string environment)
    {
        return HostFactory.GetEnvironmentUrl(config, environment);
    }

    /// <summary>
    /// Writes a success message in green.
    /// </summary>
    public static void WriteSuccess(string message) => ConsoleWriter.Success(message);

    /// <summary>
    /// Writes an error message in red.
    /// </summary>
    public static void WriteError(string message) => ConsoleWriter.Error(message);

    /// <summary>
    /// Writes an info message in cyan.
    /// </summary>
    public static void WriteInfo(string message) => ConsoleWriter.Info(message);
}
