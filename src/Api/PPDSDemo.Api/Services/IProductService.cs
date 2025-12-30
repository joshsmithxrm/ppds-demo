using PPDSDemo.Api.Models;

namespace PPDSDemo.Api.Services;

/// <summary>
/// Service interface for product operations.
/// </summary>
public interface IProductService
{
    IEnumerable<Product> GetAll(string? filter = null);
    Product? GetById(Guid id);
    Product Create(Product product);
    Product? Update(Guid id, Product product);
    bool Delete(Guid id);
}
