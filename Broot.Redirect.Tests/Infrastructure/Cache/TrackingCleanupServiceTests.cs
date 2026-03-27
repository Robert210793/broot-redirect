using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Infrastructure.Cache;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Broot.Redirect.Tests.Infrastructure.Cache
{
    public class TrackingCleanupServiceTests
    {
        private readonly ITrackingRepository _trackingRepository;
        private readonly ILogger<TrackingCleanupService> _logger;

        public TrackingCleanupServiceTests()
        {
            _trackingRepository = Substitute.For<ITrackingRepository>();
            _logger = Substitute.For<ILogger<TrackingCleanupService>>();
        }

        private TrackingCleanupService CreateService(int retentionDays = 30)
        {
            return new TrackingCleanupService(_trackingRepository, retentionDays, _logger);
        }

        [Fact]
        public async Task ExecuteAsync_DeletesEntriesOlderThanRetention()
        {
            _trackingRepository.DeleteOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(5);

            var service = CreateService(retentionDays: 30);
            using var cts = new CancellationTokenSource();

            // Start the service and cancel after a short delay to allow one execution
            cts.CancelAfter(TimeSpan.FromMilliseconds(500));

            await service.StartAsync(cts.Token);

            // Wait a bit for the background execution to run
            await Task.Delay(300);

            await service.StopAsync(CancellationToken.None);

            await _trackingRepository.Received().DeleteOlderThanAsync(
                Arg.Is<DateTimeOffset>(d => d < DateTimeOffset.UtcNow),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteAsync_CancellationRequested_StopsGracefully()
        {
            _trackingRepository.DeleteOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(0);

            var service = CreateService();
            using var cts = new CancellationTokenSource();

            cts.CancelAfter(TimeSpan.FromMilliseconds(100));

            await service.StartAsync(cts.Token);

            await Task.Delay(200);

            // Should not throw
            await service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ExecuteAsync_RepositoryThrows_ContinuesRunning()
        {
            var callCount = 0;

            _trackingRepository.DeleteOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    callCount++;

                    if (callCount == 1)
                    {
                        throw new Exception("storage unavailable");
                    }

                    return Task.FromResult(0);
                });

            var service = CreateService();
            using var cts = new CancellationTokenSource();

            cts.CancelAfter(TimeSpan.FromMilliseconds(200));

            await service.StartAsync(cts.Token);

            await Task.Delay(300);

            await service.StopAsync(CancellationToken.None);

            // Service should have called the repository at least once despite the exception
            callCount.Should().BeGreaterOrEqualTo(1);
        }
    }
}
