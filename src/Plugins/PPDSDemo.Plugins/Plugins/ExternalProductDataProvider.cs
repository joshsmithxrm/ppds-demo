using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PPDS.Plugins;

namespace PPDSDemo.Plugins.Plugins
{
    /// <summary>
    /// Data Provider plugin for ppds_ExternalProduct virtual table.
    /// Routes CRUD operations to Azure Web API.
    /// </summary>
    /// <remarks>
    /// This plugin requires multiple step registrations (one per message):
    /// - Retrieve (Stage 30, Mode Sync)
    /// - RetrieveMultiple (Stage 30, Mode Sync)
    /// - Create (Stage 30, Mode Sync)
    /// - Update (Stage 30, Mode Sync)
    /// - Delete (Stage 30, Mode Sync)
    ///
    /// Secure Configuration Format: "apiKey|baseUrl"
    /// Example: "my-secret-key|https://api-ppds-demo.azurewebsites.net"
    /// </remarks>
    public class ExternalProductDataProvider : PluginBase
    {
        // Static client cache to prevent socket exhaustion
        // HttpClient is designed to be reused across requests
        private static readonly ConcurrentDictionary<string, HttpClient> _httpClients =
            new ConcurrentDictionary<string, HttpClient>();

        private readonly string _apiKey;
        private readonly string _apiBaseUrl;

        private const string EntityLogicalName = "ppds_externalproduct";
        private const string PrimaryIdAttribute = "ppds_externalproductid";

        /// <summary>
        /// Initializes a new instance of the ExternalProductDataProvider class.
        /// </summary>
        /// <param name="unsecureConfig">Unsecure configuration (not used).</param>
        /// <param name="secureConfig">Secure configuration in format "apiKey|baseUrl".</param>
        public ExternalProductDataProvider(string unsecureConfig, string secureConfig)
        {
            var parts = secureConfig?.Split('|') ?? Array.Empty<string>();
            _apiKey = parts.Length > 0 ? parts[0] : "";
            _apiBaseUrl = parts.Length > 1 ? parts[1] : "";
        }

        protected override void ExecutePlugin(LocalPluginContext context)
        {
            // Validate configuration
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiBaseUrl))
            {
                throw new InvalidPluginExecutionException(
                    "Plugin is not configured. Secure configuration must contain 'apiKey|baseUrl'.");
            }

            var message = context.PluginExecutionContext.MessageName;
            context.Trace($"Processing {message} for virtual table");

            switch (message)
            {
                case "Retrieve":
                    HandleRetrieve(context);
                    break;
                case "RetrieveMultiple":
                    HandleRetrieveMultiple(context);
                    break;
                case "Create":
                    HandleCreate(context);
                    break;
                case "Update":
                    HandleUpdate(context);
                    break;
                case "Delete":
                    HandleDelete(context);
                    break;
                default:
                    throw new InvalidPluginExecutionException($"Unsupported message: {message}");
            }
        }

        private void HandleRetrieve(LocalPluginContext context)
        {
            var target = context.GetTargetEntityReference();
            if (target == null)
            {
                throw new InvalidPluginExecutionException("Target entity reference is required for Retrieve.");
            }

            context.Trace($"Retrieving product {target.Id}");

            var client = GetHttpClient();
            using (var request = CreateRequest(HttpMethod.Get, $"/api/products/{target.Id}"))
            {
                var response = client.SendAsync(request).Result;

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        throw new InvalidPluginExecutionException($"Product {target.Id} not found.");
                    }
                    throw new InvalidPluginExecutionException(
                        $"API returned {response.StatusCode}");
                }

                var body = response.Content.ReadAsStringAsync().Result;
                var product = JsonSerializer.Deserialize<ProductDto>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var entity = MapToEntity(product);
                context.PluginExecutionContext.OutputParameters["BusinessEntity"] = entity;

                context.Trace($"Retrieved product: {product.Name}");
            }
        }

        private void HandleRetrieveMultiple(LocalPluginContext context)
        {
            context.Trace("Retrieving multiple products");

            // TODO: Parse query for filter conditions
            // For now, retrieve all products

            var client = GetHttpClient();
            using (var request = CreateRequest(HttpMethod.Get, "/api/products"))
            {
                var response = client.SendAsync(request).Result;

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidPluginExecutionException(
                        $"API returned {response.StatusCode}");
                }

                var body = response.Content.ReadAsStringAsync().Result;
                var products = JsonSerializer.Deserialize<List<ProductDto>>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var collection = new EntityCollection { EntityName = EntityLogicalName };
                foreach (var product in products ?? new List<ProductDto>())
                {
                    collection.Entities.Add(MapToEntity(product));
                }

                context.PluginExecutionContext.OutputParameters["BusinessEntityCollection"] = collection;

                context.Trace($"Retrieved {collection.Entities.Count} products");
            }
        }

        private void HandleCreate(LocalPluginContext context)
        {
            var target = context.GetTargetEntity();
            if (target == null)
            {
                throw new InvalidPluginExecutionException("Target entity is required for Create.");
            }

            context.Trace("Creating product");

            var product = MapFromEntity(target);
            var json = JsonSerializer.Serialize(product);

            var client = GetHttpClient();
            using (var request = CreateRequest(HttpMethod.Post, "/api/products"))
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = client.SendAsync(request).Result;

                if (!response.IsSuccessStatusCode)
                {
                    var error = response.Content.ReadAsStringAsync().Result;
                    throw new InvalidPluginExecutionException(
                        $"Failed to create product: {response.StatusCode} - {error}");
                }

                var body = response.Content.ReadAsStringAsync().Result;
                var created = JsonSerializer.Deserialize<ProductDto>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                context.PluginExecutionContext.OutputParameters["id"] = created.Id;

                context.Trace($"Created product with ID: {created.Id}");
            }
        }

        private void HandleUpdate(LocalPluginContext context)
        {
            var target = context.GetTargetEntity();
            if (target == null)
            {
                throw new InvalidPluginExecutionException("Target entity is required for Update.");
            }

            context.Trace($"Updating product {target.Id}");

            var product = MapFromEntity(target);
            var json = JsonSerializer.Serialize(product);

            var client = GetHttpClient();
            using (var request = CreateRequest(HttpMethod.Put, $"/api/products/{target.Id}"))
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = client.SendAsync(request).Result;

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        throw new InvalidPluginExecutionException($"Product {target.Id} not found.");
                    }
                    var error = response.Content.ReadAsStringAsync().Result;
                    throw new InvalidPluginExecutionException(
                        $"Failed to update product: {response.StatusCode} - {error}");
                }

                context.Trace($"Updated product {target.Id}");
            }
        }

        private void HandleDelete(LocalPluginContext context)
        {
            var target = context.GetTargetEntityReference();
            if (target == null)
            {
                throw new InvalidPluginExecutionException("Target entity reference is required for Delete.");
            }

            context.Trace($"Deleting product {target.Id}");

            var client = GetHttpClient();
            using (var request = CreateRequest(HttpMethod.Delete, $"/api/products/{target.Id}"))
            {
                var response = client.SendAsync(request).Result;

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        throw new InvalidPluginExecutionException($"Product {target.Id} not found.");
                    }
                    throw new InvalidPluginExecutionException(
                        $"Failed to delete product: {response.StatusCode}");
                }

                context.Trace($"Deleted product {target.Id}");
            }
        }

        private HttpClient GetHttpClient()
        {
            return _httpClients.GetOrAdd(_apiBaseUrl, url =>
            {
                var client = new HttpClient
                {
                    BaseAddress = new Uri(url),
                    Timeout = TimeSpan.FromSeconds(30)
                };
                return client;
            });
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string requestUri)
        {
            var request = new HttpRequestMessage(method, requestUri);
            request.Headers.Add("X-API-Key", _apiKey);
            return request;
        }

        private Entity MapToEntity(ProductDto product)
        {
            var entity = new Entity(EntityLogicalName, product.Id);
            entity[PrimaryIdAttribute] = product.Id;
            entity["ppds_name"] = product.Name;
            entity["ppds_sku"] = product.Sku;
            entity["ppds_price"] = new Money(product.Price);
            entity["ppds_category"] = product.Category;
            entity["ppds_instock"] = product.InStock;
            return entity;
        }

        private ProductDto MapFromEntity(Entity entity)
        {
            return new ProductDto
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>("ppds_name") ?? "",
                Sku = entity.GetAttributeValue<string>("ppds_sku") ?? "",
                Price = entity.GetAttributeValue<Money>("ppds_price")?.Value ?? 0m,
                Category = entity.GetAttributeValue<string>("ppds_category") ?? "",
                InStock = entity.GetAttributeValue<bool?>("ppds_instock") ?? true
            };
        }

        private class ProductDto
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public string Sku { get; set; } = "";
            public decimal Price { get; set; }
            public string Category { get; set; } = "";
            public bool InStock { get; set; }
        }
    }
}
