using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Broot.Redirect.Core.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.Core.Services
{
    public class UrlTransformServiceTests
    {
        private static RedirectRule CreateRule(
            string matcher,
            RedirectType type,
            string targetUrl = "")
        {
            return new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = matcher,
                RedirectType = type,
                TargetUrl = targetUrl,
                SearchAndReplace = new List<SearchAndReplaceEntry>(),
                KeptQueryParams = new List<KeptQueryParam>(),
                StaticQueryParams = new List<StaticQueryParam>()
            };
        }

        public class ResolveTargetUrlTests
        {
            private readonly IGlobalRuleCacheService _globalRuleCache;
            private readonly UrlTransformService _sut;

            public ResolveTargetUrlTests()
            {
                _globalRuleCache = Substitute.For<IGlobalRuleCacheService>();
                _globalRuleCache.GetAll().Returns(new List<GlobalRule>());
                _sut = new UrlTransformService(_globalRuleCache);
            }

            [Fact]
            public void ResolveTargetUrl_WildcardRule_ResolvesToTargetUrl()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://new.com/page");

                var result = _sut.ResolveTargetUrl("/old-page", rule, "https://defaultdomain.com");

                result.Should().Be("https://new.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_PartialRuleWithPathMatcher_ReplacesMatcherWithTarget()
            {
                var rule = CreateRule("/old", RedirectType.Partial, "/new");

                var result = _sut.ResolveTargetUrl("/old/subpage", rule, "https://defaultdomain.com");

                result.Should().Be("https://defaultdomain.com/new/subpage");
            }

            [Fact]
            public void ResolveTargetUrl_PartialRuleWithDomainMatcher_ReplacesDomain()
            {
                var rule = CreateRule("example.com", RedirectType.Partial, "https://new.example.com");

                var result = _sut.ResolveTargetUrl("http://example.com/path", rule, "https://defaultdomain.com");

                result.Should().Be("https://new.example.com/path");
            }

            [Fact]
            public void ResolveTargetUrl_DomainRule_ReplacesDomainKeepsPath()
            {
                var rule = CreateRule("example.com", RedirectType.Domain, "https://new.example.com");

                var result = _sut.ResolveTargetUrl("http://example.com/some/path", rule, "https://defaultdomain.com");

                result.Should().Be("https://new.example.com/some/path");
            }

            [Fact]
            public void ResolveTargetUrl_ForwardQueryParams_AppendsOriginalQuery()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.ForwardQueryParams = true;

                var result = _sut.ResolveTargetUrl(
                    "http://old.com/old-page?source=google",
                    rule,
                    "https://defaultdomain.com");

                result.Should().Be("https://new.com/page?source=google");
            }

            [Fact]
            public void ResolveTargetUrl_DiscardQueryParams_StripsQuery()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;

                var result = _sut.ResolveTargetUrl(
                    "http://old.com/old-page?q=1",
                    rule,
                    "https://defaultdomain.com");

                result.Should().Be("https://new.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_DiscardAndKeptQueryParams_OnlyKeepsMatching()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^utm_" }
                };

                var result = _sut.ResolveTargetUrl(
                    "http://old.com/old-page?utm_source=test&other=value",
                    rule,
                    "https://defaultdomain.com");

                result.Should().Be("https://new.com/page?utm_source=test");
            }

            [Fact]
            public void ResolveTargetUrl_KeptQueryParamsWithTargetKey_RenamesKey()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^source$", TargetKey = "utm_source" }
                };

                var result = _sut.ResolveTargetUrl(
                    "http://old.com/old-page?source=google",
                    rule,
                    "https://defaultdomain.com");

                result.Should().Be("https://new.com/page?utm_source=google");
            }

            [Fact]
            public void ResolveTargetUrl_KeptQueryParamsWithValuePattern_FiltersValues()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^id$", ValuePattern = @"^\d+$" }
                };

                var result = _sut.ResolveTargetUrl(
                    "http://old.com/old-page?id=123&name=test",
                    rule,
                    "https://defaultdomain.com");

                result.Should().Be("https://new.com/page?id=123");
            }

            [Fact]
            public void ResolveTargetUrl_KeptQueryParamsWithSkipEncoding_DoesNotEncode()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^url$", SkipEncoding = true }
                };

                var result = _sut.ResolveTargetUrl(
                    "http://old.com/old-page?url=https://example.com/path",
                    rule,
                    "https://defaultdomain.com");

                result.Should().Contain("url=https://example.com/path");
            }

            [Fact]
            public void ResolveTargetUrl_StaticQueryParams_AppendsToUrl()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://new.com/page");
                rule.StaticQueryParams = new List<StaticQueryParam>
                {
                    new StaticQueryParam { Key = "source", Value = "redirect" }
                };

                var result = _sut.ResolveTargetUrl("/old-page", rule, "https://defaultdomain.com");

                result.Should().Be("https://new.com/page?source=redirect");
            }

            [Fact]
            public void ResolveTargetUrl_StaticQueryParamsWithSkipEncoding_DoesNotEncode()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://new.com/page");
                rule.StaticQueryParams = new List<StaticQueryParam>
                {
                    new StaticQueryParam { Key = "redirect_url", Value = "https://example.com", SkipEncoding = true }
                };

                var result = _sut.ResolveTargetUrl("/old-page", rule, "https://defaultdomain.com");

                result.Should().Contain("redirect_url=https://example.com");
            }

            [Fact]
            public void ResolveTargetUrl_SearchAndReplace_TransformsUrl()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://example.com/old-content");
                rule.SearchAndReplace = new List<SearchAndReplaceEntry>
                {
                    new SearchAndReplaceEntry { Search = "old", Replace = "new", CaseSensitive = false }
                };

                var result = _sut.ResolveTargetUrl("/old-page", rule, "https://defaultdomain.com");

                result.Should().Be("https://example.com/new-content");
            }

            [Fact]
            public void ResolveTargetUrl_SearchAndReplaceCaseInsensitive_MatchesAnyCase()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://example.com/OLD-content");
                rule.SearchAndReplace = new List<SearchAndReplaceEntry>
                {
                    new SearchAndReplaceEntry { Search = "old", Replace = "new", CaseSensitive = false }
                };

                var result = _sut.ResolveTargetUrl("/old-page", rule, "https://defaultdomain.com");

                result.Should().Be("https://example.com/new-content");
            }

            [Fact]
            public void ResolveTargetUrl_SearchAndReplaceCaseSensitive_OnlyMatchesExactCase()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://example.com/OLD-content");
                rule.SearchAndReplace = new List<SearchAndReplaceEntry>
                {
                    new SearchAndReplaceEntry { Search = "old", Replace = "new", CaseSensitive = true }
                };

                var result = _sut.ResolveTargetUrl("/old-page", rule, "https://defaultdomain.com");

                result.Should().Be("https://example.com/OLD-content");
            }

            [Fact]
            public void ResolveTargetUrl_GlobalRulesApplied_TransformsUrl()
            {
                var globalRule = new GlobalRule
                {
                    Id = Guid.NewGuid(),
                    Search = "example.com",
                    Replace = "new-example.com",
                    CaseSensitive = false,
                    Priority = 0
                };

                _globalRuleCache.GetAll().Returns(new List<GlobalRule> { globalRule });

                var rule = CreateRule("/page", RedirectType.Wildcard, "https://example.com/page");

                var result = _sut.ResolveTargetUrl("/page", rule, "https://defaultdomain.com");

                result.Should().Be("https://new-example.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_MultipleGlobalRules_AppliedInOrder()
            {
                var globalRule1 = new GlobalRule
                {
                    Id = Guid.NewGuid(),
                    Search = "old",
                    Replace = "middle",
                    CaseSensitive = false,
                    Priority = 0
                };

                var globalRule2 = new GlobalRule
                {
                    Id = Guid.NewGuid(),
                    Search = "middle",
                    Replace = "final",
                    CaseSensitive = false,
                    Priority = 1
                };

                _globalRuleCache.GetAll().Returns(new List<GlobalRule> { globalRule1, globalRule2 });

                var rule = CreateRule("/page", RedirectType.Wildcard, "https://old.com/page");

                var result = _sut.ResolveTargetUrl("/page", rule, "https://defaultdomain.com");

                result.Should().Be("https://final.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_GlobalRuleCaseSensitive_OnlyMatchesExactCase()
            {
                var globalRule = new GlobalRule
                {
                    Id = Guid.NewGuid(),
                    Search = "Example",
                    Replace = "Other",
                    CaseSensitive = true,
                    Priority = 0
                };

                _globalRuleCache.GetAll().Returns(new List<GlobalRule> { globalRule });

                var rule = CreateRule("/page", RedirectType.Wildcard, "https://example.com/page");

                var result = _sut.ResolveTargetUrl("/page", rule, "https://defaultdomain.com");

                result.Should().Be("https://example.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_FragmentPreservation_PreservesFragment()
            {
                var rule = CreateRule("/old-path", RedirectType.Wildcard, "https://new.com/new-path");

                var result = _sut.ResolveTargetUrl(
                    "https://old.com/old-path#section",
                    rule,
                    "https://defaultdomain.com");

                result.Should().Be("https://new.com/new-path#section");
            }

            [Fact]
            public void ResolveTargetUrl_WildcardWithDomainMatcher_ReplacesDomain()
            {
                var rule = CreateRule("old.com", RedirectType.Wildcard, "https://new.com");

                var result = _sut.ResolveTargetUrl("http://old.com/path?q=1", rule, "https://default.com");

                result.Should().Be("https://new.com/path?q=1");
            }

            [Fact]
            public void ResolveTargetUrl_RegexTypeFallsToDefault_UsesGenerateNewUrl()
            {
                var rule = CreateRule("^/test", RedirectType.Regex, "");

                var result = _sut.ResolveTargetUrl("http://old.com/test", rule, "https://new.com");

                result.Should().StartWith("https://new.com");
            }

            [Fact]
            public void ResolveTargetUrl_NullDefaultDomain_UsesFallback()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://target.com/page");

                var result = _sut.ResolveTargetUrl("/page", rule, null!);

                result.Should().Be("https://target.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_PartialMatcherNotAtStart_ReplacesAtFoundIndex()
            {
                var rule = CreateRule("/middle", RedirectType.Partial, "/replaced");

                var result = _sut.ResolveTargetUrl("/prefix/middle/suffix", rule, "https://new.com");

                result.Should().Contain("/replaced");
            }

            [Fact]
            public void ResolveTargetUrl_PartialMatcherNotFound_UsesTargetOnly()
            {
                var rule = CreateRule("/nomatch", RedirectType.Partial, "/target");

                var result = _sut.ResolveTargetUrl("/completely/different", rule, "https://new.com");

                result.Should().Be("https://new.com/target");
            }

            [Fact]
            public void ResolveTargetUrl_WildcardWithSuffix_AppendsSuffix()
            {
                var rule = CreateRule("/base/", RedirectType.Wildcard, "https://new.com/base/");

                var result = _sut.ResolveTargetUrl("/base/extra/path", rule, "https://default.com");

                result.Should().Be("https://new.com/base/extra/path");
            }

            [Fact]
            public void ResolveTargetUrl_WildcardTargetWithQuery_MergesIncomingQuerySafely()
            {
                var rule = CreateRule(
                    "/ims/Library/Document.docx",
                    RedirectType.Wildcard,
                    "https://example.sharepoint.com/:w:/r/sites/docs/Document.docx?d=w123&web=1");
                rule.DiscardQueryParams = false;
                rule.ForwardQueryParams = false;

                var result = _sut.ResolveTargetUrl(
                    "/ims/Library/Document.docx?Source=%2Fims%2Fstart.aspx&download=1",
                    rule,
                    "https://default.com");

                result.Should().Be(
                    "https://example.sharepoint.com/:w:/r/sites/docs/Document.docx"
                    + "?d=w123&web=1&Source=%2Fims%2Fstart.aspx&download=1");
                result.Count(character => character == '?').Should().Be(1);
            }

            [Fact]
            public void ResolveTargetUrl_WildcardTargetFragment_KeepsTargetFragmentAfterMergedQuery()
            {
                var rule = CreateRule(
                    "/old",
                    RedirectType.Wildcard,
                    "https://new.com/page?process=abc#Dokumentenmanagementsystem");

                var result = _sut.ResolveTargetUrl("/old?Source=test", rule, "https://default.com");

                result.Should().Be(
                    "https://new.com/page?process=abc&Source=test#Dokumentenmanagementsystem");
            }

            [Fact]
            public void ResolveTargetUrl_QuerySpecificWildcard_MergesExtraEncodedQueryBeforeTargetFragment()
            {
                var rule = CreateRule(
                    "/ims/Strategie/Forms/AllItems.aspx?RootFolder=%2Fims%2FStrategie",
                    RedirectType.Wildcard,
                    "https://new.com/Prozess.aspx?ProzessId=abc#Dokumentenmanagementsystem");

                var result = _sut.ResolveTargetUrl(
                    "/ims/Strategie/Forms/AllItems.aspx?RootFolder=%2Fims%2FStrategie&Source=%2Fims%2Fstart.aspx",
                    rule,
                    "https://default.com");

                result.Should().Be(
                    "https://new.com/Prozess.aspx?ProzessId=abc&Source=%2Fims%2Fstart.aspx#Dokumentenmanagementsystem");
            }

            [Fact]
            public void ResolveTargetUrl_ForwardQueryParamsWithTargetQuery_DoesNotDuplicateIncomingQuery()
            {
                var rule = CreateRule("/old", RedirectType.Wildcard, "https://new.com/page?fixed=1");
                rule.ForwardQueryParams = true;

                var result = _sut.ResolveTargetUrl("/old?source=test", rule, "https://default.com");

                result.Should().Be("https://new.com/page?fixed=1&source=test");
                result.Split("source=test").Length.Should().Be(2);
            }

            [Fact]
            public void ResolveTargetUrl_WildcardNoMatchPrefix_FallsBackToCleanDomain()
            {
                var rule = CreateRule("/specific", RedirectType.Wildcard, "https://new.com/specific");

                var result = _sut.ResolveTargetUrl("/different/path", rule, "https://default.com");

                result.Should().StartWith("https://default.com");
            }

            [Fact]
            public void ResolveTargetUrl_DomainWithEmptyTargetUrl_UsesDefaultDomain()
            {
                var rule = CreateRule("old.com", RedirectType.Domain, "");

                var result = _sut.ResolveTargetUrl("http://old.com/path", rule, "https://default.com");

                result.Should().Be("https://default.com/path");
            }

            [Fact]
            public void ResolveTargetUrl_PartialDomainMatcherWithRelativeTarget_CombinesWithDefault()
            {
                var rule = CreateRule("old.com", RedirectType.Partial, "/newbase");

                var result = _sut.ResolveTargetUrl("http://old.com/page", rule, "https://default.com");

                result.Should().Be("https://default.com/newbase/page");
            }

            [Fact]
            public void ResolveTargetUrl_DiscardQueryParamsWithFragment_PreservesFragment()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;

                var result = _sut.ResolveTargetUrl("http://old.com/page?q=1#frag", rule, "https://default.com");

                result.Should().Contain("#frag");
                result.Should().NotContain("q=1");
            }

            [Fact]
            public void ResolveTargetUrl_ForwardQueryParamsNoOriginalQuery_ReturnsUnchanged()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.ForwardQueryParams = true;

                var result = _sut.ResolveTargetUrl("http://old.com/page", rule, "https://default.com");

                result.Should().Be("https://new.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_KeptQueryParamsNoMatch_ReturnsUrlWithoutQuery()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^nonexistent$" }
                };

                var result = _sut.ResolveTargetUrl(
                    "http://old.com/page?other=value",
                    rule,
                    "https://default.com");

                result.Should().Be("https://new.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_StaticQueryParamsEmptyKey_SkipsEmpty()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.StaticQueryParams = new List<StaticQueryParam>
                {
                    new StaticQueryParam { Key = "", Value = "ignored" },
                    new StaticQueryParam { Key = "valid", Value = "kept" }
                };

                var result = _sut.ResolveTargetUrl("/page", rule, "https://default.com");

                result.Should().Be("https://new.com/page?valid=kept");
            }

            [Fact]
            public void ResolveTargetUrl_WildcardRelativeTarget_PrependsDomain()
            {
                var rule = CreateRule("/old", RedirectType.Wildcard, "newpath");

                var result = _sut.ResolveTargetUrl("/old/extra", rule, "https://default.com");

                result.Should().StartWith("https://default.com/");
            }

            [Fact]
            public void ResolveTargetUrl_WildcardDomainMatcher_ResolvesDomain()
            {
                var rule = CreateRule("old.example.com", RedirectType.Wildcard, "https://new.example.com");

                var result = _sut.ResolveTargetUrl("http://old.example.com/some/path", rule, "https://default.com");

                result.Should().Be("https://new.example.com/some/path");
            }

            [Fact]
            public void ResolveTargetUrl_PartialMiddleMatch_ReplacesCorrectly()
            {
                var rule = CreateRule("/middle", RedirectType.Partial, "/replaced");

                var result = _sut.ResolveTargetUrl("/before/middle/after", rule, "https://default.com");

                result.Should().Contain("/replaced");
                result.Should().Contain("/after");
            }

            [Fact]
            public void ResolveTargetUrl_PartialNoMatch_FallsBackToTarget()
            {
                var rule = CreateRule("/nonexistent", RedirectType.Partial, "/target");

                var result = _sut.ResolveTargetUrl("/completely-different", rule, "https://default.com");

                result.Should().Be("https://default.com/target");
            }

            [Fact]
            public void ResolveTargetUrl_PartialWithDiscardQueryParams_StripsQuery()
            {
                var rule = CreateRule("/old", RedirectType.Partial, "/new");
                rule.DiscardQueryParams = true;

                var result = _sut.ResolveTargetUrl("/old/page?q=1", rule, "https://default.com");

                result.Should().NotContain("q=1");
            }

            [Fact]
            public void ResolveTargetUrl_WildcardWithDiscardQueryParams_StripsQuery()
            {
                var rule = CreateRule("/old", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;

                var result = _sut.ResolveTargetUrl("/old?q=1", rule, "https://default.com");

                result.Should().Be("https://new.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_WildcardMatcherWithTrailingSlash_HandlesSlashCorrectly()
            {
                var rule = CreateRule("/old/", RedirectType.Wildcard, "https://new.com/target/");

                var result = _sut.ResolveTargetUrl("/old/subpath", rule, "https://default.com");

                result.Should().Be("https://new.com/target/subpath");
            }

            [Fact]
            public void ResolveTargetUrl_PartialDomainMatcherRelativeTarget_UsesCleanDomain()
            {
                var rule = CreateRule("example.com", RedirectType.Partial, "/relative");

                var result = _sut.ResolveTargetUrl("http://example.com/some/path", rule, "https://default.com");

                result.Should().Contain("default.com");
                result.Should().Contain("/relative");
            }

            [Fact]
            public void ResolveTargetUrl_KeptQueryParamsInvalidKeyRegex_Skipped()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "[invalid" },
                    new KeptQueryParam { KeyPattern = "^valid$" }
                };

                var result = _sut.ResolveTargetUrl(
                    "http://old.com/page?valid=yes&other=no",
                    rule,
                    "https://default.com");

                result.Should().Contain("valid=yes");
                result.Should().NotContain("other=no");
            }

            [Fact]
            public void ResolveTargetUrl_KeptQueryParamsInvalidValueRegex_Skipped()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^key$", ValuePattern = "[invalid" }
                };

                var result = _sut.ResolveTargetUrl(
                    "http://old.com/page?key=value",
                    rule,
                    "https://default.com");

                // Invalid value regex causes the rule to be skipped
                result.Should().Be("https://new.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_KeptQueryParamsEmptyKeyPattern_Skipped()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "" }
                };

                var result = _sut.ResolveTargetUrl(
                    "http://old.com/page?key=value",
                    rule,
                    "https://default.com");

                result.Should().Be("https://new.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_DefaultRedirectType_UsesGenerateNewUrl()
            {
                var rule = CreateRule("/old", (RedirectType)99, "https://new.com");

                var result = _sut.ResolveTargetUrl("http://old.com/old", rule, "https://default.com");

                result.Should().Contain("default.com");
            }

            [Fact]
            public void ResolveTargetUrl_DomainWithNoTarget_UsesDefaultDomain()
            {
                var rule = CreateRule("old.com", RedirectType.Domain, "");

                var result = _sut.ResolveTargetUrl("http://old.com/path", rule, "https://default.com");

                result.Should().Contain("default.com");
                result.Should().Contain("/path");
            }

            [Fact]
            public void ResolveTargetUrl_ForwardQueryParamsNoQuery_NoChange()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.ForwardQueryParams = true;

                var result = _sut.ResolveTargetUrl("/page", rule, "https://default.com");

                result.Should().Be("https://new.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_KeptQueryParamsValuePatternMismatch_NotKept()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^id$", ValuePattern = @"^\d+$" }
                };

                var result = _sut.ResolveTargetUrl(
                    "http://old.com/page?id=abc",
                    rule,
                    "https://default.com");

                // Value "abc" doesn't match pattern ^\d+$
                result.Should().Be("https://new.com/page");
            }

            [Fact]
            public void ResolveTargetUrl_FragmentInQuery_PreservedWithStripQueryParams()
            {
                var rule = CreateRule("/old", RedirectType.Partial, "/new");
                rule.DiscardQueryParams = true;

                var result = _sut.ResolveTargetUrl("/old?q=1#section", rule, "https://default.com");

                result.Should().Contain("#section");
                result.Should().NotContain("q=1");
            }

            [Fact]
            public void ResolveTargetUrl_NeitherForwardNorDiscard_NoExtraQueryAppended()
            {
                // Partial rule: with DiscardQueryParams=false and ForwardQueryParams=false,
                // no explicit query forwarding or kept-params logic runs.
                var rule = CreateRule("/old", RedirectType.Partial, "/new");
                rule.ForwardQueryParams = false;
                rule.DiscardQueryParams = false;

                var result = _sut.ResolveTargetUrl(
                    "/old/sub?source=google",
                    rule,
                    "https://defaultdomain.com");

                // Partial resolution includes the query in the replaced path,
                // but ForwardOriginalQueryParams is NOT called, so no duplication
                var queryCount = result.Split("source=google").Length - 1;
                queryCount.Should().BeLessOrEqualTo(1);
            }

            [Fact]
            public void ResolveTargetUrl_MultipleSearchAndReplace_AppliesAll()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard, "https://new.com/old-page");
                rule.SearchAndReplace = new List<SearchAndReplaceEntry>
                {
                    new SearchAndReplaceEntry { Search = "old", Replace = "new", CaseSensitive = false },
                    new SearchAndReplaceEntry { Search = "page", Replace = "site", CaseSensitive = false }
                };

                var result = _sut.ResolveTargetUrl("/old-page", rule, "https://default.com");

                result.Should().Be("https://new.com/new-site");
            }

            [Fact]
            public void ResolveTargetUrl_MultipleGlobalRules_AppliesAll()
            {
                _globalRuleCache.GetAll().Returns(new List<GlobalRule>
                {
                    new GlobalRule { Search = "aaa", Replace = "bbb" },
                    new GlobalRule { Search = "bbb", Replace = "ccc" }
                });

                var rule = CreateRule("/aaa", RedirectType.Wildcard, "https://new.com/aaa");

                var result = _sut.ResolveTargetUrl("/aaa", rule, "https://default.com");

                result.Should().Be("https://new.com/ccc");
            }
        }

        public class ResolveTargetUrlWithTraceTests
        {
            private readonly IGlobalRuleCacheService _globalRuleCache;
            private readonly UrlTransformService _sut;

            public ResolveTargetUrlWithTraceTests()
            {
                _globalRuleCache = Substitute.For<IGlobalRuleCacheService>();
                _globalRuleCache.GetAll().Returns(new List<GlobalRule>());
                _sut = new UrlTransformService(_globalRuleCache);
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_StartsWithInput_EndsWithResult()
            {
                var rule = CreateRule("/old", RedirectType.Wildcard, "https://new.com/page");

                var (resolvedUrl, trace) = _sut.ResolveTargetUrlWithTrace("/old", rule, "https://new.com");

                trace.First().Type.Should().Be("input");
                trace.Last().Type.Should().Be("result");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_SearchReplace_OnlyAppearsWhenUrlChanged()
            {
                var rule = CreateRule("/old", RedirectType.Wildcard, "https://new.com/page");
                rule.SearchAndReplace = new List<SearchAndReplaceEntry>
                {
                    new SearchAndReplaceEntry { Search = "nonexistent", Replace = "other" }
                };

                var (resolvedUrl, trace) = _sut.ResolveTargetUrlWithTrace("/old", rule, "https://new.com");

                trace.Should().NotContain(step => step.Type == "search-replace");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_GlobalRule_OnlyAppearsWhenUrlChanged()
            {
                var globalRule = new GlobalRule
                {
                    Id = Guid.NewGuid(),
                    Search = "nonexistent",
                    Replace = "other",
                    CaseSensitive = false,
                    Priority = 0
                };

                _globalRuleCache.GetAll().Returns(new List<GlobalRule> { globalRule });

                var rule = CreateRule("/old", RedirectType.Wildcard, "https://new.com/page");

                var (resolvedUrl, trace) = _sut.ResolveTargetUrlWithTrace("/old", rule, "https://new.com");

                trace.Should().NotContain(step => step.Type == "global-rule");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_FinalTraceAfter_MatchesReturnedUrl()
            {
                var rule = CreateRule("/old", RedirectType.Wildcard, "https://new.com/page");

                var (resolvedUrl, trace) = _sut.ResolveTargetUrlWithTrace("/old", rule, "https://new.com");

                trace.Last().After.Should().Be(resolvedUrl);
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_SearchReplaceChanges_AppearsInTrace()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://old.com/page");
                rule.SearchAndReplace = new List<SearchAndReplaceEntry>
                {
                    new SearchAndReplaceEntry { Search = "old", Replace = "new", CaseSensitive = false }
                };

                var (resolvedUrl, trace) = _sut.ResolveTargetUrlWithTrace("/page", rule, "https://default.com");

                trace.Should().Contain(step => step.Type == "search-replace");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_GlobalRuleChanges_AppearsInTrace()
            {
                var globalRule = new GlobalRule
                {
                    Id = Guid.NewGuid(),
                    Search = "default",
                    Replace = "updated",
                    CaseSensitive = false,
                    Priority = 0
                };

                _globalRuleCache.GetAll().Returns(new List<GlobalRule> { globalRule });

                var rule = CreateRule("/page", RedirectType.Wildcard, "https://default.com/page");

                var (resolvedUrl, trace) = _sut.ResolveTargetUrlWithTrace("/page", rule, "https://default.com");

                trace.Should().Contain(step => step.Type == "global-rule");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_ForwardQueryParams_AppearsInTrace()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.ForwardQueryParams = true;

                var (resolvedUrl, trace) = _sut.ResolveTargetUrlWithTrace(
                    "http://old.com/page?source=test",
                    rule,
                    "https://default.com");

                trace.Should().Contain(step => step.Type == "query-forward");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_KeptQueryParams_AppearsInTrace()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^utm_" }
                };

                var (resolvedUrl, trace) = _sut.ResolveTargetUrlWithTrace(
                    "http://old.com/page?utm_source=test",
                    rule,
                    "https://default.com");

                trace.Should().Contain(step => step.Type == "query-kept");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_DiscardQueryParamsOnly_AppearsInTrace()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;

                var (resolvedUrl, trace) = _sut.ResolveTargetUrlWithTrace(
                    "http://old.com/page?q=1",
                    rule,
                    "https://default.com");

                trace.Should().Contain(step => step.Type == "query-discard");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_StaticQueryParams_AppearsInTrace()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.StaticQueryParams = new List<StaticQueryParam>
                {
                    new StaticQueryParam { Key = "ref", Value = "redirect" }
                };

                var (resolvedUrl, trace) = _sut.ResolveTargetUrlWithTrace("/page", rule, "https://default.com");

                trace.Should().Contain(step => step.Type == "query-static");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_PartialRule_IncludesDomainReplaceStep()
            {
                var rule = CreateRule("/old", RedirectType.Partial, "/new");

                var (resolvedUrl, trace) = _sut.ResolveTargetUrlWithTrace("/old/sub", rule, "https://default.com");

                trace.Should().Contain(step => step.Type == "domain-replace");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_DomainRule_IncludesDomainReplaceStep()
            {
                var rule = CreateRule("old.com", RedirectType.Domain, "https://new.com");

                var (resolvedUrl, trace) = _sut.ResolveTargetUrlWithTrace(
                    "http://old.com/path",
                    rule,
                    "https://default.com");

                trace.Should().Contain(step => step.Type == "domain-replace");
                trace.Last().After.Should().Be(resolvedUrl);
            }
        }

        public class StaticHelperTests
        {
            [Fact]
            public void ExtractPath_AbsoluteUrl_ReturnsPathQueryAndFragment()
            {
                var result = UrlTransformService.ExtractPath("https://old.com/path?q=1#frag");

                result.Should().Be("/path?q=1#frag");
            }

            [Fact]
            public void ExtractPath_RelativePath_ReturnsPath()
            {
                var result = UrlTransformService.ExtractPath("/relative/path");

                result.Should().Be("/relative/path");
            }

            [Fact]
            public void GenerateNewUrl_HttpToHttps_ReplacesDomain()
            {
                var result = UrlTransformService.GenerateNewUrl("http://old.com/path", "https://new.com");

                result.Should().Be("https://new.com/path");
            }

            [Fact]
            public void AppendQueryString_NoExistingQuery_AddsQuery()
            {
                var result = UrlTransformService.AppendQueryString("https://example.com/path", "?a=1");

                result.Should().Be("https://example.com/path?a=1");
            }

            [Fact]
            public void AppendQueryString_ExistingQuery_AppendsWithAmpersand()
            {
                var result = UrlTransformService.AppendQueryString("https://example.com/path?x=1", "?a=1");

                result.Should().Be("https://example.com/path?x=1&a=1");
            }

            [Fact]
            public void AppendQueryString_WithFragment_PlacesQueryBeforeFragment()
            {
                var result = UrlTransformService.AppendQueryString("https://example.com/path#frag", "?a=1");

                result.Should().Be("https://example.com/path?a=1#frag");
            }

            [Fact]
            public void ApplySearchAndReplace_EmptySearch_ReturnsUnchanged()
            {
                var entry = new SearchAndReplaceEntry { Search = "", Replace = "new" };

                var result = UrlTransformService.ApplySearchAndReplace("https://example.com/old", entry);

                result.Should().Be("https://example.com/old");
            }

            [Fact]
            public void AppendQueryString_EmptyQueryString_ReturnsOriginal()
            {
                var result = UrlTransformService.AppendQueryString("https://example.com/path", "");

                result.Should().Be("https://example.com/path");
            }

            [Fact]
            public void GenerateNewUrl_BareHostname_PrependHttpsAndReplaceDomain()
            {
                var result = UrlTransformService.GenerateNewUrl("old.com/path", "https://new.com");

                result.Should().Be("https://new.com/path");
            }

            [Fact]
            public void GenerateNewUrl_NullDomain_UsesFallback()
            {
                var result = UrlTransformService.GenerateNewUrl("http://old.com/path", null);

                result.Should().Contain("/path");
            }

            [Fact]
            public void ExtractPath_BareHostWithPath_ReturnsFullInput()
            {
                var result = UrlTransformService.ExtractPath("old.com/path");

                result.Should().Be("/old.com/path");
            }

            [Fact]
            public void ApplyGlobalRule_EmptySearch_ReturnsUnchanged()
            {
                var globalRule = new GlobalRule { Search = "", Replace = "new" };

                var result = UrlTransformService.ApplyGlobalRule("https://example.com/path", globalRule);

                result.Should().Be("https://example.com/path");
            }

            [Fact]
            public void ApplySearchAndReplace_ValidReplace_TransformsUrl()
            {
                var entry = new SearchAndReplaceEntry { Search = "old", Replace = "new", CaseSensitive = false };

                var result = UrlTransformService.ApplySearchAndReplace("https://old.com/old-path", entry);

                result.Should().Be("https://new.com/new-path");
            }

            [Fact]
            public void AppendQueryString_ExistingQueryAndFragment_AppendsCorrectly()
            {
                var result = UrlTransformService.AppendQueryString("https://example.com/path?a=1#frag", "?b=2");

                result.Should().Be("https://example.com/path?a=1&b=2#frag");
            }

            [Fact]
            public void AppendQueryString_NullQueryString_ReturnsOriginal()
            {
                var result = UrlTransformService.AppendQueryString("https://example.com/path", "");

                result.Should().Be("https://example.com/path");
            }

            [Fact]
            public void AppendQueryString_FragmentOnly_PreservesFragment()
            {
                var result = UrlTransformService.AppendQueryString("https://example.com/path#section", "?a=1");

                result.Should().Be("https://example.com/path?a=1#section");
            }

            [Fact]
            public void ApplySearchAndReplace_NullSearch_ReturnsUnchanged()
            {
                var entry = new SearchAndReplaceEntry { Search = null!, Replace = "new" };

                var result = UrlTransformService.ApplySearchAndReplace("https://example.com", entry);

                result.Should().Be("https://example.com");
            }

            [Fact]
            public void ApplySearchAndReplace_CaseSensitive_OnlyMatchesExactCase()
            {
                var entry = new SearchAndReplaceEntry { Search = "Old", Replace = "New", CaseSensitive = true };

                var result = UrlTransformService.ApplySearchAndReplace("https://Old.com/old-path", entry);

                result.Should().Be("https://New.com/old-path");
            }

            [Fact]
            public void ApplyGlobalRule_CaseSensitive_OnlyMatchesExactCase()
            {
                var globalRule = new GlobalRule { Search = "Test", Replace = "New", CaseSensitive = true };

                var result = UrlTransformService.ApplyGlobalRule("https://Test.com/test-path", globalRule);

                result.Should().Be("https://New.com/test-path");
            }

            [Fact]
            public void ApplyGlobalRule_CaseInsensitive_MatchesBothCases()
            {
                var globalRule = new GlobalRule { Search = "old", Replace = "new", CaseSensitive = false };

                var result = UrlTransformService.ApplyGlobalRule("https://OLD.com/Old-path", globalRule);

                result.Should().Be("https://new.com/new-path");
            }

            [Fact]
            public void ExtractPath_FullHttpsUrl_ReturnsPathAndQuery()
            {
                var result = UrlTransformService.ExtractPath("https://example.com/path?q=1");

                result.Should().Be("/path?q=1");
            }

            [Fact]
            public void ExtractPath_RelativePathWithoutSlash_PrependsSlash()
            {
                var result = UrlTransformService.ExtractPath("relative-path");

                result.Should().StartWith("/");
            }

            [Fact]
            public void GenerateNewUrl_HttpUrl_ConvertedToHttps()
            {
                var result = UrlTransformService.GenerateNewUrl("http://old.com/path", "https://new.com");

                result.Should().Be("https://new.com/path");
            }

            [Fact]
            public void ApplyGlobalRule_NullReplace_UsesEmptyString()
            {
                var globalRule = new GlobalRule { Search = "remove-me", Replace = null, CaseSensitive = false };

                var result = UrlTransformService.ApplyGlobalRule("https://example.com/remove-me/path", globalRule);

                result.Should().Be("https://example.com//path");
            }

            [Fact]
            public void ApplySearchAndReplace_NullReplace_UsesEmptyString()
            {
                var entry = new SearchAndReplaceEntry { Search = "remove", Replace = null, CaseSensitive = false };

                var result = UrlTransformService.ApplySearchAndReplace("https://example.com/remove-this", entry);

                result.Should().Be("https://example.com/-this");
            }

            [Fact]
            public void AppendQueryString_QueryWithoutQuestionMark_HandledCorrectly()
            {
                var result = UrlTransformService.AppendQueryString("https://example.com/path", "a=1");

                result.Should().Be("https://example.com/path?a=1");
            }
        }

        public class ResolveTargetUrlWithTraceEdgeCases
        {
            private readonly IGlobalRuleCacheService _globalRuleCache;
            private readonly UrlTransformService _sut;

            public ResolveTargetUrlWithTraceEdgeCases()
            {
                _globalRuleCache = Substitute.For<IGlobalRuleCacheService>();
                _globalRuleCache.GetAll().Returns(new List<GlobalRule>());
                _sut = new UrlTransformService(_globalRuleCache);
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_DiscardQueryParamsNoKept_ShowsDiscardStep()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.ForwardQueryParams = false;

                var (_, trace) = _sut.ResolveTargetUrlWithTrace("/page?q=1", rule, "https://default.com");

                trace.Should().Contain(step => step.Type == "query-discard");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_SearchAndReplaceNoChange_NoTraceStep()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.SearchAndReplace = new List<SearchAndReplaceEntry>
                {
                    new SearchAndReplaceEntry { Search = "nonexistent", Replace = "new" }
                };

                var (_, trace) = _sut.ResolveTargetUrlWithTrace("/page", rule, "https://default.com");

                trace.Should().NotContain(step => step.Type == "search-replace");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_GlobalRuleNoChange_NoTraceStep()
            {
                _globalRuleCache.GetAll().Returns(new List<GlobalRule>
                {
                    new GlobalRule { Search = "nonexistent", Replace = "new", CaseSensitive = false }
                });

                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");

                var (_, trace) = _sut.ResolveTargetUrlWithTrace("/page", rule, "https://default.com");

                trace.Should().NotContain(step => step.Type == "global-rule");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_ForwardQueryNoQuery_NoTraceStep()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.ForwardQueryParams = true;

                var (_, trace) = _sut.ResolveTargetUrlWithTrace("/page", rule, "https://default.com");

                trace.Should().NotContain(step => step.Type == "query-forward");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_KeptQueryNoMatch_NoTraceStep()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.DiscardQueryParams = true;
                rule.KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^nonexistent$" }
                };

                var (_, trace) = _sut.ResolveTargetUrlWithTrace(
                    "http://old.com/page?other=1", rule, "https://default.com");

                trace.Should().NotContain(step => step.Type == "query-kept");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_StaticQueryNoChange_NoTraceStep()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.StaticQueryParams = new List<StaticQueryParam>
                {
                    new StaticQueryParam { Key = "", Value = "ignored" }
                };

                var (_, trace) = _sut.ResolveTargetUrlWithTrace("/page", rule, "https://default.com");

                trace.Should().NotContain(step => step.Type == "query-static");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_AlwaysHasInputAndResultSteps()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");

                var (_, trace) = _sut.ResolveTargetUrlWithTrace("/page", rule, "https://default.com");

                trace.First().Type.Should().Be("input");
                trace.Last().Type.Should().Be("result");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_NeitherForwardNorDiscard_NoQueryTraceSteps()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");
                rule.ForwardQueryParams = false;
                rule.DiscardQueryParams = false;

                var (_, trace) = _sut.ResolveTargetUrlWithTrace(
                    "http://old.com/page?q=1", rule, "https://default.com");

                trace.Should().NotContain(step => step.Type == "query-forward");
                trace.Should().NotContain(step => step.Type == "query-kept");
                trace.Should().NotContain(step => step.Type == "query-discard");
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_MultipleSearchAndReplace_TracksEachChange()
            {
                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/old-page");
                rule.SearchAndReplace = new List<SearchAndReplaceEntry>
                {
                    new SearchAndReplaceEntry { Search = "old", Replace = "new", CaseSensitive = false },
                    new SearchAndReplaceEntry { Search = "page", Replace = "site", CaseSensitive = false }
                };

                var (_, trace) = _sut.ResolveTargetUrlWithTrace("/page", rule, "https://default.com");

                trace.Count(step => step.Type == "search-replace").Should().Be(2);
            }

            [Fact]
            public void ResolveTargetUrlWithTrace_MultipleGlobalRules_TracksEachChange()
            {
                _globalRuleCache.GetAll().Returns(new List<GlobalRule>
                {
                    new GlobalRule { Search = "com", Replace = "org", CaseSensitive = false },
                    new GlobalRule { Search = "new", Replace = "updated", CaseSensitive = false }
                });

                var rule = CreateRule("/page", RedirectType.Wildcard, "https://new.com/page");

                var (_, trace) = _sut.ResolveTargetUrlWithTrace("/page", rule, "https://default.com");

                trace.Count(step => step.Type == "global-rule").Should().Be(2);
            }
        }
    }
}
