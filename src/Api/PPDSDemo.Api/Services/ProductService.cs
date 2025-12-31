using System.Collections.Concurrent;
using PPDSDemo.Api.Models;

namespace PPDSDemo.Api.Services;

/// <summary>
/// In-memory implementation of IProductService.
/// Provides sample product data for Virtual Table demonstration.
/// </summary>
public class ProductService : IProductService
{
    private readonly ConcurrentDictionary<Guid, Product> _products = new();

    public ProductService()
    {
        SeedProducts();
    }

    private void SeedProducts()
    {
        var samples = new[]
        {
            new Product
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Widget Pro",
                Sku = "WGT-001",
                Price = 29.99m,
                Category = "Widgets",
                InStock = true
            },
            new Product
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Gadget Plus",
                Sku = "GDG-002",
                Price = 49.99m,
                Category = "Gadgets",
                InStock = true
            },
            new Product
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "Thingamajig",
                Sku = "THG-003",
                Price = 19.99m,
                Category = "Things",
                InStock = false
            },
            new Product
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Name = "Doohickey Deluxe",
                Sku = "DHK-004",
                Price = 79.99m,
                Category = "Doohickeys",
                InStock = true
            },
            new Product
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Name = "Whatchamacallit",
                Sku = "WMC-005",
                Price = 14.99m,
                Category = "Things",
                InStock = true
            }
        };

        foreach (var product in samples)
        {
            _products[product.Id] = product;
        }
    }

    public IEnumerable<Product> GetAll(string? filter = null)
    {
        var products = _products.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            products = products.Where(p =>
                p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                p.Sku.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                p.Category.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        return products.OrderBy(p => p.Name);
    }

    public Product? GetById(Guid id)
    {
        return _products.TryGetValue(id, out var product) ? product : null;
    }

    public Product Create(Product product)
    {
        var newProduct = product with { Id = product.Id == Guid.Empty ? Guid.NewGuid() : product.Id };
        _products[newProduct.Id] = newProduct;
        return newProduct;
    }

    public Product? Update(Guid id, Product product)
    {
        if (!_products.ContainsKey(id))
        {
            return null;
        }

        var updatedProduct = product with { Id = id };
        _products[id] = updatedProduct;
        return updatedProduct;
    }

    public bool Delete(Guid id)
    {
        return _products.TryRemove(id, out _);
    }
}
