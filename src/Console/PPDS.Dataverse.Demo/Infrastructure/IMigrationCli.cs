namespace PPDS.Dataverse.Demo.Infrastructure;

/// <summary>
/// Abstraction for invoking the ppds-migrate CLI.
///
/// This interface eliminates the need for each command to:
/// - Know the CLI path
/// - Manage Process lifecycle
/// - Handle output stream draining
/// - Remember to include global options
///
/// All methods automatically include verbose/debug/environment from GlobalOptions.
/// </summary>
public interface IMigrationCli
{
    /// <summary>
    /// Generates a migration schema from Dataverse metadata.
    /// Equivalent to: ppds-migrate schema generate -e {entities} -o {outputPath} [options]
    /// </summary>
    /// <param name="entities">Entity logical names to include in schema.</param>
    /// <param name="outputPath">Output path for generated schema XML.</param>
    /// <param name="options">Global CLI options (environment, verbose, debug).</param>
    /// <param name="includeRelationships">Include relationship metadata.</param>
    /// <param name="includeAttributes">Map of entity to attributes to include (null = all attributes).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CliResult> SchemaGenerateAsync(
        IEnumerable<string> entities,
        string outputPath,
        GlobalOptions options,
        bool includeRelationships = true,
        Dictionary<string, string[]>? includeAttributes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports data from Dataverse to a ZIP file.
    /// Equivalent to: ppds-migrate export --schema {schemaPath} --output {outputPath} [options]
    /// </summary>
    Task<CliResult> ExportAsync(
        string schemaPath,
        string outputPath,
        GlobalOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports data from a ZIP file into Dataverse.
    /// Equivalent to: ppds-migrate import --data {dataPath} --mode {mode} [options]
    /// </summary>
    Task<CliResult> ImportAsync(
        string dataPath,
        GlobalOptions options,
        ImportCliOptions? importOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs an arbitrary CLI command with the given arguments.
    /// Global options are automatically appended.
    /// </summary>
    Task<CliResult> RunAsync(
        CliArgs args,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options specific to the import command.
/// </summary>
public record ImportCliOptions
{
    /// <summary>
    /// Import mode: Create, Update, or Upsert.
    /// Default: Upsert
    /// </summary>
    public string Mode { get; init; } = "Upsert";

    /// <summary>
    /// Path to user mapping XML file for remapping user references.
    /// </summary>
    public string? UserMappingPath { get; init; }

    /// <summary>
    /// Strip ownership fields (ownerid, createdby, modifiedby).
    /// </summary>
    public bool StripOwnerFields { get; init; }

    /// <summary>
    /// Bypass custom plugin execution during import.
    /// </summary>
    public bool BypassPlugins { get; init; }

    /// <summary>
    /// Bypass Power Automate flow triggers during import.
    /// </summary>
    public bool BypassFlows { get; init; }

    /// <summary>
    /// Continue import on individual record failures.
    /// </summary>
    public bool ContinueOnError { get; init; }
}
