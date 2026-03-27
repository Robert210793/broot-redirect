using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Middleware;
using Broot.Redirect.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.API.Middleware
{
    public class AdminSessionMiddlewareTests
    {
        private async Task<(bool NextCalled, DefaultHttpContext Context)> Invoke(
            string path,
            string method = "GET",
            bool authenticated = false)
        {
            var nextCalled = false;

            RequestDelegate next = _ =>
            {
                nextCalled = true;

                return Task.CompletedTask;
            };

            var logger = Substitute.For<ILogger<AdminSessionMiddleware>>();
            var middleware = new AdminSessionMiddleware(next, logger);

            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Request.Method = method;
            context.InstallMockSession();

            if (authenticated)
            {
                context.Session.SetString(SessionKeys.IsAdminAuthenticated, "true");
            }

            await middleware.InvokeAsync(context);

            return (nextCalled, context);
        }

        [Fact]
        public async Task InvokeAsync_NonProtectedPath_CallsNext()
        {
            var (nextCalled, _) = await Invoke("/api/health");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_NonApiPath_CallsNext()
        {
            var (nextCalled, _) = await Invoke("/some/page");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_RulesPath_UnauthReturns403()
        {
            var (nextCalled, context) = await Invoke("/api/rules");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task InvokeAsync_GlobalRulesPath_UnauthReturns403()
        {
            var (nextCalled, context) = await Invoke("/api/global-rules");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task InvokeAsync_StatsPath_UnauthReturns403()
        {
            var (nextCalled, context) = await Invoke("/api/stats");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task InvokeAsync_SettingsPut_RequiresAuth()
        {
            var (nextCalled, context) = await Invoke("/api/settings", "PUT");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task InvokeAsync_SettingsGet_DoesNotRequireAuth()
        {
            var (nextCalled, _) = await Invoke("/api/settings", "GET");

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_AuthStatus_RequiresAuth()
        {
            var (nextCalled, context) = await Invoke("/api/auth/status");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task InvokeAsync_AuthLogout_RequiresAuth()
        {
            var (nextCalled, context) = await Invoke("/api/auth/logout");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task InvokeAsync_BlockedIps_RequiresAuth()
        {
            var (nextCalled, context) = await Invoke("/api/auth/blocked-ips");

            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task InvokeAsync_Authenticated_CallsNext()
        {
            var (nextCalled, _) = await Invoke("/api/rules", "GET", authenticated: true);

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_AuthLoginPath_DoesNotRequireAuth()
        {
            var (nextCalled, _) = await Invoke("/api/auth/login", "POST");

            nextCalled.Should().BeTrue();
        }
    }
}
