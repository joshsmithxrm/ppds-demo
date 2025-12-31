namespace PPDSDemo.Api.Models;

/// <summary>
/// Response model for the Process Account Custom API.
/// </summary>
public record ProcessAccountResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}
