using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Controllers;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.API.Controllers
{
    public class RedirectControllerTests
    {
        private readonly IRuleMatchingService _ruleMatchingService;
        private readonly IUrlTransformService _urlTransformService;
        private readonly IAppSettingsCacheService _settingsCache;
        private readonly ISmartSearchService _smartSearchService;
        private readonly RedirectController _sut;

        public RedirectControllerTests()
        {
            _ruleMatchingService = Substitute.For<IRuleMatchingService>();
            _urlTransformService = Substitute.For<IUrlTransformService>();
            _settingsCache = Substitute.For<IAppSettingsCacheService>();
            _smartSearchService = Substitute.For<ISmartSearchService>();
            var options = Options.Create(new BrootRedirectOptions());
            var logger = Substitute.For<ILogger<RedirectController>>();

            _sut = new RedirectController(
                _ruleMatchingService,
                _urlTransformService,
                _settingsCache,
                _smartSearchService,
                options,
                logger);

            _settingsCache.GetSettings().Returns(new AppSettings());
        }

        [Fact]
        public void Resolve_NullPath_ReturnsBadRequest()
        {
            var result = _sut.Resolve(null);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public void Resolve_EmptyPath_ReturnsBadRequest()
        {
            var result = _sut.Resolve("  ");

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public void Resolve_MatchFound_ReturnsOkWithResolvedUrl()
        {
            var rule = new RedirectRule { Matcher = "/old", Id = Guid.NewGuid() };

            var matchResult = new RuleMatchResult
            {
                Rule = rule,
                Score = 1000,
                Quality = 100,
                Level = MatchQualityLevel.Green
            };

            _ruleMatchingService
                .ResolveMatch(Arg.Any<string>(), Arg.Any<RuleMatchingConfig>())
                .Returns(matchResult);

            _urlTransformService
                .ResolveTargetUrl(Arg.Any<string>(), Arg.Any<RedirectRule>(), Arg.Any<string>())
                .Returns("https://new.com/page");

            var result = _sut.Resolve("/old") as OkObjectResult;

            result.Should().NotBeNull();

            var response = result!.Value as RedirectResolveResponse;

            response.Should().NotBeNull();
            response!.ResolvedUrl.Should().Be("https://new.com/page");
            response.IsSmartSearchFallback.Should().BeFalse();
        }

        [Fact]
        public void Resolve_NoMatchSmartSearch_ReturnsFallback()
        {
            _ruleMatchingService
                .ResolveMatch(Arg.Any<string>(), Arg.Any<RuleMatchingConfig>())
                .Returns((RuleMatchResult?)null);

            _settingsCache.GetSettings().Returns(new AppSettings
            {
                NoMatchBehavior = "SmartSearch"
            });

            _smartSearchService
                .BuildSearchUrl(Arg.Any<string>(), Arg.Any<AppSettings>())
                .Returns("https://search.com/q=test");

            var result = _sut.Resolve("/unknown") as OkObjectResult;

            result.Should().NotBeNull();

            var response = result!.Value as RedirectResolveResponse;

            response.Should().NotBeNull();
            response!.IsSmartSearchFallback.Should().BeTrue();
            response.FallbackSearchUrl.Should().Be("https://search.com/q=test");
        }

        [Fact]
        public void Resolve_NoMatchSmartSearchNullUrl_ReturnsNotFound()
        {
            _ruleMatchingService
                .ResolveMatch(Arg.Any<string>(), Arg.Any<RuleMatchingConfig>())
                .Returns((RuleMatchResult?)null);

            _settingsCache.GetSettings().Returns(new AppSettings
            {
                NoMatchBehavior = "SmartSearch"
            });

            _smartSearchService
                .BuildSearchUrl(Arg.Any<string>(), Arg.Any<AppSettings>())
                .Returns((string?)null);

            var result = _sut.Resolve("/unknown");

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public void Resolve_NoMatchDefaultBehavior_ReturnsNotFound()
        {
            _ruleMatchingService
                .ResolveMatch(Arg.Any<string>(), Arg.Any<RuleMatchingConfig>())
                .Returns((RuleMatchResult?)null);

            _settingsCache.GetSettings().Returns(new AppSettings
            {
                NoMatchBehavior = "RedirectToDefault"
            });

            var result = _sut.Resolve("/unknown");

            result.Should().BeOfType<NotFoundObjectResult>();
        }
    }
}
