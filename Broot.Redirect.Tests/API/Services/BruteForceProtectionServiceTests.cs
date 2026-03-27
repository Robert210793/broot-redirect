using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.API.Services
{
    public class BruteForceProtectionServiceTests : IDisposable
    {
        private readonly BruteForceProtectionService _sut;

        public BruteForceProtectionServiceTests()
        {
            var options = Options.Create(new BrootRedirectOptions
            {
                LoginMaxAttempts = 3,
                LoginBlockDurationMinutes = 1
            });

            var logger = Substitute.For<ILogger<BruteForceProtectionService>>();
            _sut = new BruteForceProtectionService(options, logger);
        }

        public void Dispose()
        {
            _sut.Dispose();
        }

        [Fact]
        public void IsBlocked_UnknownIp_ReturnsFalse()
        {
            var result = _sut.IsBlocked("192.168.1.1");

            result.Should().BeFalse();
        }

        [Fact]
        public void IsBlocked_AfterMaxAttempts_ReturnsTrue()
        {
            var ipAddress = "10.0.0.1";

            _sut.RecordFailure(ipAddress);
            _sut.RecordFailure(ipAddress);
            _sut.RecordFailure(ipAddress);

            var result = _sut.IsBlocked(ipAddress);

            result.Should().BeTrue();
        }

        [Fact]
        public void IsBlocked_BelowMaxAttempts_ReturnsFalse()
        {
            var ipAddress = "10.0.0.2";

            _sut.RecordFailure(ipAddress);
            _sut.RecordFailure(ipAddress);

            var result = _sut.IsBlocked(ipAddress);

            result.Should().BeFalse();
        }

        [Fact]
        public void ResetAttempts_ClearsBlock()
        {
            var ipAddress = "10.0.0.3";

            _sut.RecordFailure(ipAddress);
            _sut.RecordFailure(ipAddress);
            _sut.RecordFailure(ipAddress);
            _sut.IsBlocked(ipAddress).Should().BeTrue();

            _sut.ResetAttempts(ipAddress);

            _sut.IsBlocked(ipAddress).Should().BeFalse();
        }

        [Fact]
        public void BlockIp_ManuallyBlocksIp()
        {
            var ipAddress = "10.0.0.4";

            _sut.BlockIp(ipAddress);

            _sut.IsBlocked(ipAddress).Should().BeTrue();
        }

        [Fact]
        public void UnblockIp_UnblocksManuallyBlockedIp()
        {
            var ipAddress = "10.0.0.5";

            _sut.BlockIp(ipAddress);
            _sut.IsBlocked(ipAddress).Should().BeTrue();

            _sut.UnblockIp(ipAddress);

            _sut.IsBlocked(ipAddress).Should().BeFalse();
        }

        [Fact]
        public void ClearAll_ClearsEverything()
        {
            _sut.BlockIp("10.0.0.6");
            _sut.BlockIp("10.0.0.7");
            _sut.GetBlockedIps().Should().HaveCount(2);

            _sut.ClearAll();

            _sut.GetBlockedIps().Should().BeEmpty();
        }

        [Fact]
        public void GetBlockedIps_ReturnsOnlyBlockedIps()
        {
            _sut.RecordFailure("10.0.0.8");
            _sut.RecordFailure("10.0.0.8");
            _sut.RecordFailure("10.0.0.8");

            _sut.RecordFailure("10.0.0.9");

            var blocked = _sut.GetBlockedIps();

            blocked.Should().ContainSingle();
            blocked[0].Ip.Should().Be("10.0.0.8");
        }

        [Fact]
        public void IsBlocked_ExpiredBlock_ReturnsFalse()
        {
            // Use very short block duration
            var options = Options.Create(new BrootRedirectOptions
            {
                LoginMaxAttempts = 1,
                LoginBlockDurationMinutes = 0 // 0 minutes = immediate expiry
            });

            var logger = Substitute.For<ILogger<BruteForceProtectionService>>();

            using var service = new BruteForceProtectionService(options, logger);

            service.RecordFailure("10.0.0.50");

            // The block duration is 0 minutes, so it should expire immediately
            // Need to wait just a tick for DateTimeOffset.UtcNow to advance
            Thread.Sleep(10);

            service.IsBlocked("10.0.0.50").Should().BeFalse();
        }

        [Fact]
        public void IsBlocked_NotBlockedButHasAttempts_ReturnsFalse()
        {
            _sut.RecordFailure("10.0.0.51");
            _sut.RecordFailure("10.0.0.51");
            // 2 attempts, threshold is 3 -> not blocked

            _sut.IsBlocked("10.0.0.51").Should().BeFalse();
        }

        [Fact]
        public void GetBlockedIps_IncludesAttemptsAndBlockedUntil()
        {
            _sut.RecordFailure("10.0.0.52");
            _sut.RecordFailure("10.0.0.52");
            _sut.RecordFailure("10.0.0.52");

            var blocked = _sut.GetBlockedIps();

            blocked.Should().ContainSingle();
            blocked[0].Attempts.Should().Be(3);
            blocked[0].BlockedUntil.Should().BeAfter(DateTimeOffset.UtcNow);
        }

        [Fact]
        public void RecordFailure_ExactlyAtThreshold_Blocks()
        {
            _sut.RecordFailure("10.0.0.53");
            _sut.RecordFailure("10.0.0.53");

            _sut.IsBlocked("10.0.0.53").Should().BeFalse();

            _sut.RecordFailure("10.0.0.53");

            _sut.IsBlocked("10.0.0.53").Should().BeTrue();
        }

        [Fact]
        public void BlockIp_AlreadyBlocked_UpdatesBlockTime()
        {
            _sut.BlockIp("10.0.0.54");

            var firstBlock = _sut.GetBlockedIps();
            var firstBlockTime = firstBlock[0].BlockedUntil;

            Thread.Sleep(10);

            _sut.BlockIp("10.0.0.54");

            var secondBlock = _sut.GetBlockedIps();
            secondBlock[0].BlockedUntil.Should().BeOnOrAfter(firstBlockTime);
        }

        [Fact]
        public void UnblockIp_NonExistent_DoesNotThrow()
        {
            var act = () => _sut.UnblockIp("10.0.0.55");

            act.Should().NotThrow();
        }

        [Fact]
        public void ClearAll_AfterFailures_ClearsAttemptsToo()
        {
            _sut.RecordFailure("10.0.0.56");
            _sut.RecordFailure("10.0.0.56");

            _sut.ClearAll();

            // After clearing, new failures should start from 0
            _sut.RecordFailure("10.0.0.56");
            _sut.RecordFailure("10.0.0.56");

            _sut.IsBlocked("10.0.0.56").Should().BeFalse();
        }

        [Fact]
        public void CleanupExpiredEntries_RemovesExpiredBlocks()
        {
            // Use 0-minute block duration so blocks expire immediately
            var options = Options.Create(new BrootRedirectOptions
            {
                LoginMaxAttempts = 1,
                LoginBlockDurationMinutes = 0
            });

            var logger = Substitute.For<ILogger<BruteForceProtectionService>>();

            using var service = new BruteForceProtectionService(options, logger);

            service.RecordFailure("10.0.0.60");
            // Block is already expired (0 minutes)

            Thread.Sleep(10);

            // Invoke the private cleanup method via reflection
            var cleanupMethod = typeof(BruteForceProtectionService)
                .GetMethod("CleanupExpiredEntries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            cleanupMethod!.Invoke(service, new object?[] { null });

            // After cleanup, the IP should no longer be in the dictionary
            service.GetBlockedIps().Should().BeEmpty();
            service.IsBlocked("10.0.0.60").Should().BeFalse();
        }
    }
}
