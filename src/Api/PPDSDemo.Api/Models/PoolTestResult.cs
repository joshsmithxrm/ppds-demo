namespace PPDSDemo.Api.Models;

/// <summary>
/// Result of a connection pool performance test.
/// </summary>
public record PoolTestResult
{
    public int OperationCount { get; init; }
    public bool Parallel { get; init; }
    public long TotalMs { get; init; }
    public double AverageMs { get; init; }
    public double MinMs { get; init; }
    public double MaxMs { get; init; }
    public int ErrorCount { get; init; }
}
