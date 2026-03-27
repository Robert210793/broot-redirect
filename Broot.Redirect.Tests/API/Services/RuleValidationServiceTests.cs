using Broot.Redirect.API.Dtos;
using Broot.Redirect.API.Services;
using Broot.Redirect.Core.Models;
using FluentAssertions;
using Xunit;

namespace Broot.Redirect.Tests.API.Services
{
    public class RuleValidationServiceTests
    {
        private readonly RuleValidationService _sut = new();

        private static CreateRuleRequest CreateValidRequest()
        {
            return new CreateRuleRequest
            {
                Matcher = "/valid-path",
                TargetUrl = "/new-path",
                RedirectType = "partial"
            };
        }

        [Fact]
        public void ValidateCreate_EmptyMatcher_ReturnsError()
        {
            var request = CreateValidRequest();
            request.Matcher = "";

            var errors = _sut.ValidateCreate(request);

            errors.Should().NotBeEmpty();
        }

        [Fact]
        public void ValidateCreate_MatcherTooLong_ReturnsError()
        {
            var request = CreateValidRequest();
            request.Matcher = "/" + new string('a', 500);

            var errors = _sut.ValidateCreate(request);

            errors.Should().Contain(error => error.Contains("lang"));
        }

        [Fact]
        public void ValidateCreate_InvalidMatcherPattern_ReturnsError()
        {
            var request = CreateValidRequest();
            request.Matcher = " invalid";

            var errors = _sut.ValidateCreate(request);

            errors.Should().NotBeEmpty();
        }

        [Fact]
        public void ValidateCreate_ValidMatcher_ReturnsNoErrors()
        {
            var request = CreateValidRequest();

            var errors = _sut.ValidateCreate(request);

            errors.Should().BeEmpty();
        }

        [Fact]
        public void ValidateCreate_WildcardWithoutHttp_ReturnsError()
        {
            var request = new CreateRuleRequest
            {
                Matcher = "/old-page",
                TargetUrl = "new.com/page",
                RedirectType = "wildcard"
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().Contain(error => error.Contains("http"));
        }

        [Fact]
        public void ValidateCreate_PartialWithoutSlashOrHttp_ReturnsError()
        {
            var request = new CreateRuleRequest
            {
                Matcher = "/old-page",
                TargetUrl = "relative-path",
                RedirectType = "partial"
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().NotBeEmpty();
        }

        [Fact]
        public void ValidateCreate_DomainWithPath_ReturnsError()
        {
            var request = new CreateRuleRequest
            {
                Matcher = "example.com",
                TargetUrl = "https://new.com/subdir",
                RedirectType = "domain"
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().Contain(error => error.Contains("Unterordner"));
        }

        [Fact]
        public void ValidateCreate_KeptQueryParamsEmptyKeyPattern_ReturnsError()
        {
            var request = CreateValidRequest();
            request.KeptQueryParams = new List<KeptQueryParam>
            {
                new KeptQueryParam { KeyPattern = "" }
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().Contain(error => error.Contains("KeyPattern"));
        }

        [Fact]
        public void ValidateCreate_StaticQueryParamsEmptyKey_ReturnsError()
        {
            var request = CreateValidRequest();
            request.StaticQueryParams = new List<StaticQueryParam>
            {
                new StaticQueryParam { Key = "" }
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().Contain(error => error.Contains("Key"));
        }

        [Fact]
        public void ValidateCreate_SearchAndReplaceEmptySearch_ReturnsError()
        {
            var request = CreateValidRequest();
            request.SearchAndReplace = new List<SearchAndReplaceEntry>
            {
                new SearchAndReplaceEntry { Search = "" }
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().Contain(error => error.Contains("Suchbegriff"));
        }

        [Fact]
        public void ValidateCreate_ValidCompleteRule_ReturnsNoErrors()
        {
            var request = new CreateRuleRequest
            {
                Matcher = "/valid-path",
                TargetUrl = "/new-path",
                RedirectType = "partial",
                InfoText = "Some info text",
                AutoRedirect = true,
                KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^utm_" }
                },
                StaticQueryParams = new List<StaticQueryParam>
                {
                    new StaticQueryParam { Key = "source", Value = "redirect" }
                },
                SearchAndReplace = new List<SearchAndReplaceEntry>
                {
                    new SearchAndReplaceEntry { Search = "old", Replace = "new" }
                }
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().BeEmpty();
        }

        [Fact]
        public void ValidateCreate_TargetUrlTooLong_ReturnsError()
        {
            var request = CreateValidRequest();
            request.TargetUrl = "/" + new string('a', 2000);

            var errors = _sut.ValidateCreate(request);

            errors.Should().Contain(error => error.Contains("lang"));
        }

        [Fact]
        public void ValidateCreate_InfoTextTooLong_ReturnsError()
        {
            var request = CreateValidRequest();
            request.InfoText = new string('a', 5001);

            var errors = _sut.ValidateCreate(request);

            errors.Should().Contain(error => error.Contains("lang"));
        }

        [Fact]
        public void ValidateCreate_WildcardWithHttps_ReturnsNoError()
        {
            var request = new CreateRuleRequest
            {
                Matcher = "/page",
                TargetUrl = "https://new.com/page",
                RedirectType = "wildcard"
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().BeEmpty();
        }

        [Fact]
        public void ValidateCreate_PartialWithSlashPrefix_ReturnsNoError()
        {
            var request = new CreateRuleRequest
            {
                Matcher = "/old",
                TargetUrl = "/new",
                RedirectType = "partial"
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().BeEmpty();
        }

        [Fact]
        public void ValidateCreate_DomainWithValidUrl_ReturnsNoError()
        {
            var request = new CreateRuleRequest
            {
                Matcher = "example.com",
                TargetUrl = "https://new.com",
                RedirectType = "domain"
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().BeEmpty();
        }

        [Fact]
        public void ValidateCreate_DomainWithoutHttp_ReturnsError()
        {
            var request = new CreateRuleRequest
            {
                Matcher = "example.com",
                TargetUrl = "new.com",
                RedirectType = "domain"
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().NotBeEmpty();
        }

        [Fact]
        public void ValidateCreate_NullTargetUrl_NoTypeError()
        {
            var request = new CreateRuleRequest
            {
                Matcher = "/valid",
                TargetUrl = null,
                RedirectType = "wildcard"
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().BeEmpty();
        }

        [Fact]
        public void ValidateCreate_DomainMatcher_ReturnsNoErrors()
        {
            var request = new CreateRuleRequest
            {
                Matcher = "example.com",
                TargetUrl = "https://new.com",
                RedirectType = "domain"
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().BeEmpty();
        }

        [Fact]
        public void ValidateCreate_MultipleKeptQueryParams_ValidatesEach()
        {
            var request = CreateValidRequest();
            request.KeptQueryParams = new List<KeptQueryParam>
            {
                new KeptQueryParam { KeyPattern = "^valid$" },
                new KeptQueryParam { KeyPattern = "" }
            };

            var errors = _sut.ValidateCreate(request);

            errors.Should().ContainSingle();
        }
    }
}
