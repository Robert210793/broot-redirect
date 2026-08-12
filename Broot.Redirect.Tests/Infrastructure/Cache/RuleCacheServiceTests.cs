using Broot.Redirect.Core.Models;
using Broot.Redirect.Infrastructure.Cache;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.Infrastructure.Cache
{
    public class RuleCacheServiceTests
    {
        private static RedirectRule CreateRule(
            string matcher,
            RedirectType type,
            string targetUrl = "",
            Guid? id = null)
        {
            return new RedirectRule
            {
                Id = id ?? Guid.NewGuid(),
                Matcher = matcher,
                RedirectType = type,
                TargetUrl = targetUrl,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        private static RuleCacheService CreateService(CacheOptions? cacheOptions = null)
        {
            var options = Options.Create(cacheOptions ?? new CacheOptions());
            var logger = Substitute.For<ILogger<RuleCacheService>>();

            return new RuleCacheService(options, logger);
        }

        public class InitializeTests
        {
            [Fact]
            public void Initialize_MixedRules_PopulatesAllIndexesCorrectly()
            {
                var sut = CreateService();

                var wildcardRule = CreateRule("/path", RedirectType.Wildcard);
                var partialRule = CreateRule("/partial", RedirectType.Partial);
                var domainRule = CreateRule("example.com", RedirectType.Domain);
                var regexRule = CreateRule(@"^/test/\d+$", RedirectType.Regex);

                sut.Initialize(new[] { wildcardRule, partialRule, domainRule, regexRule });

                sut.RuleCount.Should().Be(4);
                sut.IsWarmedUp.Should().BeTrue();
                sut.LookupWildcardCandidates("/path").Should().ContainSingle().Which.Should().Be(wildcardRule);
                sut.GetPartialAndDomainRules().Should().HaveCount(2);
                sut.GetRegexRules().Should().HaveCount(1);
            }
        }

        public class LookupWildcardCandidatesTests
        {
            [Fact]
            public void LookupWildcardCandidates_ValidPath_ReturnsRule()
            {
                var sut = CreateService();
                var rule = CreateRule("/path", RedirectType.Wildcard);

                sut.Initialize(new[] { rule });

                sut.LookupWildcardCandidates("/path").Should().ContainSingle().Which.Should().Be(rule);
            }

            [Fact]
            public void LookupWildcardCandidates_CaseInsensitive_MatchesRegardlessOfCase()
            {
                var sut = CreateService(new CacheOptions { CaseSensitiveMatching = false });
                var rule = CreateRule("/Some/Path", RedirectType.Wildcard);

                sut.Initialize(new[] { rule });

                sut.LookupWildcardCandidates("/some/path").Should().ContainSingle().Which.Should().Be(rule);
                sut.LookupWildcardCandidates("/SOME/PATH").Should().ContainSingle().Which.Should().Be(rule);
            }

            [Fact]
            public void LookupWildcardCandidates_QuerySpecificRulesSharePath_ReturnsAllRules()
            {
                var sut = CreateService();
                var firstRule = CreateRule("/path?folder=first", RedirectType.Wildcard);
                var secondRule = CreateRule("/path?folder=second", RedirectType.Wildcard);

                sut.Initialize(new[] { firstRule, secondRule });

                sut.LookupWildcardCandidates("/path")
                    .Should()
                    .BeEquivalentTo(new[] { firstRule, secondRule });
            }
        }

        public class GetPartialAndDomainRulesTests
        {
            [Fact]
            public void GetPartialAndDomainRules_ReturnsSortedByMatcherLengthDescending()
            {
                var sut = CreateService();

                var shortRule = CreateRule("/a", RedirectType.Partial);
                var longRule = CreateRule("/a/b/c", RedirectType.Partial);
                var mediumRule = CreateRule("/a/b", RedirectType.Partial);

                sut.Initialize(new[] { shortRule, longRule, mediumRule });

                var result = sut.GetPartialAndDomainRules();

                result.Should().HaveCount(3);
                result[0].Should().Be(longRule);
                result[1].Should().Be(mediumRule);
                result[2].Should().Be(shortRule);
            }
        }

        public class GetRegexRulesTests
        {
            [Fact]
            public void GetRegexRules_InvalidPattern_SkippedWithWarning()
            {
                var sut = CreateService();

                var invalidRegex = CreateRule("[invalid", RedirectType.Regex);
                var validRegex = CreateRule(@"^/test/\d+$", RedirectType.Regex);

                sut.Initialize(new[] { invalidRegex, validRegex });

                var result = sut.GetRegexRules();

                result.Should().HaveCount(1);
                result[0].Rule.Should().Be(validRegex);
            }
        }

        public class MatcherExistsTests
        {
            [Fact]
            public void MatcherExists_ExistingMatcher_ReturnsTrue()
            {
                var sut = CreateService();
                var rule = CreateRule("/existing", RedirectType.Partial);

                sut.Initialize(new[] { rule });

                sut.MatcherExists("/existing").Should().BeTrue();
            }

            [Fact]
            public void MatcherExists_NonExistent_ReturnsFalse()
            {
                var sut = CreateService();
                var rule = CreateRule("/existing", RedirectType.Partial);

                sut.Initialize(new[] { rule });

                sut.MatcherExists("/nonexistent").Should().BeFalse();
            }

            [Fact]
            public void MatcherExists_ExcludedRuleId_ReturnsFalse()
            {
                var sut = CreateService();
                var rule = CreateRule("/existing", RedirectType.Partial);

                sut.Initialize(new[] { rule });

                sut.MatcherExists("/existing", rule.Id).Should().BeFalse();
            }
        }

        public class FindOverlappingMatcherTests
        {
            [Fact]
            public void FindOverlappingMatcher_HierarchicalOverlap_DetectsOverlap()
            {
                var sut = CreateService();
                var existingRule = CreateRule("/a/b", RedirectType.Partial);

                sut.Initialize(new[] { existingRule });

                var result = sut.FindOverlappingMatcher("/a/b/c");

                result.Should().Be(existingRule);
            }

            [Fact]
            public void FindOverlappingMatcher_SameLength_DoesNotFlag()
            {
                var sut = CreateService();
                var existingRule = CreateRule("/a/b", RedirectType.Partial);

                sut.Initialize(new[] { existingRule });

                var result = sut.FindOverlappingMatcher("/a/b");

                result.Should().BeNull();
            }

            [Fact]
            public void FindOverlappingMatcher_RegexRule_Skipped()
            {
                var sut = CreateService();
                var regexRule = CreateRule(@"^/archive/\d+$", RedirectType.Regex);

                sut.Initialize(new[] { regexRule });

                var result = sut.FindOverlappingMatcher("/archive/123");

                result.Should().BeNull();
            }
        }

        public class MutationTests
        {
            [Fact]
            public void AddRule_AddsAndRebuildsIndexes()
            {
                var sut = CreateService();

                sut.Initialize(Array.Empty<RedirectRule>());
                sut.RuleCount.Should().Be(0);

                var rule = CreateRule("/new-rule", RedirectType.Wildcard);

                sut.AddRule(rule);

                sut.RuleCount.Should().Be(1);
                sut.LookupWildcardCandidates("/new-rule").Should().ContainSingle().Which.Should().Be(rule);
            }

            [Fact]
            public void UpdateRule_UpdatesAndRebuildsIndexes()
            {
                var sut = CreateService();
                var ruleId = Guid.NewGuid();
                var originalRule = CreateRule("/original", RedirectType.Wildcard, id: ruleId);

                sut.Initialize(new[] { originalRule });

                var updatedRule = CreateRule("/updated", RedirectType.Wildcard, id: ruleId);

                sut.UpdateRule(updatedRule);

                sut.RuleCount.Should().Be(1);
                sut.LookupWildcardCandidates("/original").Should().BeEmpty();
                sut.LookupWildcardCandidates("/updated").Should().ContainSingle().Which.Should().Be(updatedRule);
            }

            [Fact]
            public void RemoveRule_RemovesAndRebuildsIndexes()
            {
                var sut = CreateService();
                var rule = CreateRule("/path", RedirectType.Wildcard);

                sut.Initialize(new[] { rule });
                sut.RuleCount.Should().Be(1);

                sut.RemoveRule(rule.Id);

                sut.RuleCount.Should().Be(0);
                sut.LookupWildcardCandidates("/path").Should().BeEmpty();
            }

            [Fact]
            public void RemoveRules_BulkRemovesAndRebuildsOnce()
            {
                var sut = CreateService();

                var rule1 = CreateRule("/path1", RedirectType.Wildcard);
                var rule2 = CreateRule("/path2", RedirectType.Wildcard);
                var rule3 = CreateRule("/path3", RedirectType.Wildcard);

                sut.Initialize(new[] { rule1, rule2, rule3 });
                sut.RuleCount.Should().Be(3);

                sut.RemoveRules(new HashSet<Guid> { rule1.Id, rule2.Id });

                sut.RuleCount.Should().Be(1);
                sut.LookupWildcardCandidates("/path3").Should().ContainSingle().Which.Should().Be(rule3);
            }

            [Fact]
            public void ReplaceAll_ClearsAndRepopulates()
            {
                var sut = CreateService();
                var oldRule = CreateRule("/old", RedirectType.Wildcard);

                sut.Initialize(new[] { oldRule });

                var newRule = CreateRule("/new", RedirectType.Wildcard);

                sut.ReplaceAll(new[] { newRule });

                sut.RuleCount.Should().Be(1);
                sut.LookupWildcardCandidates("/old").Should().BeEmpty();
                sut.LookupWildcardCandidates("/new").Should().ContainSingle().Which.Should().Be(newRule);
            }
        }

        public class StateTests
        {
            [Fact]
            public void IsWarmedUp_FalseBeforeInit_TrueAfter()
            {
                var sut = CreateService();

                sut.IsWarmedUp.Should().BeFalse();

                sut.Initialize(Array.Empty<RedirectRule>());

                sut.IsWarmedUp.Should().BeTrue();
            }

            [Fact]
            public void RuleCount_ReflectsCurrentState()
            {
                var sut = CreateService();

                sut.Initialize(Array.Empty<RedirectRule>());
                sut.RuleCount.Should().Be(0);

                sut.AddRule(CreateRule("/a", RedirectType.Wildcard));

                sut.RuleCount.Should().Be(1);

                sut.AddRule(CreateRule("/b", RedirectType.Partial));

                sut.RuleCount.Should().Be(2);
            }
        }

        public class TrailingSlashTests
        {
            [Fact]
            public void LookupWildcardCandidates_TrailingSlashIgnore_TrimsTrailingSlash()
            {
                var sut = CreateService(new CacheOptions { TrailingSlashPolicy = "ignore" });
                var rule = CreateRule("/path/", RedirectType.Wildcard);

                sut.Initialize(new[] { rule });

                // The normalizer should trim the trailing slash, so lookup without slash should work
                sut.LookupWildcardCandidates("/path").Should().ContainSingle().Which.Should().Be(rule);
            }

            [Fact]
            public void LookupWildcardCandidates_TrailingSlashStrict_PreservesTrailingSlash()
            {
                var sut = CreateService(new CacheOptions { TrailingSlashPolicy = "strict" });
                var rule = CreateRule("/path/", RedirectType.Wildcard);

                sut.Initialize(new[] { rule });

                sut.LookupWildcardCandidates("/path/").Should().ContainSingle().Which.Should().Be(rule);
                sut.LookupWildcardCandidates("/path").Should().BeEmpty();
            }

            [Fact]
            public void LookupWildcardCandidates_RootSlash_NotTrimmed()
            {
                var sut = CreateService(new CacheOptions { TrailingSlashPolicy = "ignore" });
                var rule = CreateRule("/", RedirectType.Wildcard);

                sut.Initialize(new[] { rule });

                sut.LookupWildcardCandidates("/").Should().ContainSingle().Which.Should().Be(rule);
            }
        }

        public class CaseSensitivityTests
        {
            [Fact]
            public void LookupWildcardCandidates_CaseSensitive_DoesNotMatchDifferentCase()
            {
                var sut = CreateService(new CacheOptions { CaseSensitiveMatching = true });
                var rule = CreateRule("/Path", RedirectType.Wildcard);

                sut.Initialize(new[] { rule });

                sut.LookupWildcardCandidates("/Path").Should().ContainSingle().Which.Should().Be(rule);
                sut.LookupWildcardCandidates("/path").Should().BeEmpty();
            }
        }

        public class RegexEdgeCaseTests
        {
            [Fact]
            public void Initialize_AllInvalidRegex_ResultsInEmptyRegexList()
            {
                var sut = CreateService();

                var invalid1 = CreateRule("[bad", RedirectType.Regex);
                var invalid2 = CreateRule("(unclosed", RedirectType.Regex);

                sut.Initialize(new[] { invalid1, invalid2 });

                sut.GetRegexRules().Should().BeEmpty();
                sut.RuleCount.Should().Be(2);
            }

            [Fact]
            public void Initialize_RegexTimeout_UsesConfiguredValue()
            {
                var sut = CreateService(new CacheOptions { RegexMatchTimeout = TimeSpan.FromSeconds(5) });

                var rule = CreateRule(@"^\d+$", RedirectType.Regex);

                sut.Initialize(new[] { rule });

                var regexRules = sut.GetRegexRules();

                regexRules.Should().HaveCount(1);
                regexRules[0].CompiledRegex.MatchTimeout.Should().Be(TimeSpan.FromSeconds(5));
            }
        }

        public class FindOverlappingMatcherEdgeCaseTests
        {
            [Fact]
            public void FindOverlappingMatcher_EmptyMatcher_ReturnsNull()
            {
                var sut = CreateService();
                var rule = CreateRule("/a/b", RedirectType.Partial);

                sut.Initialize(new[] { rule });

                var result = sut.FindOverlappingMatcher("");

                result.Should().BeNull();
            }

            [Fact]
            public void FindOverlappingMatcher_ExcludedRuleId_SkipsRule()
            {
                var sut = CreateService();
                var rule = CreateRule("/a/b", RedirectType.Partial);

                sut.Initialize(new[] { rule });

                var result = sut.FindOverlappingMatcher("/a/b/c", excludeRuleId: rule.Id);

                result.Should().BeNull();
            }

            [Fact]
            public void FindOverlappingMatcher_DifferentPrefixes_NoOverlap()
            {
                var sut = CreateService();
                var rule = CreateRule("/x/y", RedirectType.Partial);

                sut.Initialize(new[] { rule });

                var result = sut.FindOverlappingMatcher("/a/b/c");

                result.Should().BeNull();
            }
        }

        public class GetByIdTests
        {
            [Fact]
            public void GetById_Exists_ReturnsRule()
            {
                var sut = CreateService();
                var rule = CreateRule("/path", RedirectType.Wildcard);

                sut.Initialize(new[] { rule });

                sut.GetById(rule.Id).Should().Be(rule);
            }

            [Fact]
            public void GetById_NotExists_ReturnsNull()
            {
                var sut = CreateService();

                sut.Initialize(Array.Empty<RedirectRule>());

                sut.GetById(Guid.NewGuid()).Should().BeNull();
            }
        }

        public class GetAllTests
        {
            [Fact]
            public void GetAll_ReturnsAllRules()
            {
                var sut = CreateService();

                var rule1 = CreateRule("/a", RedirectType.Wildcard);
                var rule2 = CreateRule("/b", RedirectType.Partial);

                sut.Initialize(new[] { rule1, rule2 });

                var all = sut.GetAll();

                all.Should().HaveCount(2);
                all.Should().Contain(rule1);
                all.Should().Contain(rule2);
            }
        }
    }
}
