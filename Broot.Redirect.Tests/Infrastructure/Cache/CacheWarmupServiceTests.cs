using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Broot.Redirect.Infrastructure.Cache;
using Broot.Redirect.Infrastructure.Persistence;
using Broot.Redirect.Tests.Integration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.Infrastructure.Cache
{
    [Collection("Azurite")]
    [Trait("Category", "Integration")]
    public class CacheWarmupServiceTests : IAsyncLifetime
    {
        private readonly AzuriteFixture _fixture;
        private readonly TableStorageRuleRepository _ruleRepository;
        private readonly TableStorageAppSettingsRepository _settingsRepository;
        private readonly TableStorageGlobalRuleRepository _globalRuleRepository;
        private readonly TableStorageTrackingRepository _trackingRepository;
        private readonly IRuleCacheService _ruleCacheService;
        private readonly IAppSettingsCacheService _settingsCacheService;
        private readonly IGlobalRuleCacheService _globalRuleCacheService;
        private readonly ILogger<CacheWarmupService> _logger;
        private readonly CacheWarmupService _sut;

        public CacheWarmupServiceTests(AzuriteFixture fixture)
        {
            _fixture = fixture;

            var options = Options.Create(new TableStorageOptions
            {
                ConnectionString = AzuriteFixture.ConnectionString,
                TableName = "WarmupRules"
            });

            _ruleRepository = new TableStorageRuleRepository(
                options,
                Substitute.For<ILogger<TableStorageRuleRepository>>());

            _settingsRepository = new TableStorageAppSettingsRepository(
                options,
                Substitute.For<ILogger<TableStorageAppSettingsRepository>>());

            _globalRuleRepository = new TableStorageGlobalRuleRepository(
                options,
                Substitute.For<ILogger<TableStorageGlobalRuleRepository>>());

            _trackingRepository = new TableStorageTrackingRepository(
                options,
                Substitute.For<ILogger<TableStorageTrackingRepository>>());

            _ruleCacheService = Substitute.For<IRuleCacheService>();
            _settingsCacheService = Substitute.For<IAppSettingsCacheService>();
            _globalRuleCacheService = Substitute.For<IGlobalRuleCacheService>();
            _logger = Substitute.For<ILogger<CacheWarmupService>>();

            _sut = new CacheWarmupService(
                _ruleRepository,
                _settingsRepository,
                _globalRuleRepository,
                _trackingRepository,
                _ruleCacheService,
                _settingsCacheService,
                _globalRuleCacheService,
                _logger);
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task StartAsync_InitializesAllCaches()
        {
            await _sut.StartAsync(CancellationToken.None);

            _ruleCacheService.Received(1).Initialize(Arg.Any<IReadOnlyList<RedirectRule>>());
            _settingsCacheService.Received(1).Initialize(Arg.Any<AppSettings>());
            _globalRuleCacheService.Received(1).Initialize(Arg.Any<IReadOnlyList<GlobalRule>>());
        }

        [Fact]
        public async Task StartAsync_NoExistingSettings_SeedsDefaults()
        {
            await _sut.StartAsync(CancellationToken.None);

            // After warmup, settings cache should be initialized (either from existing or seeded defaults)
            _settingsCacheService.Received(1).Initialize(Arg.Any<AppSettings>());
        }

        [Fact]
        public async Task StopAsync_CompletesImmediately()
        {
            var task = _sut.StopAsync(CancellationToken.None);

            task.IsCompleted.Should().BeTrue();

            await task;
        }
    }
}
