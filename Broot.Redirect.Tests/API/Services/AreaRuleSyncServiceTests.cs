using Broot.Redirect.API.Dtos;
using Broot.Redirect.API.Services;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
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

        private static AreaRuleSyncEntry Entry(string? id, string? sourceUrl, string? targetUrl = TargetA)
        {
            return new AreaRuleSyncEntry { Id = id, SourceUrl = sourceUrl, TargetUrl = targetUrl };
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
            public async Task Sync_FolderUrl_CreatesPartialRuleWithoutAutoRedirect()
            {
                var response = await SyncAsync(Entry(IdA, "/ims/KVP_Innovation/11%20Prozessabweichungen/"));

                response.Created.Should().Be(1);
                response.Skipped.Should().BeEmpty();

                var rule = AreaRule(IdA);
                rule.Matcher.Should().Be("/ims/KVP_Innovation/11%20Prozessabweichungen");
                rule.RedirectType.Should().Be(RedirectType.Partial);
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
            public async Task Sync_FolderUrlWithQuery_DecidesTypeByPathBeforeQuery()
            {
                await SyncAsync(Entry(IdA, "/ims/A/?Web=1"));

                AreaRule(IdA).RedirectType.Should().Be(RedirectType.Partial);
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
                updated.RedirectType.Should().Be(RedirectType.Partial);
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
                AddRule("/ims/C", RuleSource.Manual, RedirectType.Partial);

                var response = await SyncAsync(
                    Entry(IdA, "/ims/A/"),
                    Entry(IdB, "/ims/a"),
                    Entry(IdC, "/ims/C/x.docx"),
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

            [Fact]
            public async Task Sync_FolderOverWildcardOfOtherOrigin_SkipsWithMatcherConflict()
            {
                var manual = AddRule("/ims/A/Dokument.docx", RuleSource.Manual);

                var response = await SyncAsync(Entry(IdA, "/ims/A/"));

                var skipped = response.Skipped.Should().ContainSingle().Subject;
                skipped.Code.Should().Be(AreaRuleSyncService.MatcherConflictCode);
                skipped.ConflictingRuleId.Should().Be(manual.Id);
                AreaRules().Should().BeEmpty();
            }

            [Fact]
            public async Task Sync_UrlBelowPartialRuleOfOtherOrigin_SkipsWithMatcherConflict()
            {
                var manual = AddRule("/ims", RuleSource.Manual, RedirectType.Partial);

                var response = await SyncAsync(Entry(IdA, "/ims/A/Dokument.docx"));

                response.Skipped.Should().ContainSingle().Which.ConflictingRuleId.Should().Be(manual.Id);
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
