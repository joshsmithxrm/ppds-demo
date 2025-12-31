namespace PPDSDemo.Api.Models;

/// <summary>
/// Represents a product in the external product catalog.
/// Used by the Virtual Table data provider.
/// </summary>
public record Product
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Sku { get; init; } = "";
    public decimal Price { get; init; }
    public string Category { get; init; } = "";
    public bool InStock { get; init; }
}
