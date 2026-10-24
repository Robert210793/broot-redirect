using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Filters;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.API.Filters
{
    public class QmProApiKeyFilterTests
    {
        private const string ConfiguredKey = "k3y-with-sufficient-entropy-0123456789abcdef";

        private readonly ILogger<QmProApiKeyFilter> _logger = Substitute.For<ILogger<QmProApiKeyFilter>>();

        private QmProApiKeyFilter CreateFilter(string? configuredKey = ConfiguredKey)
        {
            return new QmProApiKeyFilter(
                Options.Create(new BrootRedirectOptions { QmProApiKey = configuredKey }),
                _logger);
        }

        private static AuthorizationFilterContext CreateContext(params string[] apiKeys)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "POST";
            httpContext.Request.Path = "/api/qm-pro/publish";

            if (apiKeys.Length > 0)
            {
                httpContext.Request.Headers[QmProApiKeyFilter.HeaderName] = apiKeys;
            }

            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
        }

        private static void ShouldBeUnauthorized(AuthorizationFilterContext context)
        {
            var result = context.Result as ObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        [Fact]
        public void OnAuthorization_CorrectKey_AllowsRequest()
        {
            var context = CreateContext(ConfiguredKey);

            CreateFilter().OnAuthorization(context);

            context.Result.Should().BeNull();
        }

        [Fact]
        public void OnAuthorization_WrongKey_Returns401()
        {
            var context = CreateContext("wrong-key");

            CreateFilter().OnAuthorization(context);

            ShouldBeUnauthorized(context);
        }

        [Fact]
        public void OnAuthorization_KeyWithDifferentCase_Returns401()
        {
            var context = CreateContext(ConfiguredKey.ToUpperInvariant());

            CreateFilter().OnAuthorization(context);

            ShouldBeUnauthorized(context);
        }

        [Fact]
        public void OnAuthorization_MissingHeader_Returns401()
        {
            var context = CreateContext();

            CreateFilter().OnAuthorization(context);

            ShouldBeUnauthorized(context);
        }

        [Fact]
        public void OnAuthorization_EmptyHeader_Returns401()
        {
            var context = CreateContext(string.Empty);

            CreateFilter().OnAuthorization(context);

            ShouldBeUnauthorized(context);
        }

        [Fact]
        public void OnAuthorization_MultipleHeaderValues_Returns401()
        {
            var context = CreateContext(ConfiguredKey, ConfiguredKey);

            CreateFilter().OnAuthorization(context);

            ShouldBeUnauthorized(context);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void OnAuthorization_NoKeyConfigured_RejectsEveryRequest(string? configuredKey)
        {
            var filter = CreateFilter(configuredKey);
            var context = CreateContext(configuredKey ?? string.Empty);

            filter.OnAuthorization(context);

            filter.IsConfigured.Should().BeFalse();
            ShouldBeUnauthorized(context);
        }

        [Fact]
        public void OnAuthorization_Rejected_DoesNotLogProvidedOrConfiguredKey()
        {
            const string providedKey = "provided-secret-value-that-must-not-be-logged";
            var context = CreateContext(providedKey);

            CreateFilter().OnAuthorization(context);

            var loggedArguments = _logger.ReceivedCalls()
                .SelectMany(call => call.GetArguments())
                .Select(argument => argument?.ToString() ?? string.Empty)
                .ToList();

            loggedArguments.Should().NotBeEmpty();
            loggedArguments.Should().NotContain(argument => argument.Contains(providedKey));
            loggedArguments.Should().NotContain(argument => argument.Contains(ConfiguredKey));
        }
    }
}
