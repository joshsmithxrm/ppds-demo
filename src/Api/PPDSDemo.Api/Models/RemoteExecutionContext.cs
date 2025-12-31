namespace PPDSDemo.Api.Models;

/// <summary>
/// Simplified model for Dataverse webhook payload.
/// Represents the execution context passed by Dataverse service endpoints.
/// </summary>
public record RemoteExecutionContext
{
    public string MessageName { get; init; } = "";
    public int Stage { get; init; }
    public string PrimaryEntityName { get; init; } = "";
    public Guid PrimaryEntityId { get; init; }
    public int Depth { get; init; }
    public Guid UserId { get; init; }
    public Guid OrganizationId { get; init; }
    public Dictionary<string, object>? InputParameters { get; init; }
}
