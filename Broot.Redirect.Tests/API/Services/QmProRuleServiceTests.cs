using Broot.Redirect.API.Dtos;
using Broot.Redirect.API.Services;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Broot.Redirect.Infrastructure.Cache;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.API.Services
{
    public class QmProRuleServiceTests
    {
        private const string DocumentId = "https://tenant.sharepoint.com/sites/QM|bfcdf1ce-0000-4000-8000-000000000001|3f2a0000-0000-4000-8000-000000000002";
        private const string OldUrl = "https://intranet.example.local/sites/qm/Freigegebene Dokumente/Handbuch.docx?web=1#section";
        private const string PublicationUrl = "https://tenant.sharepoint.com/sites/QM/Publikation/Handbuch.docx";

        private readonly IRedirectRuleRepository _repository;
        private readonly RuleCacheService _cache;
        private readonly QmProRuleService _sut;

        public QmProRuleServiceTests()
        {
            _repository = Substitute.For<IRedirectRuleRepository>();
            _repository.UpdateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>()).Returns(true);

            _cache = new RuleCacheService(
                Options.Create(new CacheOptions()),
                Substitute.For<ILogger<RuleCacheService>>());
            _cache.Initialize(new List<RedirectRule>());

            _sut = new QmProRuleService(
                _repository,
                _cache,
                new RuleValidationService(),
                Substitute.For<ILogger<QmProRuleService>>());
        }

        private static QmProPublishRequest PublishRequest(
            string? documentId = DocumentId,
            string? oldUrl = OldUrl,
            string? publicationUrl = PublicationUrl)
        {
            return new QmProPublishRequest
            {
                DocumentId = documentId,
                OldUrl = oldUrl,
                PublicationUrl = publicationUrl
            };
        }

        private static RedirectRule QmProRule(
            string matcher,
            string documentId,
            bool isActive = true,
            string targetUrl = PublicationUrl)
        {
            return new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = matcher,
                TargetUrl = targetUrl,
                RedirectType = RedirectType.Wildcard,
                Source = RuleSource.QmPro,
                ExternalDocumentId = documentId,
                IsActive = isActive,
                CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };
        }

        public class PublishCreateTests : QmProRuleServiceTests
        {
            [Fact]
            public async Task Publish_UnknownDocument_CreatesActiveWildcardRuleFromPathAndQuery()
            {
                var result = await _sut.PublishAsync(PublishRequest());

                result.Status.Should().Be(QmProPublishStatus.Created);

                var rule = result.Rule!;
                rule.Matcher.Should().Be("/sites/qm/Freigegebene%20Dokumente/Handbuch.docx?web=1");
                rule.TargetUrl.Should().Be(PublicationUrl);
                rule.RedirectType.Should().Be(RedirectType.Wildcard);
                rule.AutoRedirect.Should().BeFalse();
                rule.Source.Should().Be(RuleSource.QmPro);
                rule.IsActive.Should().BeTrue();
                rule.ExternalDocumentId.Should().Be(DocumentId);

                await _repository.Received(1).CreateAsync(rule, Arg.Any<CancellationToken>());
                _cache.GetById(rule.Id).Should().BeSameAs(rule);
                _cache.LookupWildcardCandidates("/sites/qm/freigegebene dokumente/handbuch.docx")
                    .Should().ContainSingle().Which.Id.Should().Be(rule.Id);
            }

            [Fact]
            public async Task Publish_DiscardsHostOfOldUrl()
            {
                var result = await _sut.PublishAsync(PublishRequest(oldUrl: "http://other-host:8080/docs/a.pdf"));

                result.Rule!.Matcher.Should().Be("/docs/a.pdf");
            }

            [Fact]
            public async Task Publish_EncodedAndRawOldUrl_ProduceSameMatcher()
            {
                var raw = await _sut.PublishAsync(PublishRequest(documentId: "doc-raw", oldUrl: "https://h/a b/c.docx"));

                QmProRuleService.BuildMatcher(new Uri("https://h/a%20b/c.docx")).Should().Be(raw.Rule!.Matcher);
            }

            [Fact]
            public async Task Publish_StoresTrimmedDocumentId()
            {
                var result = await _sut.PublishAsync(PublishRequest(documentId: "  doc-1  "));

                result.Rule!.ExternalDocumentId.Should().Be("doc-1");
            }
        }

        public class PublishUpdateTests : QmProRuleServiceTests
        {
            [Fact]
            public async Task Publish_KnownDocument_OverwritesMatcherAndTarget_KeepsIdCreatedAtAndSource()
            {
                var existing = QmProRule("/old/path.docx", DocumentId, targetUrl: "https://tenant.sharepoint.com/old.docx");
                _cache.AddRule(existing);

                var result = await _sut.PublishAsync(PublishRequest(oldUrl: "https://h/new/path.docx"));

                result.Status.Should().Be(QmProPublishStatus.Updated);
                result.Rule!.Id.Should().Be(existing.Id);
                result.Rule.CreatedAt.Should().Be(existing.CreatedAt);
                result.Rule.Source.Should().Be(RuleSource.QmPro);
                result.Rule.Matcher.Should().Be("/new/path.docx");
                result.Rule.TargetUrl.Should().Be(PublicationUrl);

                await _repository.Received(1).UpdateAsync(
                    Arg.Is<RedirectRule>(rule => rule.Id == existing.Id && rule.Matcher == "/new/path.docx"),
                    Arg.Any<CancellationToken>());
                await _repository.DidNotReceive().CreateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());
                _cache.GetById(existing.Id)!.Matcher.Should().Be("/new/path.docx");
                _cache.LookupWildcardCandidates("/old/path.docx").Should().BeEmpty();
            }

            [Fact]
            public async Task Publish_KnownDocumentDifferentCase_FindsExistingRule()
            {
                var existing = QmProRule("/old/path.docx", DocumentId.ToUpperInvariant());
                _cache.AddRule(existing);

                var result = await _sut.PublishAsync(PublishRequest());

                result.Status.Should().Be(QmProPublishStatus.Updated);
                result.Rule!.Id.Should().Be(existing.Id);
            }

            [Fact]
            public async Task Publish_DeactivatedRule_ReactivatesRule()
            {
                var existing = QmProRule("/sites/qm/Freigegebene%20Dokumente/Handbuch.docx?web=1", DocumentId, isActive: false);
                _cache.AddRule(existing);

                var result = await _sut.PublishAsync(PublishRequest());

                result.Status.Should().Be(QmProPublishStatus.Updated);
                result.Rule!.IsActive.Should().BeTrue();
                _cache.GetById(existing.Id)!.IsActive.Should().BeTrue();
                _cache.LookupWildcardCandidates("/sites/qm/freigegebene dokumente/handbuch.docx").Should().ContainSingle();
            }

            [Fact]
            public async Task Publish_UnchangedDataForActiveRule_DoesNotWrite()
            {
                var existing = QmProRule("/sites/qm/Freigegebene%20Dokumente/Handbuch.docx?web=1", DocumentId);
                _cache.AddRule(existing);

                var result = await _sut.PublishAsync(PublishRequest());

                result.Status.Should().Be(QmProPublishStatus.Unchanged);
                result.Rule.Should().BeSameAs(existing);
                await _repository.DidNotReceive().UpdateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());
                await _repository.DidNotReceive().CreateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Publish_RepeatedCall_SecondCallIsUnchanged()
            {
                var first = await _sut.PublishAsync(PublishRequest());
                var second = await _sut.PublishAsync(PublishRequest());

                first.Status.Should().Be(QmProPublishStatus.Created);
                second.Status.Should().Be(QmProPublishStatus.Unchanged);
                second.Rule!.Id.Should().Be(first.Rule!.Id);
                _cache.RuleCount.Should().Be(1);
            }

            [Fact]
            public async Task Publish_StorageUpdateFails_LeavesCacheUnchanged()
            {
                var existing = QmProRule("/old/path.docx", DocumentId);
                _cache.AddRule(existing);
                _repository.UpdateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>()).Returns(false);

                var act = () => _sut.PublishAsync(PublishRequest());

                await act.Should().ThrowAsync<InvalidOperationException>();
                _cache.GetById(existing.Id)!.Matcher.Should().Be("/old/path.docx");
            }

            [Fact]
            public async Task Publish_ParallelCallsForSameDocument_CreateOnlyOneRule()
            {
                _repository.CreateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>())
                    .Returns(async _ => await Task.Delay(20));

                var results = await Task.WhenAll(Enumerable.Range(0, 8)
                    .Select(_ => Task.Run(() => _sut.PublishAsync(PublishRequest()))));

                results.Count(result => result.Status == QmProPublishStatus.Created).Should().Be(1);
                results.Count(result => result.Status == QmProPublishStatus.Unchanged).Should().Be(7);
                await _repository.Received(1).CreateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());
            }
        }

        public class PublishValidationTests : QmProRuleServiceTests
        {
            [Theory]
            [InlineData(null, OldUrl, PublicationUrl, "Dokument-ID")]
            [InlineData("  ", OldUrl, PublicationUrl, "Dokument-ID")]
            [InlineData(DocumentId, null, PublicationUrl, "Alte URL")]
            [InlineData(DocumentId, "", PublicationUrl, "Alte URL")]
            [InlineData(DocumentId, OldUrl, null, "Publikations-URL")]
            [InlineData(DocumentId, "/relative/path.docx", PublicationUrl, "Alte URL")]
            [InlineData(DocumentId, "ftp://host/file.docx", PublicationUrl, "Alte URL")]
            [InlineData(DocumentId, "not a url", PublicationUrl, "Alte URL")]
            [InlineData(DocumentId, OldUrl, "/sites/QM/Publikation/a.docx", "Publikations-URL")]
            [InlineData(DocumentId, OldUrl, "mailto:someone@example.com", "Publikations-URL")]
            public async Task Publish_MissingOrInvalidInput_ReturnsValidationErrorWithoutWriting(
                string? documentId,
                string? oldUrl,
                string? publicationUrl,
                string expectedErrorPart)
            {
                var result = await _sut.PublishAsync(PublishRequest(documentId, oldUrl, publicationUrl));

                result.Status.Should().Be(QmProPublishStatus.ValidationFailed);
                result.Error.Should().Contain(expectedErrorPart);
                await _repository.DidNotReceive().CreateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());
                await _repository.DidNotReceive().UpdateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Publish_TooLongMatcher_ReturnsValidationErrorFromRuleValidation()
            {
                var longUrl = "https://h/" + new string('a', 600) + ".docx";

                var result = await _sut.PublishAsync(PublishRequest(oldUrl: longUrl));

                result.Status.Should().Be(QmProPublishStatus.ValidationFailed);
                result.Error.Should().Be("URL-Muster ist zu lang");
            }
        }

        public class PublishConflictTests : QmProRuleServiceTests
        {
            [Fact]
            public async Task Publish_MatcherOfOtherActiveRule_ReturnsDuplicateAndLeavesRuleUnchanged()
            {
                var other = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/sites/qm/Freigegebene%20Dokumente/Handbuch.docx?web=1",
                    TargetUrl = "https://other.example.com/x",
                    RedirectType = RedirectType.Wildcard,
                    Source = RuleSource.Manual
                };
                _cache.AddRule(other);

                var result = await _sut.PublishAsync(PublishRequest());

                result.Status.Should().Be(QmProPublishStatus.MatcherDuplicate);
                result.ConflictingRule!.Id.Should().Be(other.Id);
                _cache.GetById(other.Id)!.TargetUrl.Should().Be("https://other.example.com/x");
                await _repository.DidNotReceive().CreateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Publish_MatcherOfDeactivatedRuleOfOtherDocument_ReturnsDuplicate()
            {
                var other = QmProRule("/sites/qm/Freigegebene%20Dokumente/Handbuch.docx?web=1", "other-document", isActive: false);
                _cache.AddRule(other);

                var result = await _sut.PublishAsync(PublishRequest());

                result.Status.Should().Be(QmProPublishStatus.MatcherDuplicate);
                result.ConflictingRule!.Id.Should().Be(other.Id);
                _cache.GetById(other.Id)!.IsActive.Should().BeFalse();
                _cache.GetById(other.Id)!.ExternalDocumentId.Should().Be("other-document");
            }

            [Fact]
            public async Task Publish_EquivalentMatcherWithDifferentEncodingCaseAndQueryOrder_ReturnsDuplicate()
            {
                var other = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/Docs/A B.docx?y=2&x=1",
                    TargetUrl = "https://other.example.com/x",
                    RedirectType = RedirectType.Wildcard
                };
                _cache.AddRule(other);

                var result = await _sut.PublishAsync(PublishRequest(oldUrl: "https://h/docs/a%20b.docx?x=1&y=2"));

                result.Status.Should().Be(QmProPublishStatus.MatcherDuplicate);
            }

            [Fact]
            public async Task Publish_SamePathWithDifferentQuery_IsAllowed()
            {
                _cache.AddRule(new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/docs/a.docx?x=1",
                    TargetUrl = "https://other.example.com/x",
                    RedirectType = RedirectType.Wildcard
                });

                var result = await _sut.PublishAsync(PublishRequest(oldUrl: "https://h/docs/a.docx?x=2"));

                result.Status.Should().Be(QmProPublishStatus.Created);
            }

            [Fact]
            public async Task Publish_OwnRuleWithSameMatcher_IsNotADuplicate()
            {
                var existing = QmProRule("/docs/a.docx", DocumentId, targetUrl: "https://tenant.sharepoint.com/old.docx");
                _cache.AddRule(existing);

                var result = await _sut.PublishAsync(PublishRequest(oldUrl: "https://h/docs/a.docx"));

                result.Status.Should().Be(QmProPublishStatus.Updated);
            }

            [Fact]
            public async Task Publish_OverlapsPartialRule_ReturnsMatcherConflict()
            {
                var partial = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/sites/qm",
                    TargetUrl = "/new",
                    RedirectType = RedirectType.Partial
                };
                _cache.AddRule(partial);

                var result = await _sut.PublishAsync(PublishRequest());

                result.Status.Should().Be(QmProPublishStatus.MatcherConflict);
                result.ConflictingRule!.Id.Should().Be(partial.Id);
                await _repository.DidNotReceive().CreateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Publish_OverlapsDeactivatedPartialRule_ReturnsMatcherConflict()
            {
                var partial = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/sites/qm",
                    TargetUrl = "/new",
                    RedirectType = RedirectType.Partial,
                    IsActive = false
                };
                _cache.AddRule(partial);

                var result = await _sut.PublishAsync(PublishRequest());

                result.Status.Should().Be(QmProPublishStatus.MatcherConflict);
            }
        }

        public class UnpublishTests : QmProRuleServiceTests
        {
            [Fact]
            public async Task Unpublish_ActiveRule_DeactivatesAndRemovesFromMatching()
            {
                var existing = QmProRule("/docs/a.docx", DocumentId);
                _cache.AddRule(existing);

                var result = await _sut.UnpublishAsync(new QmProUnpublishRequest { DocumentId = DocumentId });

                result.Status.Should().Be(QmProUnpublishStatus.Deactivated);
                result.Rule!.Id.Should().Be(existing.Id);
                await _repository.Received(1).UpdateAsync(
                    Arg.Is<RedirectRule>(rule => rule.Id == existing.Id && !rule.IsActive),
                    Arg.Any<CancellationToken>());
                _cache.GetById(existing.Id)!.IsActive.Should().BeFalse();
                _cache.LookupWildcardCandidates("/docs/a.docx").Should().BeEmpty();
                _cache.GetAll().Should().ContainSingle();
            }

            [Fact]
            public async Task Unpublish_AlreadyInactiveRule_DoesNotWrite()
            {
                var existing = QmProRule("/docs/a.docx", DocumentId, isActive: false);
                _cache.AddRule(existing);

                var result = await _sut.UnpublishAsync(new QmProUnpublishRequest { DocumentId = DocumentId });

                result.Status.Should().Be(QmProUnpublishStatus.AlreadyInactive);
                result.Rule!.Id.Should().Be(existing.Id);
                _cache.GetById(existing.Id)!.IsActive.Should().BeFalse();
                await _repository.DidNotReceive().UpdateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Unpublish_UnknownDocument_ReturnsNotFoundWithoutWriting()
            {
                _cache.AddRule(QmProRule("/docs/a.docx", "other-document"));

                var result = await _sut.UnpublishAsync(new QmProUnpublishRequest { DocumentId = DocumentId });

                result.Status.Should().Be(QmProUnpublishStatus.NotFound);
                result.Rule.Should().BeNull();
                await _repository.DidNotReceive().UpdateAsync(Arg.Any<RedirectRule>(), Arg.Any<CancellationToken>());
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("   ")]
            public async Task Unpublish_MissingDocumentId_ReturnsValidationError(string? documentId)
            {
                var result = await _sut.UnpublishAsync(new QmProUnpublishRequest { DocumentId = documentId });

                result.Status.Should().Be(QmProUnpublishStatus.ValidationFailed);
                result.Error.Should().Contain("Dokument-ID");
            }

            [Fact]
            public async Task PublishAfterUnpublish_ReactivatesSameRule()
            {
                var created = await _sut.PublishAsync(PublishRequest());
                await _sut.UnpublishAsync(new QmProUnpublishRequest { DocumentId = DocumentId });

                var republished = await _sut.PublishAsync(PublishRequest());

                republished.Status.Should().Be(QmProPublishStatus.Updated);
                republished.Rule!.Id.Should().Be(created.Rule!.Id);
                republished.Rule.IsActive.Should().BeTrue();
                _cache.LookupWildcardCandidates("/sites/qm/freigegebene dokumente/handbuch.docx").Should().ContainSingle();
            }
        }
    }
}
