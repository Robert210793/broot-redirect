using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Broot.Redirect.Core.Services;
using FluentAssertions;
using NSubstitute;
using System.Text.RegularExpressions;
using Xunit;

namespace Broot.Redirect.Tests.Core.Services
{
    public class RuleMatchingServiceTests
    {
        private static RuleMatchingConfig DefaultConfig() => new RuleMatchingConfig
        {
            WeightPathSegment = 10,
            WeightQueryPair = 3,
            PenaltyWildcard = -1,
            BonusExactMatch = 5,
            TrailingSlashPolicy = TrailingSlashPolicy.Ignore,
            CaseSensitivePath = false,
            CaseSensitiveQuery = false
        };

        private static RedirectRule CreateRule(
            string matcher,
            RedirectType type,
            string targetUrl = "",
            DateTimeOffset? createdAt = null)
        {
            return new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = matcher,
                RedirectType = type,
                TargetUrl = targetUrl,
                CreatedAt = createdAt ?? DateTimeOffset.UtcNow
            };
        }

        public class ResolveMatchTests
        {
            private readonly IRuleCacheService _cacheService;
            private readonly RuleMatchingService _sut;
            private readonly RuleMatchingConfig _config;

            public ResolveMatchTests()
            {
                _cacheService = Substitute.For<IRuleCacheService>();
                _sut = new RuleMatchingService(_cacheService);
                _config = DefaultConfig();

                _cacheService.LookupWildcard(Arg.Any<string>()).Returns((RedirectRule?)null);
                _cacheService.GetPartialAndDomainRules().Returns(new List<RedirectRule>());
                _cacheService.GetRegexRules().Returns(new List<(Regex, RedirectRule)>());
            }

            [Fact]
            public void ResolveMatch_WildcardExactPath_ReturnsQuality100()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard);

                _cacheService.LookupWildcard("/old-page").Returns(rule);

                var result = _sut.ResolveMatch("/old-page", _config);

                result.Should().NotBeNull();
                result!.Quality.Should().Be(100);
                result.Score.Should().Be(1000 + _config.BonusExactMatch);
                result.Level.Should().Be(MatchQualityLevel.Green);
            }

            [Fact]
            public void ResolveMatch_WildcardWithExtraQueryParams_ReturnsQuality75()
            {
                var rule = CreateRule("/old-page", RedirectType.Wildcard);

                _cacheService.LookupWildcard("/old-page").Returns(rule);

                var result = _sut.ResolveMatch("/old-page?extra=1", _config);

                result.Should().NotBeNull();
                result!.Quality.Should().Be(75);
                result.Level.Should().Be(MatchQualityLevel.Yellow);
            }

            [Fact]
            public void ResolveMatch_WildcardWithMatcherQueryNotInRequest_ReturnsNull()
            {
                var rule = CreateRule("/old-page?required=yes", RedirectType.Wildcard);

                _cacheService.LookupWildcard("/old-page").Returns(rule);

                var result = _sut.ResolveMatch("/old-page", _config);

                result.Should().BeNull();
            }

            [Fact]
            public void ResolveMatch_WildcardTrailingSlashIgnore_MatchesPathWithoutSlash()
            {
                var rule = CreateRule("/path/", RedirectType.Wildcard);

                _cacheService.LookupWildcard("/path").Returns(rule);

                var config = DefaultConfig();
                config.TrailingSlashPolicy = TrailingSlashPolicy.Ignore;

                var result = _sut.ResolveMatch("/path/", config);

                result.Should().NotBeNull();
                result!.Rule.Should().Be(rule);
            }

            [Fact]
            public void ResolveMatch_WildcardTrailingSlashStrict_DoesNotMatchDifferentSlash()
            {
                var config = DefaultConfig();
                config.TrailingSlashPolicy = TrailingSlashPolicy.Strict;

                _cacheService.LookupWildcard("/path").Returns((RedirectRule?)null);

                var result = _sut.ResolveMatch("/path", config);

                result.Should().BeNull();
            }

            [Fact]
            public void ResolveMatch_CaseInsensitivePath_MatchesDifferentCase()
            {
                var rule = CreateRule("/path", RedirectType.Wildcard);

                _cacheService.LookupWildcard("/path").Returns(rule);

                var result = _sut.ResolveMatch("/Path", _config);

                result.Should().NotBeNull();
                result!.Quality.Should().Be(100);
            }

            [Fact]
            public void ResolveMatch_CaseSensitivePath_DoesNotMatchDifferentCase()
            {
                var config = DefaultConfig();
                config.CaseSensitivePath = true;

                _cacheService.LookupWildcard("/Path").Returns((RedirectRule?)null);

                var result = _sut.ResolveMatch("/Path", config);

                result.Should().BeNull();
            }

            [Fact]
            public void ResolveMatch_PartialRuleMatchesPrefix_ReturnsQuality50()
            {
                var rule = CreateRule("/old", RedirectType.Partial, "/new");

                _cacheService.GetPartialAndDomainRules().Returns(new List<RedirectRule> { rule });

                var result = _sut.ResolveMatch("/old/subpage", _config);

                result.Should().NotBeNull();
                result!.Quality.Should().Be(50);
                result.Rule.Should().Be(rule);
            }

            [Fact]
            public void ResolveMatch_PartialRuleMatchesExactly_ReturnsQuality100()
            {
                var rule = CreateRule("/old/exact", RedirectType.Partial, "/new");

                _cacheService.GetPartialAndDomainRules().Returns(new List<RedirectRule> { rule });

                var result = _sut.ResolveMatch("/old/exact", _config);

                result.Should().NotBeNull();
                result!.Quality.Should().Be(100);
            }

            [Fact]
            public void ResolveMatch_PartialHigherScoreThanShorter_WinsOverShorterPartial()
            {
                var specificRule = CreateRule("/old/specific", RedirectType.Partial);
                var generalRule = CreateRule("/old", RedirectType.Partial);

                _cacheService.GetPartialAndDomainRules()
                    .Returns(new List<RedirectRule> { specificRule, generalRule });

                var result = _sut.ResolveMatch("/old/specific", _config);

                result.Should().NotBeNull();
                result!.Rule.Should().Be(specificRule);
                result.Quality.Should().Be(100);
            }

            [Fact]
            public void ResolveMatch_DomainRule_MatchesHostname()
            {
                var rule = CreateRule("example.com", RedirectType.Domain);

                _cacheService.GetPartialAndDomainRules().Returns(new List<RedirectRule> { rule });

                var result = _sut.ResolveMatch("http://example.com/anything", _config);

                result.Should().NotBeNull();
                result!.Rule.Should().Be(rule);
            }

            [Fact]
            public void ResolveMatch_DomainRuleWithQuery_OnlyMatchesIfQueryPresent()
            {
                var rule = CreateRule("example.com?key=val", RedirectType.Domain);

                _cacheService.GetPartialAndDomainRules().Returns(new List<RedirectRule> { rule });

                var noQueryResult = _sut.ResolveMatch("http://example.com/path", _config);

                noQueryResult.Should().BeNull();

                var withQueryResult = _sut.ResolveMatch("http://example.com/path?key=val", _config);

                withQueryResult.Should().NotBeNull();
                withQueryResult!.Rule.Should().Be(rule);
            }

            [Fact]
            public void ResolveMatch_RegexRule_MatchesPattern()
            {
                var regex = new Regex(@"^/archive/\d{4}/", RegexOptions.Compiled);
                var rule = CreateRule(@"^/archive/\d{4}/", RedirectType.Regex);

                _cacheService.GetRegexRules()
                    .Returns(new List<(Regex, RedirectRule)> { (regex, rule) });

                var result = _sut.ResolveMatch("/archive/2024/report", _config);

                result.Should().NotBeNull();
                result!.Rule.Should().Be(rule);
                result.Score.Should().Be(500);
                result.Quality.Should().Be(100);
                result.Level.Should().Be(MatchQualityLevel.Green);
            }

            [Fact]
            public void ResolveMatch_RegexWithTimeout_CatchesAndSkips()
            {
                var evilRegex = new Regex("(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(100));
                var rule = CreateRule("(a+)+$", RedirectType.Regex);

                _cacheService.GetRegexRules()
                    .Returns(new List<(Regex, RedirectRule)> { (evilRegex, rule) });

                var result = _sut.ResolveMatch(new string('a', 30) + "b", _config);

                result.Should().BeNull();
            }

            [Fact]
            public void ResolveMatch_PhasePriority_WildcardWinsOverPartial()
            {
                var wildcardRule = CreateRule("/test-path", RedirectType.Wildcard);
                var partialRule = CreateRule("/test", RedirectType.Partial);

                _cacheService.LookupWildcard("/test-path").Returns(wildcardRule);
                _cacheService.GetPartialAndDomainRules()
                    .Returns(new List<RedirectRule> { partialRule });

                var result = _sut.ResolveMatch("/test-path", _config);

                result.Should().NotBeNull();
                result!.Rule.Should().Be(wildcardRule);
            }

            [Fact]
            public void ResolveMatch_NoMatch_ReturnsNull()
            {
                var result = _sut.ResolveMatch("/nonexistent", _config);

                result.Should().BeNull();
            }
        }

        public class FindMatchingRuleTests
        {
            private readonly IRuleCacheService _cacheService;
            private readonly RuleMatchingService _sut;
            private readonly RuleMatchingConfig _config;

            public FindMatchingRuleTests()
            {
                _cacheService = Substitute.For<IRuleCacheService>();
                _sut = new RuleMatchingService(_cacheService);
                _config = DefaultConfig();
            }

            [Fact]
            public void FindMatchingRule_SlidingWindow_MatchesAtOffset()
            {
                var rule = CreateRule("/b/c", RedirectType.Partial);

                var processed = _sut.PreprocessRule(rule, _config);

                var result = _sut.FindMatchingRule("/a/b/c/d", new[] { processed }, _config);

                result.Should().NotBeNull();
                result!.Quality.Should().Be(50);
                result.Rule.Should().Be(rule);
            }

            [Fact]
            public void FindMatchingRule_WildcardSegments_MatchesAnySegment()
            {
                var rule = CreateRule("/users/*/profile", RedirectType.Partial);

                var processed = _sut.PreprocessRule(rule, _config);

                var result = _sut.FindMatchingRule("/users/123/profile", new[] { processed }, _config);

                result.Should().NotBeNull();
                result!.Rule.Should().Be(rule);
            }

            [Fact]
            public void FindMatchingRule_PartialSegmentPrefix_MatchesPrefixPattern()
            {
                var rule = CreateRule("/doc*", RedirectType.Partial);

                var processed = _sut.PreprocessRule(rule, _config);

                var result = _sut.FindMatchingRule("/documents", new[] { processed }, _config);

                result.Should().NotBeNull();
                result!.Quality.Should().Be(50);
            }

            [Fact]
            public void FindMatchingRule_Tiebreaker_MoreStaticSegmentsWins()
            {
                var tieConfig = new RuleMatchingConfig
                {
                    WeightPathSegment = 1,
                    WeightQueryPair = 1,
                    PenaltyWildcard = 0,
                    BonusExactMatch = 0,
                    TrailingSlashPolicy = TrailingSlashPolicy.Ignore,
                    CaseSensitivePath = false,
                    CaseSensitiveQuery = false
                };

                var ruleMore = CreateRule("/a/b", RedirectType.Partial);
                var ruleLess = CreateRule("/a?x=1", RedirectType.Partial);

                var processed = new[]
                {
                    _sut.PreprocessRule(ruleLess, tieConfig),
                    _sut.PreprocessRule(ruleMore, tieConfig)
                };

                var result = _sut.FindMatchingRule("/a/b?x=1", processed, tieConfig);

                result.Should().NotBeNull();
                result!.Rule.Should().Be(ruleMore);
            }

            [Fact]
            public void FindMatchingRule_Tiebreaker_FewerWildcardsWins()
            {
                var tieConfig = new RuleMatchingConfig
                {
                    WeightPathSegment = 1,
                    WeightQueryPair = 0,
                    PenaltyWildcard = 0,
                    BonusExactMatch = 0,
                    TrailingSlashPolicy = TrailingSlashPolicy.Ignore,
                    CaseSensitivePath = false,
                    CaseSensitiveQuery = false
                };

                var ruleFewerWildcards = CreateRule("/a/*", RedirectType.Partial);
                var ruleMoreWildcards = CreateRule("/a/*/*", RedirectType.Partial);

                var processed = new[]
                {
                    _sut.PreprocessRule(ruleMoreWildcards, tieConfig),
                    _sut.PreprocessRule(ruleFewerWildcards, tieConfig)
                };

                var result = _sut.FindMatchingRule("/a/b/c", processed, tieConfig);

                result.Should().NotBeNull();
                result!.Rule.Should().Be(ruleFewerWildcards);
            }

            [Fact]
            public void FindMatchingRule_Tiebreaker_LongerMatcherWins()
            {
                var tieConfig = new RuleMatchingConfig
                {
                    WeightPathSegment = 1,
                    WeightQueryPair = 1,
                    PenaltyWildcard = 0,
                    BonusExactMatch = 0,
                    TrailingSlashPolicy = TrailingSlashPolicy.Ignore,
                    CaseSensitivePath = false,
                    CaseSensitiveQuery = false
                };

                var ruleLonger = CreateRule("/segment?longer=yes", RedirectType.Partial);
                var ruleShorter = CreateRule("/segment?l=1", RedirectType.Partial);

                var processed = new[]
                {
                    _sut.PreprocessRule(ruleShorter, tieConfig),
                    _sut.PreprocessRule(ruleLonger, tieConfig)
                };

                var result = _sut.FindMatchingRule("/segment?longer=yes&l=1&extra=x", processed, tieConfig);

                result.Should().NotBeNull();
                result!.Rule.Should().Be(ruleLonger);
            }

            [Fact]
            public void FindMatchingRule_Tiebreaker_EarlierCreatedAtWins()
            {
                var olderRule = CreateRule("/path", RedirectType.Partial, createdAt: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
                var newerRule = CreateRule("/path", RedirectType.Partial, createdAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

                var tieConfig = new RuleMatchingConfig
                {
                    WeightPathSegment = 1,
                    WeightQueryPair = 0,
                    PenaltyWildcard = 0,
                    BonusExactMatch = 0,
                    TrailingSlashPolicy = TrailingSlashPolicy.Ignore,
                    CaseSensitivePath = false,
                    CaseSensitiveQuery = false
                };

                var processed = new[]
                {
                    _sut.PreprocessRule(newerRule, tieConfig),
                    _sut.PreprocessRule(olderRule, tieConfig)
                };

                var result = _sut.FindMatchingRule("/path", processed, tieConfig);

                result.Should().NotBeNull();
                result!.Rule.Should().Be(olderRule);
            }
        }

        public class PreprocessRuleTests
        {
            private readonly RuleMatchingService _sut;
            private readonly RuleMatchingConfig _config;

            public PreprocessRuleTests()
            {
                var cacheService = Substitute.For<IRuleCacheService>();
                _sut = new RuleMatchingService(cacheService);
                _config = DefaultConfig();
            }

            [Fact]
            public void PreprocessRule_PartialMatcher_NormalizesPathAndQuery()
            {
                var rule = CreateRule("/old/path?key=value", RedirectType.Partial);

                var processed = _sut.PreprocessRule(rule, _config);

                processed.NormalizedPath.Should().BeEquivalentTo(new[] { "old", "path" });
                processed.NormalizedQuery.Should().ContainKey("key");
                processed.NormalizedQuery["key"].Should().Contain("value");
                processed.IsDomainMatcher.Should().BeFalse();
            }

            [Fact]
            public void PreprocessRule_DomainMatcher_SetsIsDomainMatcher()
            {
                var rule = CreateRule("example.com", RedirectType.Domain);

                var processed = _sut.PreprocessRule(rule, _config);

                processed.IsDomainMatcher.Should().BeTrue();
                processed.NormalizedPath.Should().BeEmpty();
            }

            [Fact]
            public void PreprocessRule_CaseInsensitive_LowercasesSegments()
            {
                var rule = CreateRule("/Path/With/MixedCase", RedirectType.Partial);

                var processed = _sut.PreprocessRule(rule, _config);

                processed.NormalizedPath.Should().BeEquivalentTo(
                    new[] { "path", "with", "mixedcase" },
                    options => options.WithStrictOrdering());
            }
        }

        public class NormalizeQueryTests
        {
            [Fact]
            public void NormalizeQuery_EmptyQuery_ReturnsEmptyDictionary()
            {
                var config = DefaultConfig();

                var result = RuleMatchingService.NormalizeQuery("", config);

                result.Should().BeEmpty();
            }

            [Fact]
            public void NormalizeQuery_TwoParams_ReturnsTwoEntries()
            {
                var config = DefaultConfig();

                var result = RuleMatchingService.NormalizeQuery("?a=1&b=2", config);

                result.Should().HaveCount(2);
                result.Should().ContainKey("a");
                result.Should().ContainKey("b");
            }

            [Fact]
            public void NormalizeQuery_DuplicateKeys_MergesAndSortsValues()
            {
                var config = DefaultConfig();

                var result = RuleMatchingService.NormalizeQuery("?a=2&a=1", config);

                result.Should().ContainKey("a");
                result["a"].Should().BeEquivalentTo(
                    new[] { "1", "2" },
                    options => options.WithStrictOrdering());
            }

            [Fact]
            public void NormalizeQuery_CaseInsensitive_LowercasesKeys()
            {
                var config = DefaultConfig();
                config.CaseSensitiveQuery = false;

                var result = RuleMatchingService.NormalizeQuery("?MyKey=value", config);

                result.Should().ContainKey("mykey");
                result.Should().NotContainKey("MyKey");
            }

            [Fact]
            public void NormalizeQuery_CaseSensitive_PreservesKeyCase()
            {
                var config = DefaultConfig();
                config.CaseSensitiveQuery = true;

                var result = RuleMatchingService.NormalizeQuery("?MyKey=value", config);

                result.Should().ContainKey("MyKey");
                result.Should().NotContainKey("mykey");
            }

            [Fact]
            public void NormalizeQuery_WithoutQuestionMark_ParsesCorrectly()
            {
                var config = DefaultConfig();

                var result = RuleMatchingService.NormalizeQuery("a=1&b=2", config);

                result.Should().HaveCount(2);
            }
        }

        public class NormalizePathTests
        {
            [Fact]
            public void NormalizePath_EmptyPath_ReturnsEmpty()
            {
                var config = DefaultConfig();

                var result = RuleMatchingService.NormalizePath("", config);

                result.Should().BeEmpty();
            }

            [Fact]
            public void NormalizePath_RootSlash_ReturnsEmpty()
            {
                var config = DefaultConfig();

                var result = RuleMatchingService.NormalizePath("/", config);

                result.Should().BeEmpty();
            }

            [Fact]
            public void NormalizePath_TrailingSlashIgnore_TrimsSlash()
            {
                var config = DefaultConfig();
                config.TrailingSlashPolicy = TrailingSlashPolicy.Ignore;

                var result = RuleMatchingService.NormalizePath("/path/", config);

                result.Should().BeEquivalentTo(new[] { "path" });
            }

            [Fact]
            public void NormalizePath_CaseSensitive_PreservesCase()
            {
                var config = DefaultConfig();
                config.CaseSensitivePath = true;

                var result = RuleMatchingService.NormalizePath("/Path/UPPER", config);

                result.Should().BeEquivalentTo(
                    new[] { "Path", "UPPER" },
                    options => options.WithStrictOrdering());
            }

            [Fact]
            public void NormalizePath_MultipleSlashes_NormalizesToSingle()
            {
                var config = DefaultConfig();

                var result = RuleMatchingService.NormalizePath("/a//b", config);

                result.Should().BeEquivalentTo(new[] { "a", "b" });
            }

            [Fact]
            public void NormalizePath_PercentEncoded_DecodesSegments()
            {
                var config = DefaultConfig();

                var result = RuleMatchingService.NormalizePath("/hello%20world", config);

                result.Should().BeEquivalentTo(new[] { "hello world" });
            }
        }

        public class QualityCalculationTests
        {
            private readonly IRuleCacheService _cacheService;
            private readonly RuleMatchingService _sut;

            public QualityCalculationTests()
            {
                _cacheService = Substitute.For<IRuleCacheService>();
                _sut = new RuleMatchingService(_cacheService);

                _cacheService.LookupWildcard(Arg.Any<string>()).Returns((RedirectRule?)null);
                _cacheService.GetPartialAndDomainRules().Returns(new List<RedirectRule>());
                _cacheService.GetRegexRules().Returns(new List<(System.Text.RegularExpressions.Regex, RedirectRule)>());
            }

            [Fact]
            public void ResolveMatch_DomainRuleExactQuery_ReturnsQuality100()
            {
                var config = DefaultConfig();
                var rule = CreateRule("example.com", RedirectType.Domain);

                _cacheService.GetPartialAndDomainRules().Returns(new List<RedirectRule> { rule });

                var result = _sut.ResolveMatch("http://example.com/path", config);

                result.Should().NotBeNull();
                result!.Quality.Should().Be(100);
                result.Level.Should().Be(MatchQualityLevel.Green);
            }

            [Fact]
            public void ResolveMatch_DomainRuleExtraQuery_ReturnsQuality75()
            {
                var config = DefaultConfig();
                var rule = CreateRule("example.com", RedirectType.Domain);

                _cacheService.GetPartialAndDomainRules().Returns(new List<RedirectRule> { rule });

                var result = _sut.ResolveMatch("http://example.com/path?extra=1", config);

                result.Should().NotBeNull();
                result!.Quality.Should().Be(75);
                result.Level.Should().Be(MatchQualityLevel.Yellow);
            }

            [Fact]
            public void ResolveMatch_WildcardQueryMatch_IncludesQueryPairsInScore()
            {
                var config = DefaultConfig();
                var rule = CreateRule("/page?key=val", RedirectType.Wildcard);

                _cacheService.LookupWildcard("/page").Returns(rule);

                var result = _sut.ResolveMatch("/page?key=val", config);

                result.Should().NotBeNull();
                result!.Score.Should().BeGreaterThan(1000);
                result.Quality.Should().Be(100);
            }
        }
    }
}
