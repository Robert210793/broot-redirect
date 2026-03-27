using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Broot.Redirect.Infrastructure.Cache;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.Infrastructure.Cache
{
    public class AppSettingsCacheServiceTests
    {
        private readonly IAppSettingsRepository _repository;
        private readonly AppSettingsCacheService _sut;

        public AppSettingsCacheServiceTests()
        {
            _repository = Substitute.For<IAppSettingsRepository>();
            var logger = Substitute.For<ILogger<AppSettingsCacheService>>();

            _sut = new AppSettingsCacheService(_repository, logger);
        }

        [Fact]
        public void IsWarmedUp_FalseBeforeInit_TrueAfter()
        {
            _sut.IsWarmedUp.Should().BeFalse();

            _sut.Initialize(new AppSettings());

            _sut.IsWarmedUp.Should().BeTrue();
        }

        [Fact]
        public void GetSettings_ReturnsDefaultBeforeInit()
        {
            var settings = _sut.GetSettings();

            settings.Should().NotBeNull();
        }

        [Fact]
        public void Initialize_StoresSettings()
        {
            var settings = new AppSettings { HeaderTitle = "Initialized" };

            _sut.Initialize(settings);

            _sut.GetSettings().HeaderTitle.Should().Be("Initialized");
        }

        [Fact]
        public async Task UpdateSettingsAsync_SavesAndCaches()
        {
            _sut.Initialize(new AppSettings());

            var updated = new AppSettings { HeaderTitle = "Updated" };

            var result = await _sut.UpdateSettingsAsync(updated);

            result.HeaderTitle.Should().Be("Updated");
            _sut.GetSettings().HeaderTitle.Should().Be("Updated");

            await _repository.Received(1).SaveAsync(updated, Arg.Any<CancellationToken>());
        }
    }
}
