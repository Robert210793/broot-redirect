using System.Diagnostics;
using System.Reflection;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Infrastructure.Persistence;

namespace Broot.Redirect.API.Controllers
{
    [ApiController]
    [Route("api/health")]
    public class HealthController : ControllerBase
    {
        private static readonly DateTimeOffset StartTime = DateTimeOffset.UtcNow;

        private static readonly string AppVersion = typeof(HealthController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        private readonly IRuleCacheService _cacheService;
        private readonly TableClient _tableClient;
        private readonly ILogger<HealthController> _logger;

        public HealthController(
            IRuleCacheService cacheService,
            IOptions<TableStorageOptions> tableStorageOptions,
            ILogger<HealthController> logger)
        {
            _cacheService = cacheService;
            _tableClient = new TableServiceClient(tableStorageOptions.Value.ConnectionString)
                .GetTableClient(tableStorageOptions.Value.TableName);
            _logger = logger;
        }

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
                Version = AppVersion,
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
                await _tableClient.QueryAsync<TableEntity>(
                    maxPerPage: 1)
                    .GetAsyncEnumerator()
                    .MoveNextAsync();

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