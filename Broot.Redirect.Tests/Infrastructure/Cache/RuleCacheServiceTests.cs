using Broot.Redirect.Core.Models;
using Broot.Redirect.Core.Services;
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
            public void FindOverlappingMatcher_PartialBelowPartial_DetectsOverlap()
            {
                var sut = CreateService();
                var existingRule = CreateRule("/a/b", RedirectType.Partial);

                sut.Initialize(new[] { existingRule });

                var result = sut.FindOverlappingMatcher("/a/b/c", RedirectType.Partial);

                result.Should().Be(existingRule);
            }

            [Fact]
            public void FindOverlappingMatcher_PartialAbovePartial_DetectsOverlap()
            {
                var sut = CreateService();
                var existingRule = CreateRule("/a/b/c", RedirectType.Partial);

                sut.Initialize(new[] { existingRule });

                var result = sut.FindOverlappingMatcher("/a/b", RedirectType.Partial);

                result.Should().Be(existingRule);
            }

            [Fact]
            public void FindOverlappingMatcher_SameLength_DoesNotFlag()
            {
                var sut = CreateService();
                var existingRule = CreateRule("/a/b", RedirectType.Partial);

                sut.Initialize(new[] { existingRule });

                var result = sut.FindOverlappingMatcher("/a/b", RedirectType.Partial);

                result.Should().BeNull();
            }

            [Fact]
            public void FindOverlappingMatcher_RegexRule_Skipped()
            {
                var sut = CreateService();
                var regexRule = CreateRule(@"^/archive/\d+$", RedirectType.Regex);

                sut.Initialize(new[] { regexRule });

                var result = sut.FindOverlappingMatcher("/archive/123", RedirectType.Partial);

                result.Should().BeNull();
            }

            [Fact]
            public void FindOverlappingMatcher_WildcardFolderAboveWildcardFile_DoesNotFlag()
            {
                var sut = CreateService();
                var existingFile = CreateRule(
                    "/ims/KVP_Innovation/11%20Audit%20intern%20u.%20extern/Richtlinie%20Auditmanagement.docx",
                    RedirectType.Wildcard);

                sut.Initialize(new[] { existingFile });

                var result = sut.FindOverlappingMatcher(
                    "/ims/KVP_Innovation/11%20Audit%20intern%20u.%20extern",
                    RedirectType.Wildcard);

                result.Should().BeNull();
            }

            [Fact]
            public void FindOverlappingMatcher_WildcardFileBelowWildcardFolder_DoesNotFlag()
            {
                var sut = CreateService();
                var existingFolder = CreateRule("/a/b", RedirectType.Wildcard);

                sut.Initialize(new[] { existingFolder });

                var result = sut.FindOverlappingMatcher("/a/b/c.docx", RedirectType.Wildcard);

                result.Should().BeNull();
            }

            [Theory]
            [InlineData(RuleSource.Manual)]
            [InlineData(RuleSource.Import)]
            [InlineData(RuleSource.QmPro)]
            [InlineData(RuleSource.AreaList)]
            public void FindOverlappingMatcher_NewPartialAboveExistingWildcard_DoesNotFlag(RuleSource source)
            {
                var sut = CreateService();
                var existingWildcard = CreateRule("/a/b/c.docx", RedirectType.Wildcard);
                existingWildcard.Source = source;

                sut.Initialize(new[] { existingWildcard });

                var result = sut.FindOverlappingMatcher("/a/b", RedirectType.Partial);

                result.Should().BeNull();
            }

            [Theory]
            [InlineData(RuleSource.Manual)]
            [InlineData(RuleSource.Import)]
            [InlineData(RuleSource.QmPro)]
            [InlineData(RuleSource.AreaList)]
            public void FindOverlappingMatcher_NewWildcardBelowExistingPartial_DoesNotFlag(RuleSource source)
            {
                var sut = CreateService();
                var existingPartial = CreateRule("/a/b", RedirectType.Partial);
                existingPartial.Source = source;

                sut.Initialize(new[] { existingPartial });

                var result = sut.FindOverlappingMatcher("/a/b/c.docx", RedirectType.Wildcard);

                result.Should().BeNull();
            }

            [Fact]
            public void FindOverlappingMatcher_NewDomainOrRegexRule_DoesNotFlag()
            {
                var sut = CreateService();
                var existingPartial = CreateRule("/a", RedirectType.Partial);

                sut.Initialize(new[] { existingPartial });

                sut.FindOverlappingMatcher("/a/b", RedirectType.Domain).Should().BeNull();
                sut.FindOverlappingMatcher("/a/b", RedirectType.Regex).Should().BeNull();
            }

            [Fact]
            public void FindOverlappingMatcher_LongerPartialBelowShorterWildcard_DoesNotFlag()
            {
                var sut = CreateService();
                var existingWildcard = CreateRule("/a/b", RedirectType.Wildcard);

                sut.Initialize(new[] { existingWildcard });

                var result = sut.FindOverlappingMatcher("/a/b/c", RedirectType.Partial);

                result.Should().BeNull();
            }

            [Fact]
            public void FindOverlappingMatcher_DomainRuleAbovePath_DoesNotFlag()
            {
                var sut = CreateService();
                var domainRule = CreateRule("example.com", RedirectType.Domain);

                sut.Initialize(new[] { domainRule });

                var result = sut.FindOverlappingMatcher("/example.com/foo", RedirectType.Partial);

                result.Should().BeNull();
            }

            [Fact]
            public void FindOverlappingMatcher_EncodedAndDecodedSpelling_AreComparable()
            {
                var sut = CreateService();
                var existingPartial = CreateRule("/ims/11%20Audit%20intern", RedirectType.Partial);

                sut.Initialize(new[] { existingPartial });

                var result = sut.FindOverlappingMatcher(
                    "/ims/11 Audit intern/Unterordner",
                    RedirectType.Partial);

                result.Should().Be(existingPartial);
            }

            [Fact]
            public void FindOverlappingMatcher_QueryStringIsIgnored_ForSegmentComparison()
            {
                var sut = CreateService();
                var existingPartial = CreateRule("/ims/KVP_Innovation", RedirectType.Partial);

                sut.Initialize(new[] { existingPartial });

                var result = sut.FindOverlappingMatcher(
                    "/ims/KVP_Innovation/Forms?View=%7B1%7D",
                    RedirectType.Partial);

                result.Should().Be(existingPartial);
            }

            [Fact]
            public void FindOverlappingMatcher_EncodedSlashInSegment_DoesNotCreateExtraSegments()
            {
                var sut = CreateService();
                var existingPartial = CreateRule("/a/b", RedirectType.Partial);

                sut.Initialize(new[] { existingPartial });

                // '/a/b%2Fc' is a two-segment path whose second segment happens to contain a
                // slash. Decoding before splitting would make it a three-segment path and
                // wrongly report an overlap with the two-segment '/a/b'.
                var result = sut.FindOverlappingMatcher("/a/b%2Fc", RedirectType.Partial);

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

                var result = sut.FindOverlappingMatcher("", RedirectType.Partial);

                result.Should().BeNull();
            }

            [Fact]
            public void FindOverlappingMatcher_ExcludedRuleId_SkipsRule()
            {
                var sut = CreateService();
                var rule = CreateRule("/a/b", RedirectType.Partial);

                sut.Initialize(new[] { rule });

                var result = sut.FindOverlappingMatcher("/a/b/c", RedirectType.Partial, excludeRuleId: rule.Id);

                result.Should().BeNull();
            }

            [Fact]
            public void FindOverlappingMatcher_DifferentPrefixes_NoOverlap()
            {
                var sut = CreateService();
                var rule = CreateRule("/x/y", RedirectType.Partial);

                sut.Initialize(new[] { rule });

                var result = sut.FindOverlappingMatcher("/a/b/c", RedirectType.Partial);

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

        public class InactiveRuleTests
        {
            [Fact]
            public void Initialize_InactiveRules_AreExcludedFromMatchingIndexes()
            {
                var sut = CreateService();
                var wildcard = CreateRule("/inactive/file.docx", RedirectType.Wildcard);
                var partial = CreateRule("/inactive-folder", RedirectType.Partial);
                var domain = CreateRule("inactive.example.com", RedirectType.Domain);
                var regex = CreateRule(@"^/inactive/\d+$", RedirectType.Regex);

                foreach (var rule in new[] { wildcard, partial, domain, regex })
                {
                    rule.IsActive = false;
                }

                sut.Initialize(new[] { wildcard, partial, domain, regex });

                sut.LookupWildcardCandidates("/inactive/file.docx").Should().BeEmpty();
                sut.GetPartialAndDomainRules().Should().BeEmpty();
                sut.GetRegexRules().Should().BeEmpty();
            }

            [Fact]
            public void Initialize_InactiveRules_RemainInRuleListAndMatcherIndex()
            {
                var sut = CreateService();
                var inactive = CreateRule("/inactive/file.docx", RedirectType.Wildcard);
                inactive.IsActive = false;

                sut.Initialize(new[] { inactive });

                sut.RuleCount.Should().Be(1);
                sut.GetAll().Should().ContainSingle().Which.Should().BeSameAs(inactive);
                sut.GetById(inactive.Id).Should().BeSameAs(inactive);
                sut.MatcherExists("/inactive/file.docx").Should().BeTrue();
            }

            [Fact]
            public void FindOverlappingMatcher_ConsidersInactivePartialRule()
            {
                var sut = CreateService();
                var inactivePartial = CreateRule("/folder", RedirectType.Partial);
                inactivePartial.IsActive = false;

                sut.Initialize(new[] { inactivePartial });

                sut.FindOverlappingMatcher("/folder/sub", RedirectType.Partial)
                    .Should().BeSameAs(inactivePartial);
            }

            [Fact]
            public void UpdateRule_Deactivation_RemovesRuleFromMatchingIndex_AndReactivationRestoresIt()
            {
                var sut = CreateService();
                var rule = CreateRule("/doc.docx", RedirectType.Wildcard);
                sut.Initialize(new[] { rule });

                var deactivated = CreateRule("/doc.docx", RedirectType.Wildcard, id: rule.Id);
                deactivated.IsActive = false;
                sut.UpdateRule(deactivated);

                sut.LookupWildcardCandidates("/doc.docx").Should().BeEmpty();

                var reactivated = CreateRule("/doc.docx", RedirectType.Wildcard, id: rule.Id);
                sut.UpdateRule(reactivated);

                sut.LookupWildcardCandidates("/doc.docx").Should().ContainSingle().Which.Should().BeSameAs(reactivated);
            }
        }

        public class InactiveRuleMatchingTests
        {
            private static readonly RuleMatchingConfig Config = new()
            {
                WeightPathSegment = 10,
                WeightQueryPair = 5,
                PenaltyWildcard = 1,
                BonusExactMatch = 50,
                TrailingSlashPolicy = TrailingSlashPolicy.Ignore,
                CaseSensitivePath = false,
                CaseSensitiveQuery = false
            };

            [Fact]
            public void ResolveMatch_InactiveWildcardRule_ReturnsNoMatch()
            {
                var cache = CreateService();
                var rule = CreateRule("/sites/qm/doc.docx", RedirectType.Wildcard, "https://new.example.com/doc.docx");
                rule.IsActive = false;
                cache.Initialize(new[] { rule });

                var result = new RuleMatchingService(cache).ResolveMatch("/sites/qm/doc.docx", Config);

                result.Should().BeNull();
            }

            [Fact]
            public void ResolveMatch_InactivePartialRule_ReturnsNoMatch()
            {
                var cache = CreateService();
                var rule = CreateRule("/sites/qm", RedirectType.Partial, "/new");
                rule.IsActive = false;
                cache.Initialize(new[] { rule });

                var result = new RuleMatchingService(cache).ResolveMatch("/sites/qm/doc.docx", Config);

                result.Should().BeNull();
            }

            [Fact]
            public void ResolveMatch_InactiveWildcardBesideActivePartial_FallsBackToPartial()
            {
                var cache = CreateService();
                var inactive = CreateRule("/sites/qm/doc.docx", RedirectType.Wildcard, "https://new.example.com/doc.docx");
                inactive.IsActive = false;
                var partial = CreateRule("/sites/qm", RedirectType.Partial, "/new");
                cache.Initialize(new[] { inactive, partial });

                var result = new RuleMatchingService(cache).ResolveMatch("/sites/qm/doc.docx", Config);

                result.Should().NotBeNull();
                result!.Rule.Id.Should().Be(partial.Id);
            }
        }

        public class ExternalDocumentIdTests
        {
            private static RedirectRule CreateDocumentRule(
                string matcher,
                string? documentId,
                bool isActive = true,
                DateTimeOffset? createdAt = null)
            {
                var rule = CreateRule(matcher, RedirectType.Wildcard);
                rule.ExternalDocumentId = documentId;
                rule.Source = RuleSource.QmPro;
                rule.IsActive = isActive;
                rule.CreatedAt = createdAt ?? DateTimeOffset.UtcNow;

                return rule;
            }

            [Fact]
            public void GetByExternalDocumentId_IsCaseInsensitive()
            {
                var sut = CreateService();
                var rule = CreateDocumentRule("/a.docx", "https://Tenant/sites/QM|ABC|Def");

                sut.Initialize(new[] { rule });

                sut.GetByExternalDocumentId("https://tenant/sites/qm|abc|DEF").Should().BeSameAs(rule);
            }

            [Fact]
            public void GetByExternalDocumentId_FindsInactiveRules()
            {
                var sut = CreateService();
                var rule = CreateDocumentRule("/a.docx", "doc-1", isActive: false);

                sut.Initialize(new[] { rule });

                sut.GetByExternalDocumentId("doc-1").Should().BeSameAs(rule);
            }

            [Theory]
            [InlineData("unknown")]
            [InlineData("")]
            [InlineData("  ")]
            public void GetByExternalDocumentId_UnknownOrEmpty_ReturnsNull(string documentId)
            {
                var sut = CreateService();
                sut.Initialize(new[] { CreateDocumentRule("/a.docx", "doc-1"), CreateRule("/b", RedirectType.Partial) });

                sut.GetByExternalDocumentId(documentId).Should().BeNull();
            }

            [Fact]
            public void GetByExternalDocumentId_FollowsMutations()
            {
                var sut = CreateService();
                sut.Initialize(new List<RedirectRule>());

                var rule = CreateDocumentRule("/a.docx", "doc-1");
                sut.AddRule(rule);

                sut.GetByExternalDocumentId("doc-1").Should().BeSameAs(rule);

                sut.RemoveRule(rule.Id);

                sut.GetByExternalDocumentId("doc-1").Should().BeNull();
            }

            [Fact]
            public void Initialize_DuplicateDocumentIds_LogsWarningAndPrefersActiveRule()
            {
                var logger = Substitute.For<ILogger<RuleCacheService>>();
                var sut = new RuleCacheService(Options.Create(new CacheOptions()), logger);
                var olderInactive = CreateDocumentRule("/a.docx", "doc-1", isActive: false, createdAt: DateTimeOffset.UtcNow.AddDays(-2));
                var newerActive = CreateDocumentRule("/b.docx", "DOC-1", createdAt: DateTimeOffset.UtcNow);

                sut.Initialize(new[] { olderInactive, newerActive });

                sut.GetByExternalDocumentId("doc-1").Should().BeSameAs(newerActive);
                logger.ReceivedCalls()
                    .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
                    .Select(call => call.GetArguments())
                    .Should().Contain(arguments =>
                        (LogLevel)arguments[0]! == LogLevel.Warning
                        && arguments[2]!.ToString()!.Contains("doc-1", StringComparison.OrdinalIgnoreCase)
                        && arguments[2]!.ToString()!.Contains(olderInactive.Id.ToString()));
            }

            [Fact]
            public void Initialize_DuplicateDocumentIds_BothActive_PrefersOldestRule()
            {
                var sut = CreateService();
                var older = CreateDocumentRule("/a.docx", "doc-1", createdAt: DateTimeOffset.UtcNow.AddDays(-2));
                var newer = CreateDocumentRule("/b.docx", "doc-1", createdAt: DateTimeOffset.UtcNow);

                sut.Initialize(new[] { newer, older });

                sut.GetByExternalDocumentId("doc-1").Should().BeSameAs(older);
            }
        }

        public class FindEquivalentMatcherTests
        {
            [Theory]
            [InlineData("/docs/a.docx", "/docs/a.docx")]
            [InlineData("/Docs/A.docx", "/docs/a.docx")]
            [InlineData("/docs/a b.docx", "/docs/a%20b.docx")]
            [InlineData("/docs/a%20b.docx", "/DOCS/A B.docx")]
            [InlineData("/docs/a.docx/", "/docs/a.docx")]
            [InlineData("/docs/a.docx?x=1&y=2", "/docs/a.docx?y=2&x=1")]
            [InlineData("/docs/a.docx?RootFolder=%2Fims%2FA", "/docs/a.docx?rootfolder=/ims/a")]
            [InlineData("/docs/a.docx?", "/docs/a.docx")]
            public void FindEquivalentMatcher_EquivalentSpellings_ReturnsRule(string existingMatcher, string inputMatcher)
            {
                var sut = CreateService();
                var rule = CreateRule(existingMatcher, RedirectType.Wildcard);
                sut.Initialize(new[] { rule });

                sut.FindEquivalentMatcher(inputMatcher).Should().BeSameAs(rule);
            }

            [Theory]
            [InlineData("/docs/a.docx?x=1", "/docs/a.docx?x=2")]
            [InlineData("/docs/a.docx?x=1", "/docs/a.docx")]
            [InlineData("/docs/a.docx", "/docs/b.docx")]
            [InlineData("/docs", "/docs/a.docx")]
            public void FindEquivalentMatcher_DifferentMatchers_ReturnsNull(string existingMatcher, string inputMatcher)
            {
                var sut = CreateService();
                sut.Initialize(new[] { CreateRule(existingMatcher, RedirectType.Wildcard) });

                sut.FindEquivalentMatcher(inputMatcher).Should().BeNull();
            }

            [Fact]
            public void FindEquivalentMatcher_ConsidersInactiveAndPartialRules()
            {
                var sut = CreateService();
                var inactive = CreateRule("/inactive.docx", RedirectType.Wildcard);
                inactive.IsActive = false;
                var partial = CreateRule("/folder", RedirectType.Partial);

                sut.Initialize(new[] { inactive, partial });

                sut.FindEquivalentMatcher("/inactive.docx").Should().BeSameAs(inactive);
                sut.FindEquivalentMatcher("/folder").Should().BeSameAs(partial);
            }

            [Fact]
            public void FindEquivalentMatcher_ExcludedRule_IsIgnored()
            {
                var sut = CreateService();
                var rule = CreateRule("/docs/a.docx", RedirectType.Wildcard);
                sut.Initialize(new[] { rule });

                sut.FindEquivalentMatcher("/docs/a.docx", rule.Id).Should().BeNull();
            }

            [Fact]
            public void FindEquivalentMatcher_RegexRules_AreSkipped()
            {
                var sut = CreateService();
                sut.Initialize(new[] { CreateRule("/docs/a.docx", RedirectType.Regex) });

                sut.FindEquivalentMatcher("/docs/a.docx").Should().BeNull();
            }

            [Fact]
            public void FindEquivalentMatcher_ExcludedSource_IsIgnoredButOtherSourcesAreFound()
            {
                var sut = CreateService();
                var areaRule = CreateRule("/docs/a.docx", RedirectType.Wildcard);
                areaRule.Source = RuleSource.AreaList;
                var manualRule = CreateRule("/docs/b.docx", RedirectType.Wildcard);
                manualRule.Source = RuleSource.Manual;
                sut.Initialize(new[] { areaRule, manualRule });

                sut.FindEquivalentMatcher("/docs/a.docx", excludedSource: RuleSource.AreaList).Should().BeNull();
                sut.FindEquivalentMatcher("/docs/a.docx").Should().BeSameAs(areaRule);
                sut.FindEquivalentMatcher("/docs/b.docx", excludedSource: RuleSource.AreaList).Should().BeSameAs(manualRule);
            }
        }

        public class FindOverlappingMatcherExcludedSourceTests
        {
            [Fact]
            public void FindOverlappingMatcher_ExcludedSource_IsIgnoredButOtherSourcesAreFound()
            {
                var sut = CreateService();
                var areaFolder = CreateRule("/ims/a", RedirectType.Partial);
                areaFolder.Source = RuleSource.AreaList;
                var manualFolder = CreateRule("/docs", RedirectType.Partial);
                manualFolder.Source = RuleSource.Manual;
                sut.Initialize(new[] { areaFolder, manualFolder });

                sut.FindOverlappingMatcher("/ims/a/b", RedirectType.Partial, excludedSource: RuleSource.AreaList).Should().BeNull();
                sut.FindOverlappingMatcher("/ims/a/b", RedirectType.Partial).Should().BeSameAs(areaFolder);
                sut.FindOverlappingMatcher("/docs/sub", RedirectType.Partial, excludedSource: RuleSource.AreaList).Should().BeSameAs(manualFolder);
            }

            [Fact]
            public void FindOverlappingMatcher_InactivePartialRuleOfOtherSource_IsFound()
            {
                var sut = CreateService();
                var inactive = CreateRule("/docs/a", RedirectType.Partial);
                inactive.Source = RuleSource.Manual;
                inactive.IsActive = false;
                sut.Initialize(new[] { inactive });

                sut.FindOverlappingMatcher("/docs", RedirectType.Partial, excludedSource: RuleSource.AreaList).Should().BeSameAs(inactive);
            }
        }

        public class ApplyChangesTests
        {
            [Fact]
            public void ApplyChanges_AddsUpdatesAndRemovesRulesAndRebuildsIndexes()
            {
                var sut = CreateService();
                var kept = CreateRule("/kept", RedirectType.Wildcard);
                var updated = CreateRule("/old", RedirectType.Wildcard);
                var removed = CreateRule("/removed", RedirectType.Wildcard);
                sut.Initialize(new[] { kept, updated, removed });

                var replacement = CreateRule("/new-folder", RedirectType.Partial, id: updated.Id);
                var added = CreateRule("/added", RedirectType.Wildcard);

                sut.ApplyChanges(new[] { replacement, added }, new HashSet<Guid> { removed.Id });

                sut.RuleCount.Should().Be(3);
                sut.GetById(updated.Id).Should().BeSameAs(replacement);
                sut.GetById(removed.Id).Should().BeNull();
                sut.LookupWildcardCandidates("/old").Should().BeEmpty();
                sut.LookupWildcardCandidates("/removed").Should().BeEmpty();
                sut.LookupWildcardCandidates("/added").Should().ContainSingle().Which.Should().BeSameAs(added);
                sut.GetPartialAndDomainRules().Should().ContainSingle().Which.Should().BeSameAs(replacement);
                sut.MatcherExists("/kept").Should().BeTrue();
            }

            [Fact]
            public void ApplyChanges_NoChanges_KeepsCache()
            {
                var sut = CreateService();
                var rule = CreateRule("/kept", RedirectType.Wildcard);
                sut.Initialize(new[] { rule });

                sut.ApplyChanges(Array.Empty<RedirectRule>(), new HashSet<Guid>());

                sut.GetById(rule.Id).Should().BeSameAs(rule);
            }
        }

        public class ExternalDocumentIndexTests
        {
            [Fact]
            public void GetByExternalDocumentId_IgnoresAreaListRules()
            {
                var sut = CreateService();
                var areaRule = CreateRule("/ims/a", RedirectType.Partial);
                areaRule.Source = RuleSource.AreaList;
                areaRule.ExternalDocumentId = "5b0c2a4e-0000-4000-8000-000000000001";
                var qmProRule = CreateRule("/docs/a.docx", RedirectType.Wildcard);
                qmProRule.Source = RuleSource.QmPro;
                qmProRule.ExternalDocumentId = "site|list|item";
                sut.Initialize(new[] { areaRule, qmProRule });

                sut.GetByExternalDocumentId("5b0c2a4e-0000-4000-8000-000000000001").Should().BeNull();
                sut.GetByExternalDocumentId("site|list|item").Should().BeSameAs(qmProRule);
            }
        }
    }
}
