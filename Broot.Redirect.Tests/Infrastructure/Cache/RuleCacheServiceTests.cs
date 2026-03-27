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
                sut.LookupWildcard("/path").Should().Be(wildcardRule);
                sut.GetPartialAndDomainRules().Should().HaveCount(2);
                sut.GetRegexRules().Should().HaveCount(1);
            }
        }

        public class LookupWildcardTests
        {
            [Fact]
            public void LookupWildcard_ValidPath_ReturnsRule()
            {
                var sut = CreateService();
                var rule = CreateRule("/path", RedirectType.Wildcard);

                sut.Initialize(new[] { rule });

                sut.LookupWildcard("/path").Should().Be(rule);
            }

            [Fact]
            public void LookupWildcard_CaseInsensitive_MatchesRegardlessOfCase()
            {
                var sut = CreateService(new CacheOptions { CaseSensitiveMatching = false });
                var rule = CreateRule("/Some/Path", RedirectType.Wildcard);

                sut.Initialize(new[] { rule });

                sut.LookupWildcard("/some/path").Should().Be(rule);
                sut.LookupWildcard("/SOME/PATH").Should().Be(rule);
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
                sut.LookupWildcard("/new-rule").Should().Be(rule);
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
                sut.LookupWildcard("/original").Should().BeNull();
                sut.LookupWildcard("/updated").Should().Be(updatedRule);
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
                sut.LookupWildcard("/path").Should().BeNull();
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
                sut.LookupWildcard("/path3").Should().Be(rule3);
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
                sut.LookupWildcard("/old").Should().BeNull();
                sut.LookupWildcard("/new").Should().Be(newRule);
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
    }
}
