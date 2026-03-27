using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Controllers;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.API.Services;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.API.Controllers
{
    public class RulesControllerTests
    {
        private readonly IRedirectRuleRepository _repository;
        private readonly IRuleCacheService _cacheService;
        private readonly IRuleMatchingService _ruleMatchingService;
        private readonly IUrlTransformService _urlTransformService;
        private readonly IAppSettingsCacheService _settingsCache;
        private readonly RulesController _sut;

        public RulesControllerTests()
        {
            _repository = Substitute.For<IRedirectRuleRepository>();
            _cacheService = Substitute.For<IRuleCacheService>();
            _ruleMatchingService = Substitute.For<IRuleMatchingService>();
            _urlTransformService = Substitute.For<IUrlTransformService>();
            _settingsCache = Substitute.For<IAppSettingsCacheService>();
            var validationService = new RuleValidationService();
            var options = Options.Create(new BrootRedirectOptions());
            var logger = Substitute.For<ILogger<RulesController>>();

            _sut = new RulesController(
                _repository,
                _cacheService,
                _ruleMatchingService,
                _urlTransformService,
                _settingsCache,
                validationService,
                options,
                logger);

            _cacheService.GetAll().Returns(new List<RedirectRule>());
            _settingsCache.GetSettings().Returns(new AppSettings());
        }

        private static RedirectRule CreateRule(string matcher = "/test", RedirectType type = RedirectType.Partial)
        {
            return new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = matcher,
                RedirectType = type,
                TargetUrl = "/target",
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        public class GetPaginatedTests : RulesControllerTests
        {
            [Fact]
            public void GetPaginated_ReturnsOkWithPaginatedResponse()
            {
                var rules = new List<RedirectRule>
                {
                    CreateRule("/a"),
                    CreateRule("/b"),
                    CreateRule("/c")
                };

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(page: 1, limit: 2) as OkObjectResult;

                result.Should().NotBeNull();

                var response = result!.Value as PaginatedRulesResponse;

                response.Should().NotBeNull();
                response!.Total.Should().Be(3);
                response.TotalPages.Should().Be(2);
                response.Rules.Should().HaveCount(2);
            }

            [Fact]
            public void GetPaginated_WithSearch_FiltersRules()
            {
                var rules = new List<RedirectRule>
                {
                    CreateRule("/matching-path"),
                    CreateRule("/other")
                };

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(search: "matching") as OkObjectResult;

                var response = result!.Value as PaginatedRulesResponse;

                response!.Total.Should().Be(1);
            }

            [Fact]
            public void GetPaginated_SortByMatcher_SortsCorrectly()
            {
                var rules = new List<RedirectRule>
                {
                    CreateRule("/b"),
                    CreateRule("/a")
                };

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(sortBy: "matcher", sortOrder: "asc") as OkObjectResult;

                var response = result!.Value as PaginatedRulesResponse;

                response!.Rules[0].Matcher.Should().Be("/a");
            }

            [Fact]
            public void GetPaginated_InvalidPage_DefaultsToOne()
            {
                _cacheService.GetAll().Returns(new List<RedirectRule> { CreateRule() });

                var result = _sut.GetPaginated(page: -1) as OkObjectResult;

                var response = result!.Value as PaginatedRulesResponse;

                response!.CurrentPage.Should().Be(1);
            }

            [Fact]
            public void GetPaginated_SortByTargetUrl_Works()
            {
                var rules = new List<RedirectRule>
                {
                    CreateRule("/a"),
                    CreateRule("/b")
                };

                rules[0].TargetUrl = "/z";
                rules[1].TargetUrl = "/a";

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(sortBy: "targeturl", sortOrder: "asc") as OkObjectResult;

                result.Should().NotBeNull();
            }

            [Fact]
            public void GetPaginated_SortByRedirectType_Works()
            {
                _cacheService.GetAll().Returns(new List<RedirectRule>
                {
                    CreateRule("/a", RedirectType.Wildcard),
                    CreateRule("/b", RedirectType.Partial)
                });

                var result = _sut.GetPaginated(sortBy: "redirecttype", sortOrder: "asc") as OkObjectResult;

                result.Should().NotBeNull();
            }
        }

        public class GetByIdTests : RulesControllerTests
        {
            [Fact]
            public void GetById_Exists_ReturnsOk()
            {
                var rule = CreateRule();

                _cacheService.GetById(rule.Id).Returns(rule);

                var result = _sut.GetById(rule.Id) as OkObjectResult;

                result.Should().NotBeNull();
                result!.Value.Should().Be(rule);
            }

            [Fact]
            public void GetById_NotFound_Returns404()
            {
                _cacheService.GetById(Arg.Any<Guid>()).Returns((RedirectRule?)null);

                var result = _sut.GetById(Guid.NewGuid());

                result.Should().BeOfType<NotFoundObjectResult>();
            }
        }

        public class CreateTests : RulesControllerTests
        {
            [Fact]
            public async Task Create_ValidRequest_ReturnsCreated()
            {
                var request = new CreateRuleRequest
                {
                    Matcher = "/new-rule",
                    TargetUrl = "/target",
                    RedirectType = "partial"
                };

                _cacheService.MatcherExists(Arg.Any<string>(), Arg.Any<Guid?>()).Returns(false);
                _cacheService.FindOverlappingMatcher(Arg.Any<string>(), Arg.Any<Guid?>()).Returns((RedirectRule?)null);

                var result = await _sut.Create(request) as CreatedAtActionResult;

                result.Should().NotBeNull();

                await _repository.Received(1).CreateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());

                _cacheService.Received(1).AddRule(Arg.Any<RedirectRule>());
            }

            [Fact]
            public async Task Create_InvalidRedirectType_ReturnsBadRequest()
            {
                var request = new CreateRuleRequest
                {
                    Matcher = "/path",
                    RedirectType = "invalid"
                };

                var result = await _sut.Create(request);

                result.Should().BeOfType<BadRequestObjectResult>();
            }

            [Fact]
            public async Task Create_DuplicateMatcher_ReturnsBadRequest()
            {
                var request = new CreateRuleRequest
                {
                    Matcher = "/existing",
                    RedirectType = "partial"
                };

                _cacheService.MatcherExists("/existing", Arg.Any<Guid?>()).Returns(true);

                var result = await _sut.Create(request);

                result.Should().BeOfType<BadRequestObjectResult>();
            }

            [Fact]
            public async Task Create_OverlappingMatcher_ReturnsConflict()
            {
                var request = new CreateRuleRequest
                {
                    Matcher = "/a/b/c",
                    RedirectType = "partial"
                };

                _cacheService.MatcherExists(Arg.Any<string>(), Arg.Any<Guid?>()).Returns(false);
                _cacheService.FindOverlappingMatcher(Arg.Any<string>(), Arg.Any<Guid?>())
                    .Returns(CreateRule("/a/b"));

                var result = await _sut.Create(request);

                result.Should().BeOfType<ConflictObjectResult>();
            }
        }

        public class UpdateTests : RulesControllerTests
        {
            [Fact]
            public async Task Update_Exists_ReturnsOk()
            {
                var rule = CreateRule();

                _cacheService.GetById(rule.Id).Returns(rule);

                var request = new UpdateRuleRequest { TargetUrl = "/updated" };

                var result = await _sut.Update(rule.Id, request) as OkObjectResult;

                result.Should().NotBeNull();

                await _repository.Received(1).UpdateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Update_NotFound_Returns404()
            {
                _cacheService.GetById(Arg.Any<Guid>()).Returns((RedirectRule?)null);

                var result = await _sut.Update(Guid.NewGuid(), new UpdateRuleRequest());

                result.Should().BeOfType<NotFoundObjectResult>();
            }

            [Fact]
            public async Task Update_MatcherChangeDuplicate_ReturnsBadRequest()
            {
                var rule = CreateRule("/original");

                _cacheService.GetById(rule.Id).Returns(rule);
                _cacheService.MatcherExists("/taken", rule.Id).Returns(true);

                var request = new UpdateRuleRequest { Matcher = "/taken" };

                var result = await _sut.Update(rule.Id, request);

                result.Should().BeOfType<BadRequestObjectResult>();
            }

            [Fact]
            public async Task Update_MatcherChangeOverlap_ReturnsConflict()
            {
                var rule = CreateRule("/original");

                _cacheService.GetById(rule.Id).Returns(rule);
                _cacheService.MatcherExists(Arg.Any<string>(), Arg.Any<Guid?>()).Returns(false);
                _cacheService.FindOverlappingMatcher("/a/b/c", rule.Id).Returns(CreateRule("/a/b"));

                var request = new UpdateRuleRequest { Matcher = "/a/b/c" };

                var result = await _sut.Update(rule.Id, request);

                result.Should().BeOfType<ConflictObjectResult>();
            }

            [Fact]
            public async Task Update_InvalidRedirectType_ReturnsBadRequest()
            {
                var rule = CreateRule();

                _cacheService.GetById(rule.Id).Returns(rule);

                var request = new UpdateRuleRequest { RedirectType = "invalid" };

                var result = await _sut.Update(rule.Id, request);

                result.Should().BeOfType<BadRequestObjectResult>();
            }

            [Fact]
            public async Task Update_ValidRedirectType_UpdatesType()
            {
                var rule = CreateRule();

                _cacheService.GetById(rule.Id).Returns(rule);

                var request = new UpdateRuleRequest { RedirectType = "wildcard" };

                var result = await _sut.Update(rule.Id, request) as OkObjectResult;

                result.Should().NotBeNull();

                var updated = result!.Value as RedirectRule;

                updated!.RedirectType.Should().Be(RedirectType.Wildcard);
            }
        }

        public class DeleteTests : RulesControllerTests
        {
            [Fact]
            public async Task Delete_Exists_ReturnsNoContent()
            {
                var rule = CreateRule();

                _cacheService.GetById(rule.Id).Returns(rule);

                var result = await _sut.Delete(rule.Id);

                result.Should().BeOfType<NoContentResult>();
            }

            [Fact]
            public async Task Delete_NotFound_Returns404()
            {
                _cacheService.GetById(Arg.Any<Guid>()).Returns((RedirectRule?)null);

                var result = await _sut.Delete(Guid.NewGuid());

                result.Should().BeOfType<NotFoundObjectResult>();
            }
        }

        public class BulkDeleteTests : RulesControllerTests
        {
            [Fact]
            public async Task BulkDelete_EmptyIds_ReturnsBadRequest()
            {
                var result = await _sut.BulkDelete(new BulkDeleteRequest { Ids = new List<string>() });

                result.Should().BeOfType<BadRequestObjectResult>();
            }

            [Fact]
            public async Task BulkDelete_ValidIds_ReturnsDeletedCount()
            {
                var rule = CreateRule();

                _cacheService.GetById(rule.Id).Returns(rule);

                var request = new BulkDeleteRequest
                {
                    Ids = new List<string> { rule.Id.ToString() }
                };

                var result = await _sut.BulkDelete(request) as OkObjectResult;

                result.Should().NotBeNull();

                var response = result!.Value as BulkDeleteResponse;

                response!.Deleted.Should().Be(1);
            }

            [Fact]
            public async Task BulkDelete_InvalidGuid_CountsAsNotFound()
            {
                var request = new BulkDeleteRequest
                {
                    Ids = new List<string> { "not-a-guid" }
                };

                var result = await _sut.BulkDelete(request) as OkObjectResult;

                var response = result!.Value as BulkDeleteResponse;

                response!.NotFound.Should().Be(1);
            }
        }

        public class ExportTests : RulesControllerTests
        {
            [Fact]
            public void Export_Json_ReturnsJsonFile()
            {
                _cacheService.GetAll().Returns(new List<RedirectRule> { CreateRule() });

                var result = _sut.Export("json") as FileContentResult;

                result.Should().NotBeNull();
                result!.ContentType.Should().Be("application/json");
                result.FileDownloadName.Should().Be("rules.json");
            }

            [Fact]
            public void Export_Csv_ReturnsCsvFile()
            {
                _cacheService.GetAll().Returns(new List<RedirectRule> { CreateRule() });

                var result = _sut.Export("csv") as FileContentResult;

                result.Should().NotBeNull();
                result!.ContentType.Should().Be("text/csv");
            }

            [Fact]
            public void Export_Xlsx_ReturnsXlsxFile()
            {
                _cacheService.GetAll().Returns(new List<RedirectRule> { CreateRule() });

                var result = _sut.Export("xlsx") as FileContentResult;

                result.Should().NotBeNull();
                result!.ContentType.Should().Contain("spreadsheetml");
            }
        }

        public class ValidateTests : RulesControllerTests
        {
            [Fact]
            public void Validate_EmptyUrls_ReturnsBadRequest()
            {
                var request = new ValidateUrlsRequest { Urls = new List<string>() };

                var result = _sut.Validate(request);

                result.Should().BeOfType<BadRequestObjectResult>();
            }

            [Fact]
            public void Validate_TooManyUrls_ReturnsBadRequest()
            {
                var request = new ValidateUrlsRequest
                {
                    Urls = Enumerable.Range(0, 501).Select(i => $"/url-{i}").ToList()
                };

                var result = _sut.Validate(request);

                result.Should().BeOfType<BadRequestObjectResult>();
            }

            [Fact]
            public void Validate_MatchFound_ReturnsMatchedResult()
            {
                var rule = CreateRule("/test");

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
                    .ResolveTargetUrlWithTrace(Arg.Any<string>(), Arg.Any<RedirectRule>(), Arg.Any<string>())
                    .Returns(("https://new.com/page", new List<UrlTraceStep>
                    {
                        new UrlTraceStep { Type = "input", Description = "Input", After = "/test" },
                        new UrlTraceStep { Type = "result", Description = "Result", After = "https://new.com/page" }
                    }));

                var request = new ValidateUrlsRequest { Urls = new List<string> { "/test" } };

                var result = _sut.Validate(request) as OkObjectResult;

                result.Should().NotBeNull();

                var response = result!.Value as ValidateUrlsResponse;

                response!.Matched.Should().Be(1);
                response.Results[0].Matched.Should().BeTrue();
            }

            [Fact]
            public void Validate_NoMatch_ReturnsUnmatchedResult()
            {
                _ruleMatchingService
                    .ResolveMatch(Arg.Any<string>(), Arg.Any<RuleMatchingConfig>())
                    .Returns((RuleMatchResult?)null);

                var request = new ValidateUrlsRequest { Urls = new List<string> { "/unknown" } };

                var result = _sut.Validate(request) as OkObjectResult;

                var response = result!.Value as ValidateUrlsResponse;

                response!.Unmatched.Should().Be(1);
                response.Results[0].Matched.Should().BeFalse();
            }

            [Fact]
            public void Validate_EmptyUrl_SkipsIt()
            {
                var request = new ValidateUrlsRequest { Urls = new List<string> { "", "  " } };

                var result = _sut.Validate(request) as OkObjectResult;

                var response = result!.Value as ValidateUrlsResponse;

                response!.Total.Should().Be(0);
            }

            [Fact]
            public void Validate_ExceptionDuringMatch_ReturnsError()
            {
                _ruleMatchingService
                    .ResolveMatch(Arg.Any<string>(), Arg.Any<RuleMatchingConfig>())
                    .Returns(_ => throw new Exception("match error"));

                var request = new ValidateUrlsRequest { Urls = new List<string> { "/test" } };

                var result = _sut.Validate(request) as OkObjectResult;

                var response = result!.Value as ValidateUrlsResponse;

                response!.Results[0].Error.Should().Be("match error");
            }
        }

        public class DeleteAllTests : RulesControllerTests
        {
            [Fact]
            public void DeleteAll_ReturnsJobId()
            {
                _cacheService.GetAll().Returns(new List<RedirectRule> { CreateRule() });

                var result = _sut.DeleteAll() as OkObjectResult;

                result.Should().NotBeNull();
            }
        }

        public class GetJobProgressTests : RulesControllerTests
        {
            [Fact]
            public void GetJobProgress_NotFound_Returns404()
            {
                var result = _sut.GetJobProgress("nonexistent");

                result.Should().BeOfType<NotFoundObjectResult>();
            }
        }

        public class AdditionalGetPaginatedTests : RulesControllerTests
        {
            [Fact]
            public void GetPaginated_SearchByTargetUrl_FiltersCorrectly()
            {
                var rules = new List<RedirectRule>
                {
                    CreateRule("/a"),
                    CreateRule("/b")
                };

                rules[0].TargetUrl = "https://example.com/found";
                rules[1].TargetUrl = "https://other.com/page";

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(search: "found") as OkObjectResult;

                var response = result!.Value as PaginatedRulesResponse;

                response!.Total.Should().Be(1);
                response.Rules[0].TargetUrl.Should().Contain("found");
            }

            [Fact]
            public void GetPaginated_SearchByInfoText_FiltersCorrectly()
            {
                var rules = new List<RedirectRule>
                {
                    CreateRule("/a"),
                    CreateRule("/b")
                };

                rules[0].InfoText = "This is a legacy page";
                rules[1].InfoText = "Something else";

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(search: "legacy") as OkObjectResult;

                var response = result!.Value as PaginatedRulesResponse;

                response!.Total.Should().Be(1);
            }

            [Fact]
            public void GetPaginated_SortByCreatedAtAsc_SortsCorrectly()
            {
                var rules = new List<RedirectRule>
                {
                    CreateRule("/newer"),
                    CreateRule("/older")
                };

                rules[0].CreatedAt = DateTimeOffset.UtcNow;
                rules[1].CreatedAt = DateTimeOffset.UtcNow.AddDays(-1);

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(sortBy: "createdAt", sortOrder: "asc") as OkObjectResult;

                var response = result!.Value as PaginatedRulesResponse;

                response!.Rules[0].Matcher.Should().Be("/older");
            }

            [Fact]
            public void GetPaginated_SortByMatcherDesc_SortsCorrectly()
            {
                var rules = new List<RedirectRule>
                {
                    CreateRule("/alpha"),
                    CreateRule("/zeta")
                };

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(sortBy: "matcher", sortOrder: "desc") as OkObjectResult;

                var response = result!.Value as PaginatedRulesResponse;

                response!.Rules[0].Matcher.Should().Be("/zeta");
            }

            [Fact]
            public void GetPaginated_SortByTargetUrlDesc_Works()
            {
                var rules = new List<RedirectRule>
                {
                    CreateRule("/a"),
                    CreateRule("/b")
                };

                rules[0].TargetUrl = "/alpha";
                rules[1].TargetUrl = "/zulu";

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(sortBy: "targeturl", sortOrder: "desc") as OkObjectResult;

                var response = result!.Value as PaginatedRulesResponse;

                response!.Rules[0].TargetUrl.Should().Be("/zulu");
            }

            [Fact]
            public void GetPaginated_SortByRedirectTypeDesc_Works()
            {
                _cacheService.GetAll().Returns(new List<RedirectRule>
                {
                    CreateRule("/a", RedirectType.Partial),
                    CreateRule("/b", RedirectType.Wildcard)
                });

                var result = _sut.GetPaginated(sortBy: "redirecttype", sortOrder: "desc") as OkObjectResult;

                result.Should().NotBeNull();
            }

            [Fact]
            public void GetPaginated_UnknownSortField_DefaultsToCreatedAt()
            {
                var rules = new List<RedirectRule>
                {
                    CreateRule("/a"),
                    CreateRule("/b")
                };

                rules[0].CreatedAt = DateTimeOffset.UtcNow;
                rules[1].CreatedAt = DateTimeOffset.UtcNow.AddDays(-1);

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(sortBy: "unknownfield", sortOrder: "asc") as OkObjectResult;

                var response = result!.Value as PaginatedRulesResponse;

                response!.Rules[0].Matcher.Should().Be("/b");
            }

            [Fact]
            public void GetPaginated_LimitTooHigh_DefaultsTo50()
            {
                var rules = Enumerable.Range(0, 100).Select(i => CreateRule($"/rule-{i}")).ToList();

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(limit: 999) as OkObjectResult;

                var response = result!.Value as PaginatedRulesResponse;

                response!.Rules.Should().HaveCount(50);
            }

            [Fact]
            public void GetPaginated_LimitZero_DefaultsTo50()
            {
                var rules = Enumerable.Range(0, 100).Select(i => CreateRule($"/rule-{i}")).ToList();

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(limit: 0) as OkObjectResult;

                var response = result!.Value as PaginatedRulesResponse;

                response!.Rules.Should().HaveCount(50);
            }

            [Fact]
            public void GetPaginated_Page2_SkipsFirstPage()
            {
                var rules = Enumerable.Range(0, 10).Select(i => CreateRule($"/rule-{i:D2}")).ToList();

                _cacheService.GetAll().Returns(rules);

                var result = _sut.GetPaginated(page: 2, limit: 3, sortBy: "matcher", sortOrder: "asc") as OkObjectResult;

                var response = result!.Value as PaginatedRulesResponse;

                response!.CurrentPage.Should().Be(2);
                response.Rules.Should().HaveCount(3);
                response.Rules[0].Matcher.Should().Be("/rule-03");
            }
        }

        public class AdditionalBulkDeleteTests : RulesControllerTests
        {
            [Fact]
            public async Task BulkDelete_MixedExistingAndNotFound_ReturnsCorrectCounts()
            {
                var existingRule = CreateRule();
                var missingId = Guid.NewGuid();

                _cacheService.GetById(existingRule.Id).Returns(existingRule);
                _cacheService.GetById(missingId).Returns((RedirectRule?)null);

                var request = new BulkDeleteRequest
                {
                    Ids = new List<string> { existingRule.Id.ToString(), missingId.ToString() }
                };

                var result = await _sut.BulkDelete(request) as OkObjectResult;

                var response = result!.Value as BulkDeleteResponse;

                response!.Deleted.Should().Be(1);
                response.NotFound.Should().Be(1);
            }

            [Fact]
            public async Task BulkDelete_DeleteThrows_CountsAsNotFound()
            {
                var rule = CreateRule();

                _cacheService.GetById(rule.Id).Returns(rule);
                _repository.DeleteAsync(rule.Id, Arg.Any<CancellationToken>())
                    .Returns<bool>(_ => throw new Exception("storage error"));

                var request = new BulkDeleteRequest
                {
                    Ids = new List<string> { rule.Id.ToString() }
                };

                var result = await _sut.BulkDelete(request) as OkObjectResult;

                var response = result!.Value as BulkDeleteResponse;

                response!.Deleted.Should().Be(0);
                response.NotFound.Should().Be(1);
            }

            [Fact]
            public async Task BulkDelete_NullIds_ReturnsBadRequest()
            {
                var request = new BulkDeleteRequest { Ids = null! };

                var result = await _sut.BulkDelete(request);

                result.Should().BeOfType<BadRequestObjectResult>();
            }
        }

        public class ImportPreviewTests : RulesControllerTests
        {
            [Fact]
            public async Task ImportPreview_JsonBody_ParsesAndReturnsPreview()
            {
                var entries = new List<ImportRuleEntry>
                {
                    new ImportRuleEntry { Matcher = "/new-path", TargetUrl = "/target", RedirectType = "partial" }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(entries);
                var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

                var httpContext = new DefaultHttpContext();
                httpContext.Request.ContentType = "application/json";
                httpContext.Request.Body = stream;

                _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

                var result = await _sut.ImportPreview() as OkObjectResult;

                result.Should().NotBeNull();

                var response = result!.Value as ImportPreviewResponse;

                response!.Total.Should().Be(1);
                response.Counts.New.Should().Be(1);
                response.Preview[0].Status.Should().Be("new");
            }

            [Fact]
            public async Task ImportPreview_EmptyMatcher_MarkedAsInvalid()
            {
                var entries = new List<ImportRuleEntry>
                {
                    new ImportRuleEntry { Matcher = "", TargetUrl = "/target" }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(entries);
                var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

                var httpContext = new DefaultHttpContext();
                httpContext.Request.ContentType = "application/json";
                httpContext.Request.Body = stream;

                _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

                var result = await _sut.ImportPreview() as OkObjectResult;

                var response = result!.Value as ImportPreviewResponse;

                response!.Counts.Invalid.Should().Be(1);
                response.Preview[0].Status.Should().Be("invalid");
                response.Preview[0].Reason.Should().Contain("Matcher");
            }

            [Fact]
            public async Task ImportPreview_InvalidRedirectType_MarkedAsInvalid()
            {
                var entries = new List<ImportRuleEntry>
                {
                    new ImportRuleEntry { Matcher = "/path", TargetUrl = "/target", RedirectType = "invalid_type" }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(entries);
                var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

                var httpContext = new DefaultHttpContext();
                httpContext.Request.ContentType = "application/json";
                httpContext.Request.Body = stream;

                _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

                var result = await _sut.ImportPreview() as OkObjectResult;

                var response = result!.Value as ImportPreviewResponse;

                response!.Counts.Invalid.Should().Be(1);
                response.Preview[0].Reason.Should().Contain("Invalid redirect type");
            }

            [Fact]
            public async Task ImportPreview_ExistingMatcher_MarkedAsUpdate()
            {
                var existingRule = CreateRule("/existing");

                _cacheService.GetAll().Returns(new List<RedirectRule> { existingRule });

                var entries = new List<ImportRuleEntry>
                {
                    new ImportRuleEntry { Matcher = "/existing", TargetUrl = "/new-target", RedirectType = "partial" }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(entries);
                var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

                var httpContext = new DefaultHttpContext();
                httpContext.Request.ContentType = "application/json";
                httpContext.Request.Body = stream;

                _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

                var result = await _sut.ImportPreview() as OkObjectResult;

                var response = result!.Value as ImportPreviewResponse;

                response!.Counts.Update.Should().Be(1);
                response.Preview[0].Status.Should().Be("update");
                response.Preview[0].ExistingRuleId.Should().Be(existingRule.Id.ToString());
            }

            [Fact]
            public async Task ImportPreview_ExistingById_MarkedAsUpdate()
            {
                var existingRule = CreateRule("/existing");

                _cacheService.GetAll().Returns(new List<RedirectRule> { existingRule });
                _cacheService.GetById(existingRule.Id).Returns(existingRule);

                var entries = new List<ImportRuleEntry>
                {
                    new ImportRuleEntry
                    {
                        Id = existingRule.Id.ToString(),
                        Matcher = "/different-matcher",
                        TargetUrl = "/target",
                        RedirectType = "partial"
                    }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(entries);
                var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

                var httpContext = new DefaultHttpContext();
                httpContext.Request.ContentType = "application/json";
                httpContext.Request.Body = stream;

                _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

                var result = await _sut.ImportPreview() as OkObjectResult;

                var response = result!.Value as ImportPreviewResponse;

                response!.Counts.Update.Should().Be(1);
            }

            [Fact]
            public async Task ImportPreview_EmptyEntries_ReturnsBadRequest()
            {
                var json = "[]";
                var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

                var httpContext = new DefaultHttpContext();
                httpContext.Request.ContentType = "application/json";
                httpContext.Request.Body = stream;

                _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

                var result = await _sut.ImportPreview();

                result.Should().BeOfType<BadRequestObjectResult>();
            }

            [Fact]
            public async Task ImportPreview_InvalidJson_ReturnsBadRequest()
            {
                var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not-json"));

                var httpContext = new DefaultHttpContext();
                httpContext.Request.ContentType = "application/json";
                httpContext.Request.Body = stream;

                _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

                var result = await _sut.ImportPreview();

                result.Should().BeOfType<BadRequestObjectResult>();
            }

            [Fact]
            public async Task ImportPreview_MixedEntries_CorrectCountBreakdown()
            {
                var existingRule = CreateRule("/existing");

                _cacheService.GetAll().Returns(new List<RedirectRule> { existingRule });

                var entries = new List<ImportRuleEntry>
                {
                    new ImportRuleEntry { Matcher = "/brand-new", TargetUrl = "/target", RedirectType = "partial" },
                    new ImportRuleEntry { Matcher = "/existing", TargetUrl = "/updated", RedirectType = "partial" },
                    new ImportRuleEntry { Matcher = "", TargetUrl = "/target" }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(entries);
                var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

                var httpContext = new DefaultHttpContext();
                httpContext.Request.ContentType = "application/json";
                httpContext.Request.Body = stream;

                _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

                var result = await _sut.ImportPreview() as OkObjectResult;

                var response = result!.Value as ImportPreviewResponse;

                response!.Total.Should().Be(3);
                response.Counts.New.Should().Be(1);
                response.Counts.Update.Should().Be(1);
                response.Counts.Invalid.Should().Be(1);
            }
        }

        public class ImportTests : RulesControllerTests
        {
            [Fact]
            public async Task Import_JsonBody_ReturnsJobId()
            {
                var entries = new List<ImportRuleEntry>
                {
                    new ImportRuleEntry { Matcher = "/import-me", TargetUrl = "/target", RedirectType = "partial" }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(entries);
                var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

                var httpContext = new DefaultHttpContext();
                httpContext.Request.ContentType = "application/json";
                httpContext.Request.Body = stream;

                _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };
                _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<RedirectRule>());

                var result = await _sut.Import() as OkObjectResult;

                result.Should().NotBeNull();
            }

            [Fact]
            public async Task Import_EmptyEntries_ReturnsBadRequest()
            {
                var json = "[]";
                var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

                var httpContext = new DefaultHttpContext();
                httpContext.Request.ContentType = "application/json";
                httpContext.Request.Body = stream;

                _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

                var result = await _sut.Import();

                result.Should().BeOfType<BadRequestObjectResult>();
            }

            [Fact]
            public async Task Import_InvalidJson_ReturnsBadRequest()
            {
                var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{invalid}"));

                var httpContext = new DefaultHttpContext();
                httpContext.Request.ContentType = "application/json";
                httpContext.Request.Body = stream;

                _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

                var result = await _sut.Import();

                result.Should().BeOfType<BadRequestObjectResult>();
            }
        }

        public class ExportAdditionalTests : RulesControllerTests
        {
            [Fact]
            public void Export_UnknownFormat_DefaultsToJson()
            {
                _cacheService.GetAll().Returns(new List<RedirectRule> { CreateRule() });

                var result = _sut.Export("unknown") as FileContentResult;

                result.Should().NotBeNull();
                result!.ContentType.Should().Be("application/json");
                result.FileDownloadName.Should().Be("rules.json");
            }

            [Fact]
            public void Export_EmptyRules_StillReturnsFile()
            {
                _cacheService.GetAll().Returns(new List<RedirectRule>());

                var result = _sut.Export("json") as FileContentResult;

                result.Should().NotBeNull();
                result!.ContentType.Should().Be("application/json");
            }
        }

        public class UpdateAdditionalTests : RulesControllerTests
        {
            [Fact]
            public async Task Update_AllFieldsProvided_UpdatesAllFields()
            {
                var rule = CreateRule("/original");
                rule.InfoText = "old info";
                rule.AutoRedirect = false;

                _cacheService.GetById(rule.Id).Returns(rule);
                _cacheService.MatcherExists(Arg.Any<string>(), Arg.Any<Guid?>()).Returns(false);
                _cacheService.FindOverlappingMatcher(Arg.Any<string>(), Arg.Any<Guid?>()).Returns((RedirectRule?)null);

                var request = new UpdateRuleRequest
                {
                    Matcher = "/updated",
                    TargetUrl = "/new-target",
                    InfoText = "new info",
                    AutoRedirect = true,
                    RedirectType = "wildcard"
                };

                var result = await _sut.Update(rule.Id, request) as OkObjectResult;

                result.Should().NotBeNull();

                var updated = result!.Value as RedirectRule;

                updated!.Matcher.Should().Be("/updated");
                updated.TargetUrl.Should().Be("/new-target");
                updated.InfoText.Should().Be("new info");
                updated.AutoRedirect.Should().BeTrue();
                updated.RedirectType.Should().Be(RedirectType.Wildcard);
            }

            [Fact]
            public async Task Update_PartialUpdate_PreservesUnchangedFields()
            {
                var rule = CreateRule("/original");
                rule.TargetUrl = "/original-target";
                rule.InfoText = "original info";

                _cacheService.GetById(rule.Id).Returns(rule);

                var request = new UpdateRuleRequest { InfoText = "updated info" };

                var result = await _sut.Update(rule.Id, request) as OkObjectResult;

                var updated = result!.Value as RedirectRule;

                updated!.Matcher.Should().Be("/original");
                updated.TargetUrl.Should().Be("/original-target");
                updated.InfoText.Should().Be("updated info");
            }
        }
    }
}
