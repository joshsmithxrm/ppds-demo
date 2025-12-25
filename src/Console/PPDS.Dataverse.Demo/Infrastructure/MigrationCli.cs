using System.Diagnostics;

namespace PPDS.Dataverse.Demo.Infrastructure;

/// <summary>
/// Implementation of IMigrationCli that invokes the ppds-migrate CLI.
///
/// This is the SINGLE place where CLI process management happens.
/// All commands delegate here, eliminating duplicated RunCliAsync methods.
///
/// Key responsibilities:
/// - CLI path resolution
/// - Process lifecycle management
/// - Output stream draining (prevents deadlocks)
/// - Logging of commands when verbose/debug enabled
/// - Consistent error handling
/// </summary>
public class MigrationCli : IMigrationCli
{
    private readonly string _cliPath;
    private readonly Action<string>? _commandLogger;
    private readonly Action<string>? _outputLogger;
    private readonly Action<string>? _errorLogger;

    /// <summary>
    /// Default CLI path relative to the demo app's output directory.
    /// </summary>
    public static readonly string DefaultCliPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..",
            "sdk", "src", "PPDS.Migration.Cli", "bin", "Debug", "net10.0", "ppds-migrate.exe"));

    /// <summary>
    /// Creates a new MigrationCli with default settings.
    /// </summary>
    public MigrationCli() : this(DefaultCliPath)
    {
    }

    /// <summary>
    /// Creates a new MigrationCli with a custom CLI path.
    /// </summary>
    /// <param name="cliPath">Path to ppds-migrate executable.</param>
    /// <param name="commandLogger">Optional callback for logging executed commands.</param>
    /// <param name="outputLogger">Optional callback for logging command output.</param>
    /// <param name="errorLogger">Optional callback for logging errors.</param>
    public MigrationCli(
        string cliPath,
        Action<string>? commandLogger = null,
        Action<string>? outputLogger = null,
        Action<string>? errorLogger = null)
    {
        _cliPath = cliPath;
        _commandLogger = commandLogger;
        _outputLogger = outputLogger;
        _errorLogger = errorLogger;
    }

    /// <summary>
    /// Creates a MigrationCli with console output logging.
    /// </summary>
    public static MigrationCli CreateWithConsoleLogging()
    {
        return new MigrationCli(
            DefaultCliPath,
            commandLogger: cmd =>
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    > ppds-migrate {cmd}");
                Console.ResetColor();
            },
            outputLogger: output =>
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                var lines = output.Split('\n');
                foreach (var line in lines.Take(15))
                {
                    Console.WriteLine($"    {line}");
                }
                if (lines.Length > 15)
                {
                    Console.WriteLine($"    ... ({lines.Length - 15} more lines)");
                }
                Console.ResetColor();
            },
            errorLogger: error =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"    CLI Error: {error}");
                Console.ResetColor();
            });
    }

    /// <summary>
    /// Checks if the CLI executable exists.
    /// </summary>
    public bool Exists => File.Exists(_cliPath);

    /// <summary>
    /// Gets the CLI path.
    /// </summary>
    public string CliPath => _cliPath;

    /// <inheritdoc/>
    public Task<CliResult> SchemaGenerateAsync(
        IEnumerable<string> entities,
        string outputPath,
        GlobalOptions options,
        bool includeRelationships = true,
        Dictionary<string, string[]>? includeAttributes = null,
        CancellationToken cancellationToken = default)
    {
        var args = new CliArgs(options)
            .Command("schema generate")
            .WithEntities(entities.ToArray())
            .WithOutput(outputPath)
            .WithRelationships(includeRelationships)
            .WithIncludeAttributes(includeAttributes);

        return RunAsync(args, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<CliResult> ExportAsync(
        string schemaPath,
        string outputPath,
        GlobalOptions options,
        CancellationToken cancellationToken = default)
    {
        var args = new CliArgs(options)
            .Command("export")
            .WithSchema(schemaPath)
            .WithOutput(outputPath);

        return RunAsync(args, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<CliResult> ImportAsync(
        string dataPath,
        GlobalOptions options,
        ImportCliOptions? importOptions = null,
        CancellationToken cancellationToken = default)
    {
        var opts = importOptions ?? new ImportCliOptions();

        var args = new CliArgs(options)
            .Command("import")
            .WithData(dataPath)
            .WithMode(opts.Mode)
            .WithUserMapping(opts.UserMappingPath)
            .Flag("--strip-owner-fields", opts.StripOwnerFields)
            .Flag("--bypass-plugins", opts.BypassPlugins)
            .Flag("--bypass-flows", opts.BypassFlows)
            .Flag("--continue-on-error", opts.ContinueOnError);

        return RunAsync(args, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CliResult> RunAsync(
        CliArgs args,
        CancellationToken cancellationToken = default)
    {
        var arguments = args.Build();
        var redactedArgs = args.BuildRedacted();

        // Log command if verbose
        _commandLogger?.Invoke(redactedArgs);

        var psi = new ProcessStartInfo
        {
            FileName = _cliPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new CliResult
            {
                ExitCode = -1,
                StandardError = $"Failed to start CLI: {ex.Message}",
                CommandLine = redactedArgs
            };
        }

        // IMPORTANT: Always drain both streams to prevent deadlock.
        // If the output buffer fills while we wait for exit, the process blocks forever.
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort
            }
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;

        // Log output if verbose and we have output
        if (!string.IsNullOrEmpty(output))
        {
            _outputLogger?.Invoke(output);
        }

        // Log errors
        if (!string.IsNullOrEmpty(error) && process.ExitCode != 0)
        {
            _errorLogger?.Invoke(error);
        }

        return new CliResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = output,
            StandardError = error,
            CommandLine = redactedArgs
        };
    }
}
