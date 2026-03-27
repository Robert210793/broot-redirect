using Broot.Redirect.API.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.API.Middleware
{
    public class CsrfProtectionMiddlewareTests
    {
        private async Task<(bool NextCalled, DefaultHttpContext Context)> Invoke(
            string path,
            string method = "POST",
            string? origin = null,
            string? referer = null,
            string host = "localhost")
        {
            var nextCalled = false;

            RequestDelegate next = _ =>
            {
                nextCalled = true;

                return Task.CompletedTask;
            };

            var logger = Substitute.For<ILogger<CsrfProtectionMiddleware>>();
            var middleware = new CsrfProtectionMiddleware(next, logger);

            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Request.Method = method;
            context.Request.Host = new HostString(host);

            if (origin != null)
            {
                context.Request.Headers.Origin = origin;
            }

            if (referer != null)
            {
                context.Request.Headers.Referer = referer;
            }

            await middleware.InvokeAsync(context);

            return (nextCalled, context);
        }

        [Fact]
        public async Task InvokeAsync_NonApiPath_SkipsCheck()
        {
            var (nextCalled, _) = await Invoke("/some/page", "POST");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_GetRequest_SkipsCheck()
        {
            var (nextCalled, _) = await Invoke("/api/rules", "GET");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_HeadRequest_SkipsCheck()
        {
            var (nextCalled, _) = await Invoke("/api/rules", "HEAD");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_OptionsRequest_SkipsCheck()
        {
            var (nextCalled, _) = await Invoke("/api/rules", "OPTIONS");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_ValidOrigin_CallsNext()
        {
            var (nextCalled, _) = await Invoke(
                "/api/rules",
                "POST",
                origin: "http://localhost",
                host: "localhost");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_InvalidOrigin_Returns403()
        {
            var (nextCalled, context) = await Invoke(
                "/api/rules",
                "POST",
                origin: "http://evil.com",
                host: "localhost");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task InvokeAsync_NullOrigin_Returns403()
        {
            var (nextCalled, context) = await Invoke(
                "/api/rules",
                "POST",
                origin: "null",
                host: "localhost");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task InvokeAsync_ValidReferer_CallsNext()
        {
            var (nextCalled, _) = await Invoke(
                "/api/rules",
                "POST",
                referer: "http://localhost/admin",
                host: "localhost");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_InvalidReferer_Returns403()
        {
            var (nextCalled, context) = await Invoke(
                "/api/rules",
                "POST",
                referer: "http://evil.com/page",
                host: "localhost");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task InvokeAsync_NoOriginOrReferer_Returns403()
        {
            var (nextCalled, context) = await Invoke("/api/rules", "POST");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(403);
        }
    }
}
