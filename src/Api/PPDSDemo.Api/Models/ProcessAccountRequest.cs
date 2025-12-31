namespace PPDSDemo.Api.Models;

/// <summary>
/// Request model for the Process Account Custom API.
/// </summary>
public record ProcessAccountRequest
{
    public Guid AccountId { get; init; }

    /// <summary>
    /// The action to perform: "validate", "enrich", or "sync".
    /// </summary>
    public string Action { get; init; } = "";
}
