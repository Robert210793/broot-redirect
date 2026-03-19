using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Interfaces;

namespace Broot.Redirect.API.Controllers
{
    [ApiController]
    [Route("api/health")]
    public class HealthController : ControllerBase
    {
        private static readonly DateTimeOffset StartTime = DateTimeOffset.UtcNow;

        private readonly IRedirectRuleRepository _repository;
        private readonly IRuleCacheService _cacheService;
        private readonly ILogger<HealthController> _logger;

        public HealthController(
            IRedirectRuleRepository repository,
            IRuleCacheService cacheService,
            ILogger<HealthController> logger)
        {
            _repository = repository;
            _cacheService = cacheService;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/health
        /// Returns system health including Table Storage connectivity, cache status,
        /// rule count, and uptime. No authentication required.
        /// Returns 200 for healthy, 503 for unhealthy.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetHealth()
        {
            var uptimeSeconds = (long)(DateTimeOffset.UtcNow - StartTime).TotalSeconds;

            var tableStorageCheck = await CheckTableStorageAsync();
            var cacheCheck = CheckCache();

            var overallStatus = (tableStorageCheck.Status == "ok" && cacheCheck.Status == "ok")
                ? "healthy"
                : "unhealthy";

            var response = new HealthResponse
            {
                Status = overallStatus,
                Timestamp = DateTimeOffset.UtcNow.ToString("o"),
                UptimeSeconds = uptimeSeconds,
                RuleCount = _cacheService.RuleCount,
                Checks = new HealthChecks
                {
                    TableStorage = tableStorageCheck,
                    Cache = cacheCheck
                }
            };

            var statusCode = overallStatus == "healthy"
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable;

            return StatusCode(statusCode, response);
        }

        private async Task<HealthCheckDetail> CheckTableStorageAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await _repository.GetAllAsync();

                stopwatch.Stop();

                return new HealthCheckDetail
                {
                    Status = "ok",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception exception)
            {
                stopwatch.Stop();

                _logger.LogError(exception, "Table Storage health check failed");

                return new HealthCheckDetail
                {
                    Status = "error",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Error = exception.Message
                };
            }
        }

        private HealthCheckDetail CheckCache()
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var isWarmedUp = _cacheService.IsWarmedUp;
                var ruleCount = _cacheService.RuleCount;

                stopwatch.Stop();

                return new HealthCheckDetail
                {
                    Status = isWarmedUp ? "ok" : "warming_up",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception exception)
            {
                stopwatch.Stop();

                _logger.LogError(exception, "Cache health check failed");

                return new HealthCheckDetail
                {
                    Status = "error",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Error = exception.Message
                };
            }
        }
    }
}