using System.Text.Json;
using System.Text.Json.Serialization;
using Broot.Redirect.API.Controllers;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.API.Filters;
using Broot.Redirect.API.Services;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Broot.Redirect.Infrastructure.Cache;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.API.Controllers
{
    public class QmProControllerTests
    {
        private const string DocumentId = "https://tenant.sharepoint.com/sites/QM|list|item";

        private static readonly JsonSerializerOptions ApiJsonOptions = CreateApiJsonOptions();

        private readonly IRedirectRuleRepository _repository;
        private readonly RuleCacheService _cache;
        private readonly QmProController _sut;

        public QmProControllerTests()
        {
            _repository = Substitute.For<IRedirectRuleRepository>();
            _repository.UpdateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>()).Returns(true);

            _cache = new RuleCacheService(
                Options.Create(new CacheOptions()),
                Substitute.For<ILogger<RuleCacheService>>());
            _cache.Initialize(new List<RedirectRule>());

            var service = new QmProRuleService(
                _repository,
                _cache,
                new RuleValidationService(),
                Substitute.For<ILogger<QmProRuleService>>());

            _sut = CreateController(service, _cache);
        }

        private static QmProController CreateController(QmProRuleService service, IRuleCacheService cache)
        {
            return new QmProController(service, cache)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
        }

        private static JsonSerializerOptions CreateApiJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

            return options;
        }

        private static JsonElement ToJson(object? value)
        {
            return JsonSerializer.SerializeToElement(value, ApiJsonOptions);
        }

        private static QmProPublishRequest PublishRequest(string oldUrl = "https://old.example.local/docs/a.docx")
        {
            return new QmProPublishRequest
            {
                DocumentId = DocumentId,
                OldUrl = oldUrl,
                PublicationUrl = "https://tenant.sharepoint.com/sites/QM/Pub/a.docx"
            };
        }

        public class ControllerSetupTests : QmProControllerTests
        {
            [Fact]
            public void Controller_UsesApiKeyFilterAndIntegrationRoute()
            {
                var type = typeof(QmProController);

                type.GetCustomAttributes(typeof(ServiceFilterAttribute), inherit: true)
                    .Cast<ServiceFilterAttribute>()
                    .Should().ContainSingle(attribute => attribute.ServiceType == typeof(QmProApiKeyFilter));

                type.GetCustomAttributes(typeof(RouteAttribute), inherit: true)
                    .Cast<RouteAttribute>()
                    .Should().ContainSingle(attribute => attribute.Template == "api/qm-pro");
            }
        }

        public class PublishTests : QmProControllerTests
        {
            [Fact]
            public async Task Publish_NewDocument_Returns201WithCreated()
            {
                var result = await _sut.Publish(PublishRequest(), CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status201Created);
                var body = ToJson(result.Value);
                body.GetProperty("result").GetString().Should().Be("created");
                body.GetProperty("ruleId").GetGuid().Should().Be(_cache.GetAll().Single().Id);
            }

            [Fact]
            public async Task Publish_ChangedData_Returns200WithUpdated()
            {
                await _sut.Publish(PublishRequest(), CancellationToken.None);

                var result = await _sut.Publish(PublishRequest("https://old.example.local/docs/b.docx"), CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status200OK);
                ToJson(result.Value).GetProperty("result").GetString().Should().Be("updated");
            }

            [Fact]
            public async Task Publish_SameData_Returns200WithUnchanged()
            {
                await _sut.Publish(PublishRequest(), CancellationToken.None);

                var result = await _sut.Publish(PublishRequest(), CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status200OK);
                ToJson(result.Value).GetProperty("result").GetString().Should().Be("unchanged");
            }

            [Fact]
            public async Task Publish_InvalidInput_Returns400WithValidationErrorCode()
            {
                var request = PublishRequest("not-a-url");

                var result = await _sut.Publish(request, CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
                var body = ToJson(result.Value);
                body.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
                body.GetProperty("error").GetString().Should().Contain("Alte URL");
            }

            [Fact]
            public async Task Publish_NullBody_Returns400WithValidationErrorCode()
            {
                var result = await _sut.Publish(null, CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
                ToJson(result.Value).GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
            }

            [Fact]
            public async Task Publish_DuplicateMatcher_Returns409WithMatcherDuplicate()
            {
                var other = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/docs/a.docx",
                    TargetUrl = "https://x.example.com/",
                    RedirectType = RedirectType.Wildcard
                };
                _cache.AddRule(other);

                var result = await _sut.Publish(PublishRequest(), CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status409Conflict);
                var body = ToJson(result.Value);
                body.GetProperty("code").GetString().Should().Be("MATCHER_DUPLICATE");
                body.GetProperty("conflictingRule").GetProperty("id").GetGuid().Should().Be(other.Id);
            }

            [Fact]
            public async Task Publish_OverlappingPartialRule_Returns409WithMatcherConflict()
            {
                var partial = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/docs",
                    TargetUrl = "/new",
                    RedirectType = RedirectType.Partial
                };
                _cache.AddRule(partial);

                var result = await _sut.Publish(PublishRequest(), CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status409Conflict);
                var body = ToJson(result.Value);
                body.GetProperty("code").GetString().Should().Be("MATCHER_CONFLICT");
                body.GetProperty("conflictingRule").GetProperty("matcher").GetString().Should().Be("/docs");
            }

            [Fact]
            public async Task Publish_BeforeWarmup_Returns503WithRetryAfterAndDoesNotWrite()
            {
                var coldCache = new RuleCacheService(
                    Options.Create(new CacheOptions()),
                    Substitute.For<ILogger<RuleCacheService>>());
                var service = new QmProRuleService(
                    _repository,
                    coldCache,
                    new RuleValidationService(),
                    Substitute.For<ILogger<QmProRuleService>>());
                var sut = CreateController(service, coldCache);

                var result = await sut.Publish(PublishRequest(), CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
                sut.Response.Headers.RetryAfter.ToString().Should().Be(QmProController.CacheNotReadyRetryAfterSeconds.ToString());
                await _repository.DidNotReceive().CreateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());
            }
        }

        public class UnpublishTests : QmProControllerTests
        {
            [Fact]
            public async Task Unpublish_ActiveRule_Returns200WithDeactivated()
            {
                await _sut.Publish(PublishRequest(), CancellationToken.None);

                var result = await _sut.Unpublish(new QmProUnpublishRequest { DocumentId = DocumentId }, CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status200OK);
                var body = ToJson(result.Value);
                body.GetProperty("result").GetString().Should().Be("deactivated");
                body.GetProperty("ruleId").GetGuid().Should().Be(_cache.GetAll().Single().Id);
            }

            [Fact]
            public async Task Unpublish_InactiveRule_Returns200WithAlreadyInactive()
            {
                await _sut.Publish(PublishRequest(), CancellationToken.None);
                await _sut.Unpublish(new QmProUnpublishRequest { DocumentId = DocumentId }, CancellationToken.None);

                var result = await _sut.Unpublish(new QmProUnpublishRequest { DocumentId = DocumentId }, CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status200OK);
                ToJson(result.Value).GetProperty("result").GetString().Should().Be("alreadyInactive");
            }

            [Fact]
            public async Task Unpublish_UnknownDocument_Returns200WithNotFound()
            {
                var result = await _sut.Unpublish(new QmProUnpublishRequest { DocumentId = "unknown" }, CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status200OK);
                var body = ToJson(result.Value);
                body.GetProperty("result").GetString().Should().Be("notFound");
                body.TryGetProperty("ruleId", out _).Should().BeFalse();
            }

            [Fact]
            public async Task Unpublish_MissingDocumentId_Returns400()
            {
                var result = await _sut.Unpublish(new QmProUnpublishRequest(), CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
                ToJson(result.Value).GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
            }

            [Fact]
            public async Task Unpublish_BeforeWarmup_Returns503()
            {
                var coldCache = new RuleCacheService(
                    Options.Create(new CacheOptions()),
                    Substitute.For<ILogger<RuleCacheService>>());
                var service = new QmProRuleService(
                    _repository,
                    coldCache,
                    new RuleValidationService(),
                    Substitute.For<ILogger<QmProRuleService>>());
                var sut = CreateController(service, coldCache);

                var result = await sut.Unpublish(new QmProUnpublishRequest { DocumentId = DocumentId }, CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
                sut.Response.Headers.RetryAfter.ToString().Should().NotBeEmpty();
            }
        }

        public class RuleJsonContractTests : QmProControllerTests
        {
            [Fact]
            public void RedirectRule_SerializedWithApiOptions_ContainsSourceIsActiveAndExternalDocumentId()
            {
                var rule = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/docs/a.docx",
                    Source = RuleSource.QmPro,
                    IsActive = false,
                    ExternalDocumentId = DocumentId
                };

                var json = ToJson(rule);

                json.GetProperty("source").GetString().Should().Be("qmPro");
                json.GetProperty("isActive").GetBoolean().Should().BeFalse();
                json.GetProperty("externalDocumentId").GetString().Should().Be(DocumentId);
            }

            [Fact]
            public void RedirectRule_WithoutDocumentId_OmitsExternalDocumentIdAndIsActiveDefaultsTrue()
            {
                var json = ToJson(new RedirectRule { Id = Guid.NewGuid(), Matcher = "/a", Source = RuleSource.Manual });

                json.GetProperty("source").GetString().Should().Be("manual");
                json.GetProperty("isActive").GetBoolean().Should().BeTrue();
                json.TryGetProperty("externalDocumentId", out _).Should().BeFalse();
            }
        }
    }
}
