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
[ApiController]
[Authorize(Policy = "ApiAccess")]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly IDataverseConnectionPool _pool;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(IDataverseConnectionPool pool, ILogger<DiagnosticsController> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    /// <summary>
    /// Health check endpoint.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Tests connection pool performance by running WhoAmI calls.
    /// </summary>
    [HttpGet("pool-test")]
    public async Task<ActionResult<PoolTestResult>> PoolTest(
        [FromQuery] int operations = 100,
        [FromQuery] bool parallel = true)
    {
        // Cap operations to prevent DoS abuse
        const int maxOperations = 1000;
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
