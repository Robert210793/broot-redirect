using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Net;
using Xunit;

namespace Broot.Redirect.Tests.API.Middleware
{
    public class RateLimitMiddlewareTests
    {
        private static int _ipCounter = 100;

        private static IPAddress UniqueIp()
        {
            var counter = Interlocked.Increment(ref _ipCounter);

            return IPAddress.Parse($"10.0.{counter / 256}.{counter % 256}");
        }

        private static RateLimitMiddleware CreateMiddleware(
            RequestDelegate next,
            int globalMax = 5,
            int trackingMax = 5,
            int adminMax = 3,
            int windowSeconds = 60)
        {
            var options = Options.Create(new BrootRedirectOptions
            {
                RateLimitGlobalMax = globalMax,
                RateLimitTrackingMax = trackingMax,
                RateLimitAdminMax = adminMax,
                RateLimitWindowSeconds = windowSeconds
            });

            var logger = Substitute.For<ILogger<RateLimitMiddleware>>();

            return new RateLimitMiddleware(next, options, logger);
        }

        [Fact]
        public async Task InvokeAsync_NonApiPath_SkipsRateLimit()
        {
            var nextCalled = false;
            RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
            var middleware = CreateMiddleware(next);

            var context = new DefaultHttpContext();
            context.Request.Path = "/some/page";
            context.Connection.RemoteIpAddress = UniqueIp();

            await middleware.InvokeAsync(context);

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_WithinLimit_CallsNext()
        {
            var nextCalled = false;
            RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
            var middleware = CreateMiddleware(next, globalMax: 5);

            var context = new DefaultHttpContext();
            context.Request.Path = "/api/redirect/resolve";
            context.Connection.RemoteIpAddress = UniqueIp();

            await middleware.InvokeAsync(context);

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_ExceedsLimit_Returns429()
        {
            var callCount = 0;
            RequestDelegate next = _ => { callCount++; return Task.CompletedTask; };
            var middleware = CreateMiddleware(next, globalMax: 2);

            var ip = UniqueIp();

            for (var i = 0; i < 3; i++)
            {
                var context = new DefaultHttpContext();
                context.Request.Path = "/api/redirect/resolve";
                context.Connection.RemoteIpAddress = ip;

                await middleware.InvokeAsync(context);

                if (i == 2)
                {
                    context.Response.StatusCode.Should().Be(429);
                }
            }

            callCount.Should().Be(2);
        }

        [Fact]
        public async Task InvokeAsync_SetsRateLimitHeaders()
        {
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = CreateMiddleware(next, globalMax: 10);

            var context = new DefaultHttpContext();
            context.Request.Path = "/api/redirect/resolve";
            context.Connection.RemoteIpAddress = UniqueIp();

            await middleware.InvokeAsync(context);

            context.Response.Headers["X-RateLimit-Limit"].ToString().Should().Be("10");
            context.Response.Headers["X-RateLimit-Remaining"].ToString().Should().Be("9");
            context.Response.Headers["X-RateLimit-Reset"].ToString().Should().NotBeEmpty();
        }

        [Fact]
        public async Task InvokeAsync_TrackingTier_UsesTrackingLimit()
        {
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = CreateMiddleware(next, globalMax: 100, trackingMax: 3);

            var ip = UniqueIp();

            for (var i = 0; i < 4; i++)
            {
                var context = new DefaultHttpContext();
                context.Request.Path = "/api/track";
                context.Request.Method = "POST";
                context.Connection.RemoteIpAddress = ip;

                await middleware.InvokeAsync(context);

                if (i == 3)
                {
                    context.Response.StatusCode.Should().Be(429);
                }
            }
        }

        [Fact]
        public async Task InvokeAsync_AdminTier_UsesAdminLimit()
        {
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = CreateMiddleware(next, globalMax: 100, adminMax: 2);

            var ip = UniqueIp();

            for (var i = 0; i < 3; i++)
            {
                var context = new DefaultHttpContext();
                context.Request.Path = "/api/rules";
                context.Request.Method = "GET";
                context.Connection.RemoteIpAddress = ip;

                await middleware.InvokeAsync(context);

                if (i == 2)
                {
                    context.Response.StatusCode.Should().Be(429);
                }
            }
        }

        [Fact]
        public async Task InvokeAsync_SettingsPut_UsesAdminTier()
        {
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = CreateMiddleware(next, globalMax: 100, adminMax: 2);

            var ip = UniqueIp();

            for (var i = 0; i < 3; i++)
            {
                var context = new DefaultHttpContext();
                context.Request.Path = "/api/settings";
                context.Request.Method = "PUT";
                context.Connection.RemoteIpAddress = ip;

                await middleware.InvokeAsync(context);

                if (i == 2)
                {
                    context.Response.StatusCode.Should().Be(429);
                }
            }
        }

        [Fact]
        public async Task InvokeAsync_ExceededLimit_SetsRetryAfterHeader()
        {
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = CreateMiddleware(next, globalMax: 1);

            var ip = UniqueIp();

            for (var i = 0; i < 2; i++)
            {
                var context = new DefaultHttpContext();
                context.Request.Path = "/api/redirect/resolve";
                context.Connection.RemoteIpAddress = ip;

                await middleware.InvokeAsync(context);

                if (i == 1)
                {
                    context.Response.Headers.RetryAfter.ToString().Should().NotBeEmpty();
                }
            }
        }
    }
}
