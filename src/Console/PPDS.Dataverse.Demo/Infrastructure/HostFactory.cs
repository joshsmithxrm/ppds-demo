using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PPDS.Dataverse.DependencyInjection;
using PPDS.Dataverse.Pooling;
using PPDS.Migration.DependencyInjection;

namespace PPDS.Dataverse.Demo.Infrastructure;

/// <summary>
/// Factory for creating configured hosts for Dataverse operations.
///
/// Centralizes host creation with proper logging configuration based on GlobalOptions.
/// Eliminates the need for each command to configure logging and services.
/// </summary>
public static class HostFactory
{
    /// <summary>
    /// Creates a host with Dataverse connection pool configured.
    /// Basic host for simple operations (whoami, queries, etc.).
    /// </summary>
    /// <param name="options">Global options (environment, logging, etc.).</param>
    public static IHost CreateHost(GlobalOptions options)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => ConfigureLogging(logging, options))
            .ConfigureServices((context, services) =>
            {
                services.AddDataverseConnectionPool(context.Configuration, environment: options.Environment);
            })
            .Build();
    }

    /// <summary>
    /// Creates a host configured for migration operations.
    /// Includes connection pool, schema generator, exporter, and importer services.
    /// </summary>
    /// <param name="options">Global options (environment, logging, etc.).</param>
    public static IHost CreateHostForMigration(GlobalOptions options)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => ConfigureLogging(logging, options))
            .ConfigureServices((context, services) =>
            {
                services.AddDataverseConnectionPool(context.Configuration, environment: options.Environment);
                services.AddDataverseMigration();
            })
            .Build();
    }

    /// <summary>
    /// Creates a host configured for bulk operations with optimal settings.
    /// Includes bulk operation executor, adaptive rate control, and progress reporting.
    /// </summary>
    /// <param name="options">Global options (environment, logging, parallelism, rate control).</param>
    public static IHost CreateHostForBulkOperations(GlobalOptions options)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => ConfigureLogging(logging, options))
            .ConfigureServices((context, services) =>
            {
                services.AddDataverseConnectionPool(context.Configuration, environment: options.Environment);

                services.Configure<DataverseOptions>(opts =>
                {
                    opts.Pool.DisableAffinityCookie = true;

                    if (options.Parallelism.HasValue)
                    {
                        opts.BulkOperations.MaxParallelBatches = options.Parallelism.Value;
                    }

                    if (options.RatePreset.HasValue)
                    {
                        opts.AdaptiveRate.Preset = options.RatePreset.Value;
                    }
                });
            })
            .Build();
    }

    /// <summary>
    /// Configures logging based on global options.
    /// --debug: LogLevel.Debug (diagnostic details)
    /// --verbose: LogLevel.Information (operational messages)
    /// Default: LogLevel.Warning (errors and warnings only)
    /// </summary>
    private static void ConfigureLogging(ILoggingBuilder logging, GlobalOptions options)
    {
        var level = options.Debug ? LogLevel.Debug
            : options.Verbose ? LogLevel.Information
            : LogLevel.Warning;

        logging.AddFilter("PPDS.Dataverse", level);
        logging.AddFilter("PPDS.Migration", level);

        if (options.EffectiveVerbose)
        {
            logging.AddSimpleConsole(consoleOptions =>
            {
                consoleOptions.SingleLine = true;
                consoleOptions.TimestampFormat = "HH:mm:ss.fff ";
            });
        }
    }

    /// <summary>
    /// Validates the connection pool is enabled and returns it.
    /// Returns null and prints setup instructions if not configured.
    /// </summary>
    public static IDataverseConnectionPool? GetConnectionPool(IHost host, string? environment = null)
    {
        var pool = host.Services.GetRequiredService<IDataverseConnectionPool>();

        if (!pool.IsEnabled)
        {
            ConsoleWriter.Error("Connection pool is not enabled.");
            ConsoleWriter.ConnectionSetupInstructions(environment);
            return null;
        }

        return pool;
    }

    /// <summary>
    /// Gets the default environment name from configuration.
    /// </summary>
    public static string GetDefaultEnvironment(IConfiguration config)
    {
        return config["Dataverse:DefaultEnvironment"] ?? "Dev";
    }

    /// <summary>
    /// Resolves the effective environment name.
    /// If options.Environment is set, uses that.
    /// Otherwise, reads DefaultEnvironment from configuration.
    /// </summary>
    public static string ResolveEnvironment(IHost host, GlobalOptions options)
    {
        if (options.Environment != null) return options.Environment;
        var config = host.Services.GetRequiredService<IConfiguration>();
        return GetDefaultEnvironment(config);
    }

    /// <summary>
    /// Gets the environment URL from configuration.
    /// </summary>
    public static string? GetEnvironmentUrl(IConfiguration config, string environment)
    {
        return config[$"Dataverse:Environments:{environment}:Url"];
    }
}
