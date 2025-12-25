using System.Text;
using System.Text.RegularExpressions;

namespace PPDS.Dataverse.Demo.Infrastructure;

/// <summary>
/// Builder for constructing CLI arguments with automatic inclusion of global options.
///
/// This builder ensures that verbose/debug/environment flags are ALWAYS included
/// when invoking the ppds-migrate CLI, eliminating the class of bugs where these
/// flags are forgotten in string concatenation.
///
/// Usage:
///   var args = new CliArgs(globalOptions)
///       .Command("import")
///       .Option("--data", dataPath)
///       .Option("--mode", "Upsert")
///       .Build();
/// </summary>
public class CliArgs
{
    private readonly GlobalOptions _global;
    private readonly List<string> _parts = new();
    private string? _secretsId;

    /// <summary>
    /// Creates a new CLI argument builder with the given global options.
    /// </summary>
    /// <param name="global">Global options that will be automatically appended.</param>
    public CliArgs(GlobalOptions global)
    {
        _global = global ?? throw new ArgumentNullException(nameof(global));
    }

    /// <summary>
    /// Adds a command or subcommand (e.g., "import", "schema generate").
    /// </summary>
    public CliArgs Command(string command)
    {
        if (!string.IsNullOrWhiteSpace(command))
        {
            _parts.Add(command);
        }
        return this;
    }

    /// <summary>
    /// Adds an option with a value (e.g., --data "path/to/file").
    /// Automatically quotes values containing spaces.
    /// </summary>
    public CliArgs Option(string name, string? value)
    {
        if (value != null)
        {
            // Quote if contains spaces and not already quoted
            var formattedValue = value.Contains(' ') && !value.StartsWith('"')
                ? $"\"{value}\""
                : value;
            _parts.Add($"{name} {formattedValue}");
        }
        return this;
    }

    /// <summary>
    /// Adds an option with multiple values (e.g., -e account,contact).
    /// </summary>
    public CliArgs Option(string name, IEnumerable<string>? values)
    {
        if (values != null)
        {
            var joined = string.Join(",", values);
            if (!string.IsNullOrEmpty(joined))
            {
                _parts.Add($"{name} {joined}");
            }
        }
        return this;
    }

    /// <summary>
    /// Adds a boolean flag if the condition is true (e.g., --continue-on-error).
    /// </summary>
    public CliArgs Flag(string name, bool condition = true)
    {
        if (condition)
        {
            _parts.Add(name);
        }
        return this;
    }

    /// <summary>
    /// Sets the secrets ID for cross-process secret sharing.
    /// Default: "ppds-dataverse-demo"
    /// </summary>
    public CliArgs WithSecretsId(string secretsId)
    {
        _secretsId = secretsId;
        return this;
    }

    /// <summary>
    /// Builds the final argument string, automatically including:
    /// - --env (from GlobalOptions.Environment)
    /// - --verbose (from GlobalOptions.Verbose)
    /// - --debug (from GlobalOptions.Debug)
    /// - --secrets-id (default or custom)
    /// </summary>
    public string Build()
    {
        var result = new StringBuilder();

        // Add explicit parts first
        foreach (var part in _parts)
        {
            if (result.Length > 0) result.Append(' ');
            result.Append(part);
        }

        // Always append global options - this is the key insight!
        // By centralizing here, we can never forget these flags.

        if (_global.Environment != null)
        {
            result.Append($" --env {_global.Environment}");
        }

        if (_global.Debug)
        {
            result.Append(" --debug");
        }
        else if (_global.Verbose)
        {
            result.Append(" --verbose");
        }

        // Secrets ID for cross-process secret sharing
        var secretsId = _secretsId ?? "ppds-dataverse-demo";
        result.Append($" --secrets-id {secretsId}");

        return result.ToString();
    }

    /// <summary>
    /// Returns the built arguments with secrets redacted (for logging).
    /// </summary>
    public string BuildRedacted()
    {
        return RedactSecrets(Build());
    }

    /// <summary>
    /// Redacts sensitive values from CLI arguments for safe logging.
    /// </summary>
    private static string RedactSecrets(string arguments)
    {
        return Regex.Replace(
            arguments,
            @"(ClientSecret|Password|Secret|Key)=([^;""'\s]+)",
            "$1=***REDACTED***",
            RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// Fluent extension methods for common CLI argument patterns.
/// </summary>
public static class CliArgsExtensions
{
    /// <summary>
    /// Adds schema file option.
    /// </summary>
    public static CliArgs WithSchema(this CliArgs args, string schemaPath)
        => args.Option("--schema", schemaPath);

    /// <summary>
    /// Adds output file option.
    /// </summary>
    public static CliArgs WithOutput(this CliArgs args, string outputPath)
        => args.Option("--output", outputPath);

    /// <summary>
    /// Adds data file option.
    /// </summary>
    public static CliArgs WithData(this CliArgs args, string dataPath)
        => args.Option("--data", dataPath);

    /// <summary>
    /// Adds import mode option.
    /// </summary>
    public static CliArgs WithMode(this CliArgs args, string mode)
        => args.Option("--mode", mode);

    /// <summary>
    /// Adds entities option.
    /// </summary>
    public static CliArgs WithEntities(this CliArgs args, params string[] entities)
        => args.Option("-e", entities);

    /// <summary>
    /// Adds user mapping file option.
    /// </summary>
    public static CliArgs WithUserMapping(this CliArgs args, string? mappingPath)
        => mappingPath != null ? args.Option("--user-mapping", mappingPath) : args;

    /// <summary>
    /// Adds include-relationships flag.
    /// </summary>
    public static CliArgs WithRelationships(this CliArgs args, bool include = true)
        => args.Flag("--include-relationships", include);

    /// <summary>
    /// Adds include-attributes option for filtering exported attributes per entity.
    /// Format: --include-attributes "entity1:attr1,attr2;entity2:attr3,attr4"
    /// </summary>
    public static CliArgs WithIncludeAttributes(this CliArgs args, Dictionary<string, string[]>? includeAttributes)
    {
        if (includeAttributes == null || includeAttributes.Count == 0)
            return args;

        // Build format: "entity1:attr1,attr2;entity2:attr3,attr4"
        var formatted = string.Join(";",
            includeAttributes.Select(kv => $"{kv.Key}:{string.Join(",", kv.Value)}"));

        return args.Option("--include-attributes", formatted);
    }
}
