using Broot.Redirect.API.Dtos;
using Broot.Redirect.API.Services;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Broot.Redirect.Core.Services;
using Broot.Redirect.Infrastructure.Cache;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Broot.Redirect.Tests.API.Services
{
    public class AreaRuleSyncServiceTests
    {
        private const string IdA = "5b0c2a4e-0000-4000-8000-00000000000a";
        private const string IdB = "5b0c2a4e-0000-4000-8000-00000000000b";
        private const string IdC = "5b0c2a4e-0000-4000-8000-00000000000c";
        private const string TargetA = "https://tenant.sharepoint.com/sites/QM/BereichA";
        private const string TargetB = "https://tenant.sharepoint.com/sites/QM/BereichB";

        private readonly IRedirectRuleRepository _repository;
        private readonly RuleCacheService _cache;
        private readonly IntegrationWriteLock _writeLock;
        private readonly ILogger<AreaRuleSyncService> _logger;
        private readonly AreaRuleSyncService _sut;

        public AreaRuleSyncServiceTests()
        {
            _repository = Substitute.For<IRedirectRuleRepository>();

            _cache = new RuleCacheService(
                Options.Create(new CacheOptions()),
                Substitute.For<ILogger<RuleCacheService>>());
            _cache.Initialize(new List<RedirectRule>());

            _writeLock = new IntegrationWriteLock();
            _logger = Substitute.For<ILogger<AreaRuleSyncService>>();

            _sut = new AreaRuleSyncService(
                _repository,
                _cache,
                new RuleValidationService(),
                _writeLock,
                _logger);
        }

        private static AreaRuleSyncEntry Entry(string? id, string? sourceUrl, string? targetUrl = TargetA, bool? active = null)
        {
            return new AreaRuleSyncEntry { Id = id, SourceUrl = sourceUrl, TargetUrl = targetUrl, Active = active };
        }

        private Task<AreaRuleSyncResponse> SyncAsync(params AreaRuleSyncEntry?[] entries)
        {
            return _sut.SyncAsync(entries);
        }

        private List<RedirectRule> AreaRules()
        {
            return _cache.GetAll().Where(rule => rule.Source == RuleSource.AreaList).ToList();
        }

        private RedirectRule AreaRule(string id)
        {
            return AreaRules().Single(rule => rule.ExternalDocumentId == id);
        }

        private RedirectRule AddRule(
            string matcher,
            RuleSource source,
            RedirectType redirectType = RedirectType.Wildcard,
            bool isActive = true,
            string? externalDocumentId = null)
        {
            var rule = new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = matcher,
                TargetUrl = "https://tenant.sharepoint.com/other",
                RedirectType = redirectType,
                Source = source,
                IsActive = isActive,
                ExternalDocumentId = externalDocumentId,
                CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };

            _cache.AddRule(rule);

            return rule;
        }

        public class CreateTests : AreaRuleSyncServiceTests
        {
            [Fact]
            public async Task Sync_FolderUrl_CreatesWildcardRuleWithoutAutoRedirect()
            {
                var response = await SyncAsync(Entry(IdA, "/ims/KVP_Innovation/11%20Prozessabweichungen/"));

                response.Created.Should().Be(1);
                response.Skipped.Should().BeEmpty();

                var rule = AreaRule(IdA);
                rule.Matcher.Should().Be("/ims/KVP_Innovation/11%20Prozessabweichungen");
                rule.RedirectType.Should().Be(RedirectType.Wildcard);
                rule.TargetUrl.Should().Be(TargetA);
                rule.AutoRedirect.Should().BeFalse();
                rule.IsActive.Should().BeTrue();
                rule.Source.Should().Be(RuleSource.AreaList);

                await _repository.Received(1).BulkUpsertAsync(
                    Arg.Is<IReadOnlyList<RedirectRule>>(rules => rules.Single().Id == rule.Id),
                    Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Sync_UrlWithoutTrailingSlash_CreatesWildcardRuleWithUnchangedQuery()
            {
                await SyncAsync(Entry(IdA, "/ims/KVP_Innovation/Forms/AllItems.aspx?RootFolder=%2Fims%2FKVP&View=%7B1%7D"));

                var rule = AreaRule(IdA);
                rule.RedirectType.Should().Be(RedirectType.Wildcard);
                rule.Matcher.Should().Be("/ims/KVP_Innovation/Forms/AllItems.aspx?RootFolder=%2Fims%2FKVP&View=%7B1%7D");
            }

            [Fact]
            public async Task Sync_FolderUrlWithQuery_CreatesWildcardRule()
            {
                await SyncAsync(Entry(IdA, "/ims/A/?Web=1"));

                AreaRule(IdA).RedirectType.Should().Be(RedirectType.Wildcard);
            }

            [Fact]
            public async Task Sync_AbsoluteSourceUrl_DiscardsSchemeHostAndFragment()
            {
                await SyncAsync(Entry(IdA, "http://services.ad.lihogr.ch/ims/A/Dokument.docx?Web=1#seite2"));

                AreaRule(IdA).Matcher.Should().Be("/ims/A/Dokument.docx?Web=1");
            }

            [Fact]
            public async Task Sync_StoresNormalizedIdentifierOnRule()
            {
                await SyncAsync(Entry("  " + IdA.ToUpperInvariant() + " ", "/ims/A/"));

                AreaRules().Single().ExternalDocumentId.Should().Be(IdA);
            }

            [Fact]
            public async Task Sync_DoesNotIndexAreaRulesByDocumentId()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"));

                _cache.GetByExternalDocumentId(IdA).Should().BeNull();
            }
        }

        public class UpdateTests : AreaRuleSyncServiceTests
        {
            [Fact]
            public async Task Sync_ChangedSourceUrl_UpdatesRuleKeepingIdCreatedAtAndSettings()
            {
                await SyncAsync(Entry(IdA, "/ims/A/Dokument.docx"));
                var created = AreaRule(IdA);
                created.InfoText = "Hinweis";
                _repository.ClearReceivedCalls();

                var response = await SyncAsync(Entry(IdA, "/ims/A/"));

                response.Updated.Should().Be(1);
                response.Created.Should().Be(0);
                var updated = AreaRule(IdA);
                updated.Id.Should().Be(created.Id);
                updated.CreatedAt.Should().Be(created.CreatedAt);
                updated.InfoText.Should().Be("Hinweis");
                updated.Matcher.Should().Be("/ims/A");
                updated.RedirectType.Should().Be(RedirectType.Wildcard);
                await _repository.Received(1).BulkUpsertAsync(
                    Arg.Is<IReadOnlyList<RedirectRule>>(rules => rules.Single().Id == created.Id),
                    Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Sync_ChangedTargetUrl_UpdatesRule()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"));

                var response = await SyncAsync(Entry(IdA, "/ims/A/", TargetB));

                response.Updated.Should().Be(1);
                AreaRule(IdA).TargetUrl.Should().Be(TargetB);
            }

            [Fact]
            public async Task Sync_UnchangedState_ChangesNothingAndDoesNotWrite()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/B/x.aspx?a=1"));
                var before = AreaRules().Select(rule => (rule.Id, rule.Matcher, rule.TargetUrl, rule.RedirectType)).ToList();
                _repository.ClearReceivedCalls();

                var response = await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/B/x.aspx?a=1"));

                response.Unchanged.Should().Be(2);
                response.Created.Should().Be(0);
                response.Updated.Should().Be(0);
                response.Deleted.Should().Be(0);
                AreaRules().Select(rule => (rule.Id, rule.Matcher, rule.TargetUrl, rule.RedirectType))
                    .Should().BeEquivalentTo(before);
                await _repository.DidNotReceive().BulkUpsertAsync(Arg.Any<IReadOnlyList<RedirectRule>>(), Arg.Any<CancellationToken>());
                await _repository.DidNotReceive().BulkDeleteAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Sync_SwappedSourceUrls_UpdatesBothWithoutSkipping()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/B/"));

                var response = await SyncAsync(Entry(IdA, "/ims/B/"), Entry(IdB, "/ims/A/"));

                response.Updated.Should().Be(2);
                response.Skipped.Should().BeEmpty();
                AreaRule(IdA).Matcher.Should().Be("/ims/B");
                AreaRule(IdB).Matcher.Should().Be("/ims/A");
            }
        }

        public class DeleteTests : AreaRuleSyncServiceTests
        {
            [Fact]
            public async Task Sync_MissingIdentifier_DeletesRule()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/B/"));
                var ruleB = AreaRule(IdB);

                var response = await SyncAsync(Entry(IdA, "/ims/A/"));

                response.Deleted.Should().Be(1);
                AreaRules().Should().ContainSingle().Which.ExternalDocumentId.Should().Be(IdA);
                _cache.GetById(ruleB.Id).Should().BeNull();
                await _repository.Received(1).BulkDeleteAsync(
                    Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == ruleB.Id),
                    Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Sync_EmptyState_DeletesAllAreaRulesAndKeepsOtherOrigins()
            {
                var manual = AddRule("/manual/a", RuleSource.Manual);
                var qmPro = AddRule("/qm/a", RuleSource.QmPro, externalDocumentId: "site|list|item");
                var imported = AddRule("/import/a", RuleSource.Import);
                await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/B/"));

                var response = await SyncAsync();

                response.Deleted.Should().Be(2);
                AreaRules().Should().BeEmpty();
                _cache.GetById(manual.Id).Should().NotBeNull();
                _cache.GetById(qmPro.Id).Should().NotBeNull();
                _cache.GetById(imported.Id).Should().NotBeNull();
            }

            [Fact]
            public async Task Sync_DuplicateRulesForSameIdentifier_KeepsOldestAndDeletesOthers()
            {
                var oldest = AddRule("/ims/A", RuleSource.AreaList, RedirectType.Partial, externalDocumentId: IdA);
                var younger = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/ims/A2",
                    TargetUrl = TargetA,
                    Source = RuleSource.AreaList,
                    ExternalDocumentId = IdA,
                    CreatedAt = oldest.CreatedAt.AddDays(1)
                };
                _cache.AddRule(younger);

                var response = await SyncAsync(Entry(IdA, "/ims/A/"));

                response.Deleted.Should().Be(1);
                _cache.GetById(younger.Id).Should().BeNull();
                AreaRule(IdA).Id.Should().Be(oldest.Id);
            }
        }

        public class ValidationTests : AreaRuleSyncServiceTests
        {
            [Theory]
            [InlineData("")]
            [InlineData("   ")]
            [InlineData(null)]
            [InlineData("kein Link")]
            [InlineData("ims/A/")]
            [InlineData("//intranet/ims/A/")]
            [InlineData("ftp://intranet/ims/A/")]
            [InlineData("file://server/share/A.docx")]
            [InlineData("/")]
            [InlineData("http://intranet/")]
            public async Task Sync_InvalidSourceUrl_SkipsEntryAndSyncsOthers(string? sourceUrl)
            {
                var response = await SyncAsync(Entry(IdA, sourceUrl), Entry(IdB, "/ims/B/"));

                response.Created.Should().Be(1);
                var skipped = response.Skipped.Should().ContainSingle().Subject;
                skipped.Id.Should().Be(IdA);
                skipped.Code.Should().Be(AreaRuleSyncService.ValidationErrorCode);
                skipped.Error.Should().Contain("Quell-URL");
                AreaRule(IdB).Should().NotBeNull();
            }

            [Theory]
            [InlineData("")]
            [InlineData(null)]
            [InlineData("kein Link")]
            [InlineData("/sites/QM/relativ")]
            [InlineData("ftp://tenant/sites/QM")]
            public async Task Sync_InvalidTargetUrl_SkipsEntry(string? targetUrl)
            {
                var response = await SyncAsync(Entry(IdA, "/ims/A/", targetUrl));

                response.Created.Should().Be(0);
                var skipped = response.Skipped.Should().ContainSingle().Subject;
                skipped.Code.Should().Be(AreaRuleSyncService.ValidationErrorCode);
                skipped.Error.Should().Contain("Ziel-URL");
            }

            [Fact]
            public async Task Sync_MissingIdentifierOrNullEntry_SkipsEntry()
            {
                var response = await SyncAsync(Entry(null, "/ims/A/"), Entry(" ", "/ims/B/"), null, Entry(IdC, "/ims/C/"));

                response.Created.Should().Be(1);
                response.Skipped.Should().HaveCount(3)
                    .And.OnlyContain(entry => entry.Code == AreaRuleSyncService.ValidationErrorCode);
            }

            [Fact]
            public async Task Sync_SameIdentifierTwice_SkipsSecondEntry()
            {
                var response = await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdA, "/ims/B/"));

                response.Created.Should().Be(1);
                AreaRule(IdA).Matcher.Should().Be("/ims/A");
                response.Skipped.Should().ContainSingle().Which.Code.Should().Be(AreaRuleSyncService.ValidationErrorCode);
            }

            [Fact]
            public async Task Sync_InvalidEntryWithExistingRule_KeepsRuleUnchanged()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"));
                var before = AreaRule(IdA);

                var response = await SyncAsync(Entry(IdA, "kein Link"));

                response.Deleted.Should().Be(0);
                response.Skipped.Should().ContainSingle().Which.Id.Should().Be(IdA);
                AreaRule(IdA).Should().BeSameAs(before);
            }

            [Fact]
            public async Task Sync_SkippedEntries_AreLoggedAsWarnings()
            {
                await SyncAsync(Entry(IdA, "kein Link"), Entry(IdB, "/ims/B/", "kein Link"));

                _logger.ReceivedCalls()
                    .Count(call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                        && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning)
                    .Should().Be(2);
            }

            [Fact]
            public async Task Sync_SkippedEntries_AreReportedInSubmittedOrder()
            {
                AddRule("/ims/C/x", RuleSource.Manual);

                var response = await SyncAsync(
                    Entry(IdA, "/ims/A/"),
                    Entry(IdB, "/ims/a"),
                    Entry(IdC, "/ims/C/x/"),
                    Entry("d", "kein Link"));

                response.Skipped.Select(entry => entry.Id).Should().Equal(IdB, IdC, "d");
            }
        }

        public class DuplicateEntryTests : AreaRuleSyncServiceTests
        {
            [Theory]
            [InlineData("/ims/A/", "/ims/A")]
            [InlineData("/ims/A%20B/", "/ims/a b/")]
            [InlineData("/ims/x.aspx?a=1&b=2", "/IMS/X.aspx?b=2&a=1")]
            [InlineData("/ims/A/", "http://other-host/ims/A/#top")]
            public async Task Sync_EquivalentSourceUrlsWithoutRule_KeepsOldestEntryAndSkipsOthers(string first, string second)
            {
                var response = await SyncAsync(Entry(IdA, first), Entry(IdB, second, TargetB));

                response.Created.Should().Be(1);
                AreaRules().Should().ContainSingle().Which.ExternalDocumentId.Should().Be(IdA);
                var skipped = response.Skipped.Should().ContainSingle().Subject;
                skipped.Id.Should().Be(IdB);
                skipped.Code.Should().Be(AreaRuleSyncService.DuplicateEntryCode);
                skipped.Error.Should().Contain(IdA);
            }

            [Fact]
            public async Task Sync_EquivalentSourceUrls_EntryWithExistingRuleWins()
            {
                await SyncAsync(Entry(IdB, "/ims/A/", TargetB));
                var existing = AreaRule(IdB);

                var response = await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/A/", TargetB));

                response.Unchanged.Should().Be(1);
                response.Created.Should().Be(0);
                AreaRules().Should().ContainSingle().Which.Id.Should().Be(existing.Id);
                var skipped = response.Skipped.Should().ContainSingle().Subject;
                skipped.Id.Should().Be(IdA);
                skipped.Code.Should().Be(AreaRuleSyncService.DuplicateEntryCode);
                skipped.ConflictingRuleId.Should().Be(existing.Id);
            }

            [Fact]
            public async Task Sync_ThreeEquivalentEntries_SkipsAllButOne()
            {
                var response = await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/A"), Entry(IdC, "/IMS/a/"));

                response.Created.Should().Be(1);
                response.Skipped.Select(entry => entry.Id).Should().Equal(IdB, IdC);
            }
        }

        public class ConflictTests : AreaRuleSyncServiceTests
        {
            [Theory]
            [InlineData(RuleSource.Manual, true)]
            [InlineData(RuleSource.Import, true)]
            [InlineData(RuleSource.QmPro, true)]
            [InlineData(RuleSource.QmPro, false)]
            public async Task Sync_EquivalentMatcherOfOtherOrigin_SkipsWithMatcherDuplicate(RuleSource source, bool isActive)
            {
                var other = AddRule("/ims/A/Dokument.docx", source, isActive: isActive);

                var response = await SyncAsync(Entry(IdA, "/ims/A/Dokument.docx"), Entry(IdB, "/ims/B/"));

                response.Created.Should().Be(1);
                var skipped = response.Skipped.Should().ContainSingle().Subject;
                skipped.Id.Should().Be(IdA);
                skipped.Code.Should().Be(AreaRuleSyncService.MatcherDuplicateCode);
                skipped.ConflictingRuleId.Should().Be(other.Id);
                _cache.GetById(other.Id).Should().BeSameAs(other);
            }

            [Theory]
            [InlineData(RuleSource.Manual, true)]
            [InlineData(RuleSource.Import, true)]
            [InlineData(RuleSource.QmPro, true)]
            [InlineData(RuleSource.QmPro, false)]
            public async Task Sync_FolderAboveDocumentRuleOfOtherOrigin_IsCreated(RuleSource source, bool isActive)
            {
                var document = AddRule("/ims/A/Dokument.docx", source, isActive: isActive);

                var response = await SyncAsync(Entry(IdA, "/ims/A/"));

                response.Created.Should().Be(1);
                response.Skipped.Should().BeEmpty();
                var rule = AreaRules().Should().ContainSingle().Subject;
                rule.Matcher.Should().Be("/ims/A");
                rule.RedirectType.Should().Be(RedirectType.Wildcard);
                _cache.GetById(document.Id).Should().BeSameAs(document);
            }

            [Fact]
            public async Task Sync_DocumentBelowFolderRuleOfOtherOrigin_IsCreated()
            {
                AddRule("/ims", RuleSource.Manual, RedirectType.Partial);

                var response = await SyncAsync(Entry(IdA, "/ims/A/Dokument.docx"));

                response.Created.Should().Be(1);
                response.Skipped.Should().BeEmpty();
                AreaRule(IdA).RedirectType.Should().Be(RedirectType.Wildcard);
            }

            [Theory]
            [InlineData("/ims", "/ims/A/")]
            [InlineData("/ims/A/B", "/ims/A/")]
            public async Task Sync_FolderNestedWithFolderRuleOfOtherOrigin_IsCreatedAsWildcardWithoutConflict(string manualFolder, string entryFolder)
            {
                var manual = AddRule(manualFolder, RuleSource.Manual, RedirectType.Partial);

                var response = await SyncAsync(Entry(IdA, entryFolder));

                response.Created.Should().Be(1);
                response.Skipped.Should().BeEmpty();
                AreaRule(IdA).RedirectType.Should().Be(RedirectType.Wildcard);
                _cache.GetById(manual.Id).Should().BeSameAs(manual);
            }

            [Fact]
            public async Task Sync_RegexRuleOfOtherOrigin_IsNoConflict()
            {
                AddRule("^/ims/.*$", RuleSource.Manual, RedirectType.Regex);

                var response = await SyncAsync(Entry(IdA, "/ims/A/"));

                response.Created.Should().Be(1);
            }

            [Fact]
            public async Task Sync_NestedAreaFolders_AreCreatedWithoutConflict()
            {
                var response = await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/A/B/", TargetB), Entry(IdC, "/ims/A/B/x.docx"));

                response.Created.Should().Be(3);
                response.Skipped.Should().BeEmpty();
                AreaRules().Should().OnlyContain(rule => rule.RedirectType == RedirectType.Wildcard);
            }

            [Fact]
            public async Task Sync_ConflictForExistingRule_KeepsRuleUnchanged()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"));
                var before = AreaRule(IdA);
                AddRule("/ims/neu.docx", RuleSource.Manual);

                var response = await SyncAsync(Entry(IdA, "/ims/neu.docx"));

                response.Skipped.Should().ContainSingle().Which.Code.Should().Be(AreaRuleSyncService.MatcherDuplicateCode);
                response.Deleted.Should().Be(0);
                AreaRule(IdA).Should().BeSameAs(before);
            }
        }

        public class WildcardTests : AreaRuleSyncServiceTests
        {
            [Fact]
            public async Task Sync_ExistingPartialAreaRule_BecomesWildcardKeepingIdAndCreatedAt()
            {
                var partial = AddRule("/ims/A", RuleSource.AreaList, RedirectType.Partial, externalDocumentId: IdA);
                partial.TargetUrl = TargetA;

                var response = await SyncAsync(Entry(IdA, "/ims/A/"));

                response.Updated.Should().Be(1);
                response.Created.Should().Be(0);
                response.Activated.Should().Be(0);
                response.Deactivated.Should().Be(0);
                var updated = AreaRule(IdA);
                updated.Id.Should().Be(partial.Id);
                updated.CreatedAt.Should().Be(partial.CreatedAt);
                updated.RedirectType.Should().Be(RedirectType.Wildcard);
                updated.Matcher.Should().Be("/ims/A");
                _cache.GetPartialAndDomainRules().Should().BeEmpty();
            }

            [Fact]
            public async Task Sync_FolderUrl_RedirectsOnlyTheFolderItself()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"));
                var matching = new RuleMatchingService(_cache);

                matching.ResolveMatch("/ims/A/", new RuleMatchingConfig())!.Rule.ExternalDocumentId.Should().Be(IdA);
                matching.ResolveMatch("/ims/A/Dokument.docx", new RuleMatchingConfig()).Should().BeNull();
            }
        }

        public class ActiveStatusTests : AreaRuleSyncServiceTests
        {
            [Fact]
            public async Task Sync_InactiveEntryWithoutRule_CreatesDeactivatedRule()
            {
                var response = await SyncAsync(Entry(IdA, "/ims/A/Dokument.docx", active: false));

                response.Created.Should().Be(1);
                response.Updated.Should().Be(0);
                response.Activated.Should().Be(0);
                response.Deactivated.Should().Be(0);
                var rule = AreaRule(IdA);
                rule.IsActive.Should().BeFalse();
                rule.RedirectType.Should().Be(RedirectType.Wildcard);
                rule.Source.Should().Be(RuleSource.AreaList);
                await _repository.Received(1).BulkUpsertAsync(
                    Arg.Is<IReadOnlyList<RedirectRule>>(rules => rules.Single().IsActive == false),
                    Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Sync_ActiveEntryBecomesInactive_DeactivatesRuleKeepingIdCreatedAtAndSettings()
            {
                await SyncAsync(Entry(IdA, "/ims/A/Dokument.docx"));
                var created = AreaRule(IdA);
                created.InfoText = "Hinweis";
                created.AutoRedirect = true;
                created.DiscardQueryParams = true;

                var response = await SyncAsync(Entry(IdA, "/ims/A/Dokument.docx", active: false));

                response.Updated.Should().Be(1);
                response.Deactivated.Should().Be(1);
                response.Activated.Should().Be(0);
                var deactivated = AreaRule(IdA);
                deactivated.IsActive.Should().BeFalse();
                deactivated.Id.Should().Be(created.Id);
                deactivated.CreatedAt.Should().Be(created.CreatedAt);
                deactivated.InfoText.Should().Be("Hinweis");
                deactivated.AutoRedirect.Should().BeTrue();
                deactivated.DiscardQueryParams.Should().BeTrue();
                deactivated.ExternalDocumentId.Should().Be(IdA);
                deactivated.Source.Should().Be(RuleSource.AreaList);
            }

            [Fact]
            public async Task Sync_InactiveEntryBecomesActive_ActivatesRuleKeepingIdCreatedAtAndSettings()
            {
                await SyncAsync(Entry(IdA, "/ims/A/Dokument.docx", active: false));
                var created = AreaRule(IdA);
                created.InfoText = "Hinweis";
                created.AutoRedirect = true;

                var response = await SyncAsync(Entry(IdA, "/ims/A/Dokument.docx", active: true));

                response.Updated.Should().Be(1);
                response.Activated.Should().Be(1);
                response.Deactivated.Should().Be(0);
                var activated = AreaRule(IdA);
                activated.IsActive.Should().BeTrue();
                activated.Id.Should().Be(created.Id);
                activated.CreatedAt.Should().Be(created.CreatedAt);
                activated.InfoText.Should().Be("Hinweis");
                activated.AutoRedirect.Should().BeTrue();
            }

            [Fact]
            public async Task Sync_MissingOrNullActive_IsTreatedAsActive()
            {
                await SyncAsync(Entry(IdA, "/ims/A/", active: false), Entry(IdB, "/ims/B/", active: false));

                var response = await SyncAsync(
                    Entry(IdA, "/ims/A/", active: null),
                    new AreaRuleSyncEntry { Id = IdB, SourceUrl = "/ims/B/", TargetUrl = TargetA },
                    new AreaRuleSyncEntry { Id = IdC, SourceUrl = "/ims/C/", TargetUrl = TargetA });

                response.Activated.Should().Be(2);
                response.Created.Should().Be(1);
                AreaRules().Should().HaveCount(3).And.OnlyContain(rule => rule.IsActive);
            }

            [Fact]
            public async Task Sync_UnchangedStateWithInactiveEntries_DoesNotWrite()
            {
                await SyncAsync(Entry(IdA, "/ims/A/", active: false), Entry(IdB, "/ims/B/x.docx"));
                _repository.ClearReceivedCalls();

                var response = await SyncAsync(Entry(IdA, "/ims/A/", active: false), Entry(IdB, "/ims/B/x.docx"));

                response.Unchanged.Should().Be(2);
                response.Updated.Should().Be(0);
                response.Activated.Should().Be(0);
                response.Deactivated.Should().Be(0);
                AreaRule(IdA).IsActive.Should().BeFalse();
                await _repository.DidNotReceive().BulkUpsertAsync(Arg.Any<IReadOnlyList<RedirectRule>>(), Arg.Any<CancellationToken>());
                await _repository.DidNotReceive().BulkDeleteAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Sync_RemovedEntry_DeletesDeactivatedRule()
            {
                await SyncAsync(Entry(IdA, "/ims/A/", active: false), Entry(IdB, "/ims/B/"));
                var deactivated = AreaRule(IdA);

                var response = await SyncAsync(Entry(IdB, "/ims/B/"));

                response.Deleted.Should().Be(1);
                _cache.GetById(deactivated.Id).Should().BeNull();
                await _repository.Received(1).BulkDeleteAsync(
                    Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == deactivated.Id),
                    Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Sync_InvalidInactiveEntry_IsSkippedWithValidationError()
            {
                var response = await SyncAsync(Entry(IdA, "kein Link", active: false), Entry(IdB, "/ims/B/", "kein Link", active: false));

                response.Created.Should().Be(0);
                response.Skipped.Should().HaveCount(2)
                    .And.OnlyContain(entry => entry.Code == AreaRuleSyncService.ValidationErrorCode);
                AreaRules().Should().BeEmpty();
            }

            [Fact]
            public async Task Sync_InactiveEntryWithEquivalentMatcherOfOtherOrigin_SkipsWithMatcherDuplicate()
            {
                var other = AddRule("/ims/A/Dokument.docx", RuleSource.QmPro, externalDocumentId: "site|list|item");

                var response = await SyncAsync(Entry(IdA, "/ims/A/Dokument.docx", active: false));

                response.Created.Should().Be(0);
                var skipped = response.Skipped.Should().ContainSingle().Subject;
                skipped.Code.Should().Be(AreaRuleSyncService.MatcherDuplicateCode);
                skipped.ConflictingRuleId.Should().Be(other.Id);
                AreaRules().Should().BeEmpty();
            }

            [Fact]
            public async Task Sync_MixedStatusChanges_ReportsActivatedAndDeactivatedAsPartOfUpdated()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/B/", active: false));

                var response = await SyncAsync(
                    Entry(IdA, "/ims/A/", active: false),
                    Entry(IdB, "/ims/B/", TargetB, active: true),
                    Entry(IdC, "/ims/C/", active: false));

                response.Created.Should().Be(1);
                response.Updated.Should().Be(2);
                response.Activated.Should().Be(1);
                response.Deactivated.Should().Be(1);
                response.Unchanged.Should().Be(0);
                AreaRule(IdB).TargetUrl.Should().Be(TargetB);
            }

            [Fact]
            public async Task Sync_ChangedUrlOfInactiveEntry_UpdatesWithoutActivating()
            {
                await SyncAsync(Entry(IdA, "/ims/A/", active: false));

                var response = await SyncAsync(Entry(IdA, "/ims/A2/", TargetB, active: false));

                response.Updated.Should().Be(1);
                response.Activated.Should().Be(0);
                response.Deactivated.Should().Be(0);
                var rule = AreaRule(IdA);
                rule.IsActive.Should().BeFalse();
                rule.Matcher.Should().Be("/ims/A2");
                rule.TargetUrl.Should().Be(TargetB);
            }

            [Fact]
            public async Task Sync_DeactivatedAreaRule_IsNotMatched()
            {
                await SyncAsync(Entry(IdA, "/ims/A/Dokument.docx"));
                var matching = new RuleMatchingService(_cache);
                matching.ResolveMatch("/ims/A/Dokument.docx", new RuleMatchingConfig()).Should().NotBeNull();

                await SyncAsync(Entry(IdA, "/ims/A/Dokument.docx", active: false));

                matching.ResolveMatch("/ims/A/Dokument.docx", new RuleMatchingConfig()).Should().BeNull();
                _cache.LookupWildcardCandidates("/ims/a/dokument.docx").Should().BeEmpty();
                AreaRule(IdA).IsActive.Should().BeFalse();
            }
        }

        public class ActiveWinnerTests : AreaRuleSyncServiceTests
        {
            [Fact]
            public async Task Sync_ActiveAndInactiveEquivalentEntries_ActiveEntryWins()
            {
                var response = await SyncAsync(Entry(IdA, "/ims/A/", active: false), Entry(IdB, "/ims/A", TargetB, active: true));

                response.Created.Should().Be(1);
                var rule = AreaRules().Should().ContainSingle().Subject;
                rule.ExternalDocumentId.Should().Be(IdB);
                rule.IsActive.Should().BeTrue();
                var skipped = response.Skipped.Should().ContainSingle().Subject;
                skipped.Id.Should().Be(IdA);
                skipped.Code.Should().Be(AreaRuleSyncService.DuplicateEntryCode);
                skipped.Error.Should().Contain(IdB);
            }

            [Fact]
            public async Task Sync_ActiveEntryWinsAgainstInactiveEntryWithExistingRule()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"));
                var previous = AreaRule(IdA);

                var response = await SyncAsync(Entry(IdA, "/ims/A/", active: false), Entry(IdB, "/ims/A/", TargetB, active: true));

                response.Created.Should().Be(1);
                response.Updated.Should().Be(1);
                response.Deactivated.Should().Be(1);
                response.Skipped.Should().ContainSingle().Which.Id.Should().Be(IdA);
                AreaRule(IdB).IsActive.Should().BeTrue();
                var loser = AreaRule(IdA);
                loser.Id.Should().Be(previous.Id);
                loser.IsActive.Should().BeFalse();
                AreaRules().Count(rule => rule.IsActive).Should().Be(1);
            }

            [Fact]
            public async Task Sync_InactiveEquivalentEntries_EntryWithExistingRuleWins()
            {
                await SyncAsync(Entry(IdB, "/ims/A/", TargetB, active: false));
                var existing = AreaRule(IdB);

                var response = await SyncAsync(Entry(IdA, "/ims/A/", active: false), Entry(IdB, "/ims/A/", TargetB, active: false));

                response.Unchanged.Should().Be(1);
                response.Created.Should().Be(0);
                AreaRules().Should().ContainSingle().Which.Id.Should().Be(existing.Id);
                var skipped = response.Skipped.Should().ContainSingle().Subject;
                skipped.Id.Should().Be(IdA);
                skipped.ConflictingRuleId.Should().Be(existing.Id);
            }

            [Fact]
            public async Task Sync_InactiveEquivalentEntriesWithoutRule_OldestEntryWins()
            {
                var response = await SyncAsync(Entry(IdA, "/ims/A/", active: false), Entry(IdB, "/ims/A", TargetB, active: false));

                response.Created.Should().Be(1);
                var rule = AreaRules().Should().ContainSingle().Subject;
                rule.ExternalDocumentId.Should().Be(IdA);
                rule.IsActive.Should().BeFalse();
                response.Skipped.Should().ContainSingle().Which.Id.Should().Be(IdB);
            }

            [Fact]
            public async Task Sync_LoserWithActiveEquivalentRule_IsDeactivated()
            {
                var ruleA = AddRule("/ims/A", RuleSource.AreaList, externalDocumentId: IdA);
                var ruleB = AddRule("/ims/A", RuleSource.AreaList, externalDocumentId: IdB);
                ruleB.CreatedAt = ruleA.CreatedAt.AddDays(1);

                var response = await SyncAsync(Entry(IdA, "/ims/A"), Entry(IdB, "/ims/A/", TargetB));

                response.Skipped.Should().ContainSingle().Which.Id.Should().Be(IdB);
                response.Deactivated.Should().Be(1);
                AreaRule(IdA).IsActive.Should().BeTrue();
                var loser = AreaRule(IdB);
                loser.Id.Should().Be(ruleB.Id);
                loser.IsActive.Should().BeFalse();
                loser.Matcher.Should().Be("/ims/A");
                loser.TargetUrl.Should().Be(ruleB.TargetUrl);
            }

            [Fact]
            public async Task Sync_ActiveLoserWithNonEquivalentRule_KeepsRuleUnchanged()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/B/"));
                var ruleB = AreaRule(IdB);

                var response = await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/A/"));

                response.Skipped.Should().ContainSingle().Which.Id.Should().Be(IdB);
                response.Updated.Should().Be(0);
                AreaRule(IdB).Should().BeSameAs(ruleB);
            }
        }

        public class SkippedInactiveEntryTests : AreaRuleSyncServiceTests
        {
            [Fact]
            public async Task Sync_InvalidInactiveEntryWithActiveRule_OnlyDeactivatesRule()
            {
                var partial = AddRule("/ims/A", RuleSource.AreaList, RedirectType.Partial, externalDocumentId: IdA);

                var response = await SyncAsync(Entry(IdA, "kein Link", "https://tenant.sharepoint.com/neu", active: false));

                response.Skipped.Should().ContainSingle().Which.Code.Should().Be(AreaRuleSyncService.ValidationErrorCode);
                response.Updated.Should().Be(1);
                response.Deactivated.Should().Be(1);
                response.Deleted.Should().Be(0);
                var rule = AreaRule(IdA);
                rule.Id.Should().Be(partial.Id);
                rule.IsActive.Should().BeFalse();
                rule.RedirectType.Should().Be(RedirectType.Partial);
                rule.Matcher.Should().Be("/ims/A");
                rule.TargetUrl.Should().Be(partial.TargetUrl);
                rule.CreatedAt.Should().Be(partial.CreatedAt);
            }

            [Fact]
            public async Task Sync_InvalidActiveEntryWithDeactivatedRule_DoesNotActivateRule()
            {
                await SyncAsync(Entry(IdA, "/ims/A/", active: false));
                var before = AreaRule(IdA);

                var response = await SyncAsync(Entry(IdA, "kein Link", active: true));

                response.Skipped.Should().ContainSingle().Which.Code.Should().Be(AreaRuleSyncService.ValidationErrorCode);
                response.Updated.Should().Be(0);
                response.Activated.Should().Be(0);
                AreaRule(IdA).Should().BeSameAs(before);
                AreaRule(IdA).IsActive.Should().BeFalse();
            }

            [Fact]
            public async Task Sync_InvalidInactiveEntryWithDeactivatedRule_DoesNotWrite()
            {
                await SyncAsync(Entry(IdA, "/ims/A/", active: false));
                _repository.ClearReceivedCalls();

                var response = await SyncAsync(Entry(IdA, "kein Link", active: false));

                response.Updated.Should().Be(0);
                response.Deactivated.Should().Be(0);
                await _repository.DidNotReceive().BulkUpsertAsync(Arg.Any<IReadOnlyList<RedirectRule>>(), Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Sync_InactiveLoserWithActiveRuleOnOtherMatcher_OnlyDeactivatesRule()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/B/"));
                var ruleB = AreaRule(IdB);

                var response = await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdB, "/ims/A/", TargetB, active: false));

                response.Skipped.Should().ContainSingle().Which.Code.Should().Be(AreaRuleSyncService.DuplicateEntryCode);
                response.Deactivated.Should().Be(1);
                var rule = AreaRule(IdB);
                rule.Id.Should().Be(ruleB.Id);
                rule.IsActive.Should().BeFalse();
                rule.Matcher.Should().Be("/ims/B");
                rule.TargetUrl.Should().Be(TargetA);
                AreaRule(IdA).IsActive.Should().BeTrue();
            }

            [Fact]
            public async Task Sync_InactiveEntryWithConflictOfOtherOrigin_OnlyDeactivatesItsRule()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"));
                var before = AreaRule(IdA);
                AddRule("/ims/neu.docx", RuleSource.Manual);

                var response = await SyncAsync(Entry(IdA, "/ims/neu.docx", active: false));

                response.Skipped.Should().ContainSingle().Which.Code.Should().Be(AreaRuleSyncService.MatcherDuplicateCode);
                response.Deactivated.Should().Be(1);
                var rule = AreaRule(IdA);
                rule.Id.Should().Be(before.Id);
                rule.IsActive.Should().BeFalse();
                rule.Matcher.Should().Be("/ims/A");
            }

            [Fact]
            public async Task Sync_SecondEntryWithSameIdentifier_DoesNotDeactivateRule()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"));

                var response = await SyncAsync(Entry(IdA, "/ims/A/"), Entry(IdA, "kein Link", active: false));

                response.Unchanged.Should().Be(1);
                response.Deactivated.Should().Be(0);
                AreaRule(IdA).IsActive.Should().BeTrue();
            }
        }

        public class PersistenceTests : AreaRuleSyncServiceTests
        {
            [Fact]
            public async Task Sync_WritesStorageBeforeCache_DeleteBeforeUpsert()
            {
                await SyncAsync(Entry(IdA, "/ims/A/"));
                _repository.ClearReceivedCalls();

                await SyncAsync(Entry(IdB, "/ims/B/"));

                Received.InOrder(() =>
                {
                    _repository.BulkDeleteAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
                    _repository.BulkUpsertAsync(Arg.Any<IReadOnlyList<RedirectRule>>(), Arg.Any<CancellationToken>());
                });
            }

            [Fact]
            public async Task Sync_StorageFailure_ReloadsCacheFromStorageAndRethrows()
            {
                var stored = AddRule("/stored", RuleSource.Manual);
                _repository.BulkUpsertAsync(Arg.Any<IReadOnlyList<RedirectRule>>(), Arg.Any<CancellationToken>())
                    .ThrowsAsync(new InvalidOperationException("storage down"));
                _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<RedirectRule> { stored });

                var act = () => SyncAsync(Entry(IdA, "/ims/A/"));

                await act.Should().ThrowAsync<InvalidOperationException>();
                AreaRules().Should().BeEmpty();
                _cache.GetById(stored.Id).Should().NotBeNull();
                await _repository.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Sync_WaitsForSharedIntegrationWriteLock()
            {
                await _writeLock.WaitAsync();

                var sync = SyncAsync(Entry(IdA, "/ims/A/"));
                await Task.Delay(50);

                sync.IsCompleted.Should().BeFalse();
                AreaRules().Should().BeEmpty();

                _writeLock.Release();
                (await sync).Created.Should().Be(1);
            }
        }
    }
}
