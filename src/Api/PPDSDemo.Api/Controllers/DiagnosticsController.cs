using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Crm.Sdk.Messages;
using PPDS.Dataverse.Pooling;
using PPDSDemo.Api.Models;

namespace PPDSDemo.Api.Controllers;

/// <summary>
/// Diagnostic endpoints for health checks and pool testing.
/// </summary>
/// <remarks>
/// <para><strong>Health Checks:</strong></para>
/// <para>
/// This controller does NOT provide an anonymous health endpoint. For Azure App Service,
/// configure health checks in Azure Portal (Monitoring → Health check) which probes
/// your app internally. The platform handles health monitoring - application code
/// should not expose unauthenticated endpoints.
/// </para>
/// <para>
/// If you need authenticated health verification, use the /api/diagnostics/health endpoint
/// which requires API key authentication.
/// </para>
/// <para><strong>Pool Testing:</strong></para>
/// <para>
/// The pool-test endpoint is only available in Development environment. Production
/// performance testing should use Azure Load Testing or similar tools against
/// non-production environments. Production monitoring should use Application Insights.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = "ApiAccess")]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly IDataverseConnectionPool _pool;
    private readonly ILogger<DiagnosticsController> _logger;
    private readonly IHostEnvironment _environment;

    public DiagnosticsController(
        IDataverseConnectionPool pool,
        ILogger<DiagnosticsController> logger,
        IHostEnvironment environment)
    {
        _pool = pool;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Authenticated health check endpoint.
    /// </summary>
    /// <remarks>
    /// Requires API key authentication. For unauthenticated health probes, configure
    /// Azure App Service's built-in health check feature in the Azure Portal.
    /// </remarks>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Tests connection pool performance by running WhoAmI calls.
    /// </summary>
    /// <remarks>
    /// Only available in Development environment. Returns 404 in Production.
    /// Use Azure Load Testing for production performance validation.
    /// </remarks>
    [HttpGet("pool-test")]
    public async Task<ActionResult<PoolTestResult>> PoolTest(
        [FromQuery] int operations = 100,
        [FromQuery] bool parallel = true)
    {
        // This endpoint is only available in Development
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        // Cap operations for development testing
        const int maxOperations = 100;
        if (operations < 1 || operations > maxOperations)
        {
            return BadRequest(new { error = $"Operations must be between 1 and {maxOperations}" });
        }

        _logger.LogInformation("Starting pool test: {Operations} operations, parallel={Parallel}", operations, parallel);

        var timings = new List<long>();
        var errors = 0;
        var stopwatch = Stopwatch.StartNew();

        if (parallel)
        {
            await using var client = await _pool.GetClientAsync();
            var dop = client.RecommendedDegreesOfParallelism;
            _logger.LogDebug("Using parallelism: {Dop}", dop);

            var results = await Task.WhenAll(
                Enumerable.Range(0, operations).Select(async _ =>
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        await using var c = await _pool.GetClientAsync();
                        await c.ExecuteAsync(new WhoAmIRequest());
                        sw.Stop();
                        return (sw.ElapsedMilliseconds, Error: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "WhoAmI call failed");
                        sw.Stop();
                        return (sw.ElapsedMilliseconds, Error: true);
                    }
                }));

            foreach (var (elapsed, error) in results)
            {
                timings.Add(elapsed);
                if (error) errors++;
            }
        }
        else
        {
            for (var i = 0; i < operations; i++)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    await using var client = await _pool.GetClientAsync();
                    await client.ExecuteAsync(new WhoAmIRequest());
                    sw.Stop();
                    timings.Add(sw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "WhoAmI call failed");
                    sw.Stop();
                    timings.Add(sw.ElapsedMilliseconds);
                    errors++;
                }
            }
        }

        stopwatch.Stop();

        var result = new PoolTestResult
        {
            OperationCount = operations,
            Parallel = parallel,
            TotalMs = stopwatch.ElapsedMilliseconds,
            AverageMs = timings.Count > 0 ? timings.Average() : 0,
            MinMs = timings.Count > 0 ? timings.Min() : 0,
            MaxMs = timings.Count > 0 ? timings.Max() : 0,
            ErrorCount = errors
        };

        _logger.LogInformation("Pool test complete: {TotalMs}ms total, {AverageMs}ms avg", result.TotalMs, result.AverageMs);

        return Ok(result);
    }
}
