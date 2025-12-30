using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PPDSDemo.Api.Models;
using PPDSDemo.Api.Services;

namespace PPDSDemo.Api.Controllers;

/// <summary>
/// API endpoints for product operations.
/// Used by the Virtual Table data provider plugin.
/// </summary>
[ApiController]
[Authorize]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(IProductService productService, ILogger<ProductsController> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    /// <summary>
    /// Get all products, optionally filtered.
    /// Used by Virtual Table RetrieveMultiple.
    /// </summary>
    [HttpGet]
    public ActionResult<IEnumerable<Product>> GetAll([FromQuery] string? filter)
    {
        _logger.LogDebug("Getting all products with filter: {Filter}", filter);
        var products = _productService.GetAll(filter);
        return Ok(products);
    }

    /// <summary>
    /// Get a single product by ID.
    /// Used by Virtual Table Retrieve.
    /// </summary>
    [HttpGet("{id:guid}")]
    public ActionResult<Product> Get(Guid id)
    {
        _logger.LogDebug("Getting product: {ProductId}", id);
        var product = _productService.GetById(id);

        if (product is null)
        {
            return NotFound();
        }

        return Ok(product);
    }

    /// <summary>
    /// Create a new product.
    /// Used by Virtual Table Create.
    /// </summary>
    [HttpPost]
    public ActionResult<Product> Create([FromBody] Product product)
    {
        _logger.LogInformation("Creating product: {ProductName}", product.Name);
        var created = _productService.Create(product);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>
    /// Update an existing product.
    /// Used by Virtual Table Update.
    /// </summary>
    [HttpPut("{id:guid}")]
    public ActionResult<Product> Update(Guid id, [FromBody] Product product)
    {
        _logger.LogInformation("Updating product: {ProductId}", id);
        var updated = _productService.Update(id, product);

        if (updated is null)
        {
            return NotFound();
        }

        return Ok(updated);
    }

    /// <summary>
    /// Delete a product.
    /// Used by Virtual Table Delete.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        _logger.LogInformation("Deleting product: {ProductId}", id);
        var deleted = _productService.Delete(id);

        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}
