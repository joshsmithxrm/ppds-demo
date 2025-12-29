namespace PPDS.Dataverse.Demo.Infrastructure;

/// <summary>
/// Common options shared across all commands.
/// This is the single source of truth for cross-cutting concerns like logging and environment.
///
/// By flowing this object through the entire command hierarchy, we ensure:
/// - Consistent option naming across commands
/// - No forgotten flags when invoking sub-commands or CLI
/// - Single place to add new global options
/// </summary>
public record GlobalOptions
{
    /// <summary>
    /// Target environment name (e.g., "Dev", "QA", "Prod").
    /// If null, uses DefaultEnvironment from configuration.
    /// </summary>
    public string? Environment { get; init; }

    /// <summary>
    /// Enable verbose logging (operational messages: Connecting..., Processing...).
    /// Maps to LogLevel.Information for SDK, --verbose for CLI.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Enable debug logging (diagnostic details: parallelism, ceiling, internal state).
    /// Maps to LogLevel.Debug for SDK, --debug for CLI.
    /// Implies Verbose=true for practical purposes.
    /// </summary>
    public bool Debug { get; init; }

    /// <summary>
    /// Max parallel batches for bulk operations.
    /// If null, uses SDK default (based on server recommendation).
    /// </summary>
    public int? Parallelism { get; init; }

    /// <summary>
    /// Effective verbose setting (Debug implies Verbose).
    /// </summary>
    public bool EffectiveVerbose => Verbose || Debug;
}

/// <summary>
/// Extension methods for System.CommandLine option creation.
/// Ensures consistent option definitions across all commands.
/// </summary>
public static class GlobalOptionsExtensions
{
    /// <summary>
    /// Creates the standard --environment/--env/-e option.
    /// </summary>
    public static System.CommandLine.Option<string?> CreateEnvironmentOption(
        string? description = null,
        bool isRequired = false)
    {
        var option = new System.CommandLine.Option<string?>(
            aliases: ["--environment", "--env", "-e"],
            description: description ?? "Target environment name (e.g., 'Dev', 'QA'). Uses DefaultEnvironment from config if not specified.");

        if (isRequired)
        {
            option.IsRequired = true;
        }

        return option;
    }

    /// <summary>
    /// Creates the standard --verbose/-v option.
    /// </summary>
    public static System.CommandLine.Option<bool> CreateVerboseOption(
        string? description = null)
    {
        return new System.CommandLine.Option<bool>(
            aliases: ["--verbose", "-v"],
            description: description ?? "Enable verbose logging (operational: Connecting..., Processing...)");
    }

    /// <summary>
    /// Creates the standard --debug option.
    /// </summary>
    public static System.CommandLine.Option<bool> CreateDebugOption(
        string? description = null)
    {
        return new System.CommandLine.Option<bool>(
            name: "--debug",
            description: description ?? "Enable debug logging (diagnostic: parallelism, ceiling, internal state)");
    }

    /// <summary>
    /// Creates the standard --parallelism option.
    /// </summary>
    public static System.CommandLine.Option<int?> CreateParallelismOption(
        string? description = null)
    {
        return new System.CommandLine.Option<int?>(
            name: "--parallelism",
            description: description ?? "Max parallel batches (uses SDK default if not specified)");
    }

}
