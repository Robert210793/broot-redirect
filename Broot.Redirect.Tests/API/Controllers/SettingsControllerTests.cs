using Broot.Redirect.API.Controllers;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.API.Controllers
{
    public class SettingsControllerTests
    {
        private readonly IAppSettingsCacheService _settingsCache;
        private readonly SettingsController _sut;

        public SettingsControllerTests()
        {
            _settingsCache = Substitute.For<IAppSettingsCacheService>();
            var logger = Substitute.For<ILogger<SettingsController>>();
            _sut = new SettingsController(_settingsCache, logger);

            _settingsCache.GetSettings().Returns(new AppSettings());

            _settingsCache
                .UpdateSettingsAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>())
                .Returns(callInfo => callInfo.Arg<AppSettings>());
        }

        [Fact]
        public void Get_ReturnsOkWithSettings()
        {
            var settings = new AppSettings { HeaderTitle = "Test Title" };

            _settingsCache.GetSettings().Returns(settings);

            var result = _sut.Get() as OkObjectResult;

            result.Should().NotBeNull();
            result!.Value.Should().Be(settings);
        }

        [Fact]
        public async Task Update_ValidRequest_ReturnsOk()
        {
            var request = new UpdateSettingsRequest
            {
                NoMatchBehavior = "SmartSearch",
                PopupMode = "inline"
            };

            var result = await _sut.Update(request) as OkObjectResult;

            result.Should().NotBeNull();

            await _settingsCache.Received(1)
                .UpdateSettingsAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Update_InvalidNoMatchBehavior_ReturnsBadRequest()
        {
            var request = new UpdateSettingsRequest { NoMatchBehavior = "InvalidValue" };

            var result = await _sut.Update(request);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_InvalidPopupMode_ReturnsBadRequest()
        {
            var request = new UpdateSettingsRequest { PopupMode = "wrong" };

            var result = await _sut.Update(request);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_NullFields_MergesWithExisting()
        {
            var existing = new AppSettings
            {
                HeaderTitle = "Existing Title",
                DefaultNewDomain = "https://existing.com"
            };

            _settingsCache.GetSettings().Returns(existing);

            var request = new UpdateSettingsRequest { HeaderTitle = "New Title" };

            await _sut.Update(request);

            await _settingsCache.Received(1).UpdateSettingsAsync(
                Arg.Is<AppSettings>(s =>
                    s.HeaderTitle == "New Title" &&
                    s.DefaultNewDomain == "https://existing.com"),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData("RedirectToDefault")]
        [InlineData("SmartSearch")]
        [InlineData("Return404")]
        public async Task Update_AllValidBehaviors_Accepted(string behavior)
        {
            var request = new UpdateSettingsRequest { NoMatchBehavior = behavior };

            var result = await _sut.Update(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Theory]
        [InlineData("inline")]
        [InlineData("active")]
        public async Task Update_BothPopupModes_Accepted(string mode)
        {
            var request = new UpdateSettingsRequest { PopupMode = mode };

            var result = await _sut.Update(request);

            result.Should().BeOfType<OkObjectResult>();
        }
    }
}
