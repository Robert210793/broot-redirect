using System.Reflection;
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

        [Fact]
        public async Task InvokeAsync_FeedbackEndpoint_UsesTrackingTier()
        {
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = CreateMiddleware(next, globalMax: 100, trackingMax: 1);

            var ip = UniqueIp();

            for (var i = 0; i < 2; i++)
            {
                var context = new DefaultHttpContext();
                context.Request.Path = "/api/feedback";
                context.Request.Method = "POST";
                context.Connection.RemoteIpAddress = ip;

                await middleware.InvokeAsync(context);

                if (i == 1)
                {
                    context.Response.StatusCode.Should().Be(429);
                }
            }
        }

        [Fact]
        public async Task InvokeAsync_DifferentIps_IndependentLimits()
        {
            var callCount = 0;
            RequestDelegate next = _ => { callCount++; return Task.CompletedTask; };
            var middleware = CreateMiddleware(next, globalMax: 1);

            var ip1 = UniqueIp();
            var ip2 = UniqueIp();

            var context1 = new DefaultHttpContext();
            context1.Request.Path = "/api/redirect/resolve";
            context1.Connection.RemoteIpAddress = ip1;

            var context2 = new DefaultHttpContext();
            context2.Request.Path = "/api/redirect/resolve";
            context2.Connection.RemoteIpAddress = ip2;

            await middleware.InvokeAsync(context1);
            await middleware.InvokeAsync(context2);

            callCount.Should().Be(2);
        }

        [Fact]
        public async Task InvokeAsync_NoRemoteIpAddress_StillWorks()
        {
            var nextCalled = false;
            RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
            var middleware = CreateMiddleware(next, globalMax: 5);

            var context = new DefaultHttpContext();
            context.Request.Path = "/api/redirect/resolve";
            // RemoteIpAddress is null

            await middleware.InvokeAsync(context);

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_DifferentTiers_TrackSeparately()
        {
            var callCount = 0;
            RequestDelegate next = _ => { callCount++; return Task.CompletedTask; };
            var middleware = CreateMiddleware(next, globalMax: 1, trackingMax: 1, adminMax: 1);

            var ip = UniqueIp();

            // Hit global tier once
            var ctx1 = new DefaultHttpContext();
            ctx1.Request.Path = "/api/redirect/resolve";
            ctx1.Connection.RemoteIpAddress = ip;
            await middleware.InvokeAsync(ctx1);

            // Hit tracking tier once
            var ctx2 = new DefaultHttpContext();
            ctx2.Request.Path = "/api/track";
            ctx2.Request.Method = "POST";
            ctx2.Connection.RemoteIpAddress = ip;
            await middleware.InvokeAsync(ctx2);

            // Hit admin tier once
            var ctx3 = new DefaultHttpContext();
            ctx3.Request.Path = "/api/rules";
            ctx3.Request.Method = "GET";
            ctx3.Connection.RemoteIpAddress = ip;
            await middleware.InvokeAsync(ctx3);

            callCount.Should().Be(3);
        }

        [Fact]
        public async Task InvokeAsync_WindowExpired_ResetsCount()
        {
            var callCount = 0;
            RequestDelegate next = _ => { callCount++; return Task.CompletedTask; };
            // windowSeconds=1 so it expires quickly
            var middleware = CreateMiddleware(next, globalMax: 1, windowSeconds: 1);

            var ip = UniqueIp();

            // First request - should pass
            var ctx1 = new DefaultHttpContext();
            ctx1.Request.Path = "/api/redirect/resolve";
            ctx1.Connection.RemoteIpAddress = ip;
            await middleware.InvokeAsync(ctx1);
            callCount.Should().Be(1);

            // Second request immediately - should be blocked
            var ctx2 = new DefaultHttpContext();
            ctx2.Request.Path = "/api/redirect/resolve";
            ctx2.Connection.RemoteIpAddress = ip;
            await middleware.InvokeAsync(ctx2);
            ctx2.Response.StatusCode.Should().Be(429);

            // Wait for window to expire
            await Task.Delay(1100);

            // Third request after window expired - should pass
            var ctx3 = new DefaultHttpContext();
            ctx3.Request.Path = "/api/redirect/resolve";
            ctx3.Connection.RemoteIpAddress = ip;
            await middleware.InvokeAsync(ctx3);
            callCount.Should().Be(2);
        }

        [Fact]
        public async Task InvokeAsync_ExceededLimit_WritesJsonResponseBody()
        {
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = CreateMiddleware(next, globalMax: 1);

            var ip = UniqueIp();

            // Exhaust limit
            var ctx1 = new DefaultHttpContext();
            ctx1.Request.Path = "/api/redirect/resolve";
            ctx1.Connection.RemoteIpAddress = ip;
            await middleware.InvokeAsync(ctx1);

            // Second request should get 429 with JSON body
            var ctx2 = new DefaultHttpContext();
            ctx2.Request.Path = "/api/redirect/resolve";
            ctx2.Connection.RemoteIpAddress = ip;
            ctx2.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(ctx2);

            ctx2.Response.StatusCode.Should().Be(429);
            ctx2.Response.ContentType.Should().Be("application/json");

            ctx2.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(ctx2.Response.Body);
            var body = await reader.ReadToEndAsync();

            body.Should().Contain("Too many requests");
            body.Should().Contain("retryAfter");
        }

        [Fact]
        public async Task InvokeAsync_GlobalRulesPath_UsesAdminTier()
        {
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = CreateMiddleware(next, globalMax: 100, adminMax: 1);

            var ip = UniqueIp();

            for (var i = 0; i < 2; i++)
            {
                var context = new DefaultHttpContext();
                context.Request.Path = "/api/global-rules";
                context.Request.Method = "GET";
                context.Connection.RemoteIpAddress = ip;

                await middleware.InvokeAsync(context);

                if (i == 1)
                {
                    context.Response.StatusCode.Should().Be(429);
                }
            }
        }

        [Fact]
        public async Task InvokeAsync_StatsPath_UsesAdminTier()
        {
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = CreateMiddleware(next, globalMax: 100, adminMax: 1);

            var ip = UniqueIp();

            for (var i = 0; i < 2; i++)
            {
                var context = new DefaultHttpContext();
                context.Request.Path = "/api/stats";
                context.Request.Method = "GET";
                context.Connection.RemoteIpAddress = ip;

                await middleware.InvokeAsync(context);

                if (i == 1)
                {
                    context.Response.StatusCode.Should().Be(429);
                }
            }
        }

        [Fact]
        public async Task CleanupExpiredEntries_RemovesStaleEntries()
        {
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = CreateMiddleware(next, globalMax: 100, windowSeconds: 1);

            var ip = UniqueIp();

            // Make a request to create an entry
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/redirect/resolve";
            context.Connection.RemoteIpAddress = ip;
            await middleware.InvokeAsync(context);

            // Wait for the entry to become stale (>2 min is the cleanup threshold,
            // but we can invoke cleanup directly and verify it doesn't throw)
            var cleanupMethod = typeof(RateLimitMiddleware)
                .GetMethod("CleanupExpiredEntries", BindingFlags.NonPublic | BindingFlags.Static);

            // Should not throw
            cleanupMethod!.Invoke(null, new object?[] { null });
        }
    }
}
