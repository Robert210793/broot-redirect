using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Middleware;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Broot.Redirect.Tests.API.Middleware
{
    public class RedirectMiddlewareTests
    {
        private readonly IRuleMatchingService _ruleMatchingService;
        private readonly IUrlTransformService _urlTransformService;
        private readonly IAppSettingsCacheService _settingsCache;
        private readonly ISmartSearchService _smartSearchService;
        private readonly ITrackingRepository _trackingRepository;
        private readonly IOptions<BrootRedirectOptions> _options;

        public RedirectMiddlewareTests()
        {
            _ruleMatchingService = Substitute.For<IRuleMatchingService>();
            _urlTransformService = Substitute.For<IUrlTransformService>();
            _settingsCache = Substitute.For<IAppSettingsCacheService>();
            _smartSearchService = Substitute.For<ISmartSearchService>();
            _trackingRepository = Substitute.For<ITrackingRepository>();
            _options = Options.Create(new BrootRedirectOptions());

            _settingsCache.GetSettings().Returns(new AppSettings());

            _trackingRepository
                .CreateAsync(Arg.Any<TrackingEntry>(), Arg.Any<CancellationToken>())
                .Returns(Guid.NewGuid());
        }

        private async Task<(bool NextCalled, DefaultHttpContext Context)> InvokeMiddleware(
            string path,
            string queryString = "")
        {
            var nextCalled = false;

            RequestDelegate next = _ =>
            {
                nextCalled = true;

                return Task.CompletedTask;
            };

            var logger = Substitute.For<ILogger<RedirectMiddleware>>();
            var middleware = new RedirectMiddleware(next, logger);

            var context = new DefaultHttpContext();
            context.Request.Path = path;

            if (!string.IsNullOrEmpty(queryString))
            {
                context.Request.QueryString = new QueryString(queryString);
            }

            context.Request.Scheme = "https";
            context.Request.Host = new HostString("example.com");

            await middleware.InvokeAsync(
                context,
                _ruleMatchingService,
                _urlTransformService,
                _settingsCache,
                _smartSearchService,
                _trackingRepository,
                _options);

            return (nextCalled, context);
        }

        [Fact]
        public async Task InvokeAsync_ApiPath_SkipsToNext()
        {
            var (nextCalled, context) = await InvokeMiddleware("/api/health");

            nextCalled.Should().BeTrue();

            _ruleMatchingService
                .DidNotReceive()
                .ResolveMatch(Arg.Any<string>(), Arg.Any<RuleMatchingConfig>());
        }

        [Fact]
        public async Task InvokeAsync_LoginPath_SkipsToNext()
        {
            var (nextCalled, context) = await InvokeMiddleware("/login");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_RootPath_SkipsToNext()
        {
            var (nextCalled, context) = await InvokeMiddleware("/");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_MatchWithAutoRedirect_Returns302()
        {
            var rule = new RedirectRule { Matcher = "/old-page", AutoRedirect = true };

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

            _settingsCache.GetSettings().Returns(new AppSettings { AutoRedirect = true });

            var (nextCalled, context) = await InvokeMiddleware("/old-page");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(302);
            context.Response.Headers.Location.ToString().Should().Be("https://new.com/page");
        }

        [Fact]
        public async Task InvokeAsync_MatchWithAutoRedirectFalse_FallsThrough()
        {
            var rule = new RedirectRule { Matcher = "/old-page", AutoRedirect = false };

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

            _settingsCache.GetSettings().Returns(new AppSettings { AutoRedirect = true });

            var (nextCalled, context) = await InvokeMiddleware("/old-page");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_MatchButGlobalAutoRedirectFalse_FallsThrough()
        {
            var rule = new RedirectRule { Matcher = "/old-page", AutoRedirect = true };

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

            _settingsCache.GetSettings().Returns(new AppSettings { AutoRedirect = false });

            var (nextCalled, context) = await InvokeMiddleware("/old-page");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_NoMatchRedirectToDefault_Returns302ToDefault()
        {
            _ruleMatchingService
                .ResolveMatch(Arg.Any<string>(), Arg.Any<RuleMatchingConfig>())
                .Returns((RuleMatchResult?)null);

            _settingsCache.GetSettings().Returns(new AppSettings
            {
                NoMatchBehavior = "RedirectToDefault",
                DefaultNewDomain = "https://default.example.com"
            });

            var (nextCalled, context) = await InvokeMiddleware("/unknown-path");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(302);
            context.Response.Headers.Location.ToString().Should().Be("https://default.example.com");
        }

        [Fact]
        public async Task InvokeAsync_NoMatchSmartSearch_Returns302ToSearchUrl()
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
                .Returns("https://search.example.com/q=test");

            var (nextCalled, context) = await InvokeMiddleware("/some/path");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(302);
            context.Response.Headers.Location.ToString().Should().Be("https://search.example.com/q=test");
        }

        [Fact]
        public async Task InvokeAsync_NoMatchSmartSearchNullUrl_FallsThrough()
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

            var (nextCalled, context) = await InvokeMiddleware("/some/path");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_AutoRedirect_WritesTrackingEntry()
        {
            var rule = new RedirectRule { Matcher = "/tracked", AutoRedirect = true };

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
                .Returns("https://new.com/tracked");

            _settingsCache.GetSettings().Returns(new AppSettings { AutoRedirect = true });

            await InvokeMiddleware("/tracked");

            await _trackingRepository
                .Received(1)
                .CreateAsync(Arg.Any<TrackingEntry>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task InvokeAsync_TrackingFailure_StillRedirects()
        {
            var rule = new RedirectRule { Matcher = "/tracked", AutoRedirect = true };

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

            _settingsCache.GetSettings().Returns(new AppSettings { AutoRedirect = true });

            _trackingRepository
                .CreateAsync(Arg.Any<TrackingEntry>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new Exception("Storage error"));

            var (nextCalled, context) = await InvokeMiddleware("/tracked");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(302);
            context.Response.Headers.Location.ToString().Should().Be("https://new.com/page");
        }
    }
}
