using Broot.Redirect.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Broot.Redirect.Infrastructure.Cache
{
    /// <summary>
    /// Background service that periodically deletes tracking entries older than the configured retention period.
    /// Runs once daily.
    /// </summary>
    public class TrackingCleanupService : BackgroundService
    {
        private readonly ITrackingRepository _trackingRepository;
        private readonly int _retentionDays;
        private readonly ILogger<TrackingCleanupService> _logger;

        public TrackingCleanupService(
            ITrackingRepository trackingRepository,
            int retentionDays,
            ILogger<TrackingCleanupService> logger)
        {
            _trackingRepository = trackingRepository;
            _retentionDays = retentionDays;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays);

                    _logger.LogInformation("Tracking cleanup starting. Deleting entries older than {Cutoff:yyyy-MM-dd}.", cutoff);

                    var deleted = await _trackingRepository.DeleteOlderThanAsync(cutoff, stoppingToken);

                    _logger.LogInformation("Tracking cleanup completed. Deleted {Count} entries.", deleted);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Tracking cleanup failed.");
                }

                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
    }
}
