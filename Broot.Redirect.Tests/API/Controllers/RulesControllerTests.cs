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
    }
}
