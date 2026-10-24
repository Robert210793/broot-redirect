using Broot.Redirect.API.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Broot.Redirect.Tests.API.Configuration
{
    public class TeamsEmbeddingTests
    {
        [Fact]
        public void ConfigureSessionCookie_EmbeddingEnabled_UsesSameSiteNoneSecureAndPartitioned()
        {
            var cookie = new CookieBuilder();

            TeamsEmbedding.ConfigureSessionCookie(cookie, allowTeamsEmbedding: true, isProduction: false);

            cookie.SameSite.Should().Be(SameSiteMode.None);
            cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
            cookie.Extensions.Should().Contain(TeamsEmbedding.PartitionedCookieAttribute);
        }

        [Fact]
        public void ConfigureSessionCookie_EmbeddingEnabled_WritesSameSiteNoneSecurePartitionedCookie()
        {
            var cookie = new CookieBuilder { Name = "admin_session" };
            var context = new DefaultHttpContext();

            TeamsEmbedding.ConfigureSessionCookie(cookie, allowTeamsEmbedding: true, isProduction: true);
            context.Response.Cookies.Append(cookie.Name, "value", cookie.Build(context));

            var setCookie = context.Response.Headers.SetCookie.ToString().ToLowerInvariant();
            setCookie.Should().Contain("samesite=none");
            setCookie.Should().Contain("secure");
            setCookie.Should().Contain("partitioned");
        }

        [Theory]
        [InlineData(true, CookieSecurePolicy.Always)]
        [InlineData(false, CookieSecurePolicy.SameAsRequest)]
        public void ConfigureSessionCookie_EmbeddingDisabled_KeepsLaxCookie(bool isProduction, CookieSecurePolicy expectedPolicy)
        {
            var cookie = new CookieBuilder();

            TeamsEmbedding.ConfigureSessionCookie(cookie, allowTeamsEmbedding: false, isProduction);

            cookie.SameSite.Should().Be(SameSiteMode.Lax);
            cookie.SecurePolicy.Should().Be(expectedPolicy);
            cookie.Extensions.Should().NotContain(TeamsEmbedding.PartitionedCookieAttribute);
        }

        [Fact]
        public void ApplyFrameAncestors_SetsPolicyAllowingSelfAndTeams()
        {
            var context = new DefaultHttpContext();

            TeamsEmbedding.ApplyFrameAncestors(context.Response.Headers);

            var policy = context.Response.Headers.ContentSecurityPolicy.ToString();
            policy.Should().StartWith("frame-ancestors 'self'");
            policy.Should().Contain("https://teams.microsoft.com");
            policy.Should().Contain("https://*.teams.microsoft.com");
            policy.Should().Contain("https://*.cloud.microsoft");
        }

        [Fact]
        public void AllowTeamsEmbedding_IsDisabledByDefault()
        {
            new BrootRedirectOptions().AllowTeamsEmbedding.Should().BeFalse();
        }
    }
}
