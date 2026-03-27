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
    }
}
