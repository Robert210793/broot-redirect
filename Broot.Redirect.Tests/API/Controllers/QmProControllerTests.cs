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
                new IntegrationWriteLock(),
                Substitute.For<ILogger<QmProRuleService>>());

            _sut = CreateController(service, _cache);
        }

        private QmProController CreateController(QmProRuleService service, IRuleCacheService cache)
        {
            var areaRuleSyncService = new AreaRuleSyncService(
                _repository,
                cache,
                new RuleValidationService(),
                new IntegrationWriteLock(),
                Substitute.For<ILogger<AreaRuleSyncService>>());

            return new QmProController(service, areaRuleSyncService, cache)
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
            public async Task Publish_BelowPartialRule_Returns201()
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

                result!.StatusCode.Should().Be(StatusCodes.Status201Created);
                ToJson(result.Value).GetProperty("result").GetString().Should().Be("created");
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
                    new IntegrationWriteLock(),
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
                    new IntegrationWriteLock(),
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

            [Fact]
            public void RedirectRule_FromAreaList_SerializesSourceAsAreaList()
            {
                var json = ToJson(new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/ims/A",
                    Source = RuleSource.AreaList,
                    ExternalDocumentId = "5b0c2a4e-0000-4000-8000-000000000001"
                });

                json.GetProperty("source").GetString().Should().Be("areaList");
                json.GetProperty("externalDocumentId").GetString().Should().Be("5b0c2a4e-0000-4000-8000-000000000001");
            }
        }

        public class AreaRulesTests : QmProControllerTests
        {
            private const string EntryId = "5b0c2a4e-0000-4000-8000-000000000001";

            private static AreaRuleSyncRequest SyncRequest(params AreaRuleSyncEntry?[] entries)
            {
                return new AreaRuleSyncRequest { Entries = entries.ToList() };
            }

            private static AreaRuleSyncEntry Entry(
                string id = EntryId,
                string? sourceUrl = "/ims/KVP_Innovation/11%20Prozessabweichungen/",
                string? targetUrl = "https://tenant.sharepoint.com/sites/QM/KVP")
            {
                return new AreaRuleSyncEntry { Id = id, SourceUrl = sourceUrl, TargetUrl = targetUrl };
            }

            [Fact]
            public void SyncAreaRules_IsPutOnAreaRulesRoute()
            {
                var method = typeof(QmProController).GetMethod(nameof(QmProController.SyncAreaRules))!;

                method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: true)
                    .Cast<HttpPutAttribute>()
                    .Should().ContainSingle(attribute => attribute.Template == "area-rules");
            }

            [Fact]
            public async Task SyncAreaRules_ValidState_Returns200WithCountsAndEmptySkipped()
            {
                var result = await _sut.SyncAreaRules(SyncRequest(Entry()), CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status200OK);
                var body = ToJson(result.Value);
                body.GetProperty("created").GetInt32().Should().Be(1);
                body.GetProperty("updated").GetInt32().Should().Be(0);
                body.GetProperty("deleted").GetInt32().Should().Be(0);
                body.GetProperty("unchanged").GetInt32().Should().Be(0);
                body.GetProperty("skipped").GetArrayLength().Should().Be(0);
                _cache.GetAll().Should().ContainSingle().Which.Source.Should().Be(RuleSource.AreaList);
            }

            [Fact]
            public async Task SyncAreaRules_SkippedEntry_IsReportedWithIdCodeErrorAndConflictingRuleId()
            {
                var manualRule = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/ims/KVP_Innovation/11%20Prozessabweichungen",
                    TargetUrl = "https://tenant.sharepoint.com/manual",
                    Source = RuleSource.Manual
                };
                _cache.AddRule(manualRule);

                var result = await _sut.SyncAreaRules(SyncRequest(Entry(), Entry(id: "x", sourceUrl: "kein Link")), CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status200OK);
                var skipped = ToJson(result.Value).GetProperty("skipped");
                skipped.GetArrayLength().Should().Be(2);
                skipped[0].GetProperty("id").GetString().Should().Be(EntryId);
                skipped[0].GetProperty("code").GetString().Should().Be("MATCHER_DUPLICATE");
                skipped[0].GetProperty("error").GetString().Should().NotBeNullOrEmpty();
                skipped[0].GetProperty("conflictingRuleId").GetGuid().Should().Be(manualRule.Id);
                skipped[1].GetProperty("id").GetString().Should().Be("x");
                skipped[1].GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
                skipped[1].TryGetProperty("conflictingRuleId", out _).Should().BeFalse();
            }

            [Fact]
            public async Task SyncAreaRules_EmptyEntries_IsValidAndReturns200()
            {
                var result = await _sut.SyncAreaRules(SyncRequest(), CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status200OK);
            }

            [Fact]
            public async Task SyncAreaRules_MissingEntries_Returns400ValidationError()
            {
                var result = await _sut.SyncAreaRules(new AreaRuleSyncRequest(), CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
                ToJson(result.Value).GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
                await _repository.DidNotReceive().BulkDeleteAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task SyncAreaRules_MissingBody_Returns400ValidationError()
            {
                var result = await _sut.SyncAreaRules(null, CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
                ToJson(result.Value).GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
            }

            [Fact]
            public async Task SyncAreaRules_BeforeWarmup_Returns503WithRetryAfterAndDoesNotWrite()
            {
                var coldCache = new RuleCacheService(
                    Options.Create(new CacheOptions()),
                    Substitute.For<ILogger<RuleCacheService>>());
                var service = new QmProRuleService(
                    _repository,
                    coldCache,
                    new RuleValidationService(),
                    new IntegrationWriteLock(),
                    Substitute.For<ILogger<QmProRuleService>>());
                var sut = CreateController(service, coldCache);

                var result = await sut.SyncAreaRules(SyncRequest(Entry()), CancellationToken.None) as ObjectResult;

                result!.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
                sut.Response.Headers.RetryAfter.ToString().Should().Be(QmProController.CacheNotReadyRetryAfterSeconds.ToString());
                ToJson(result.Value).GetProperty("code").GetString().Should().Be("CACHE_NOT_READY");
                await _repository.DidNotReceive().BulkUpsertAsync(Arg.Any<IReadOnlyList<RedirectRule>>(), Arg.Any<CancellationToken>());
            }
        }
    }
}
