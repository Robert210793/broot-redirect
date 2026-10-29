using System.Diagnostics.CodeAnalysis;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;

namespace Broot.Redirect.API.Services
{
    public enum QmProPublishStatus
    {
        Created,
        Updated,
        Unchanged,
        ValidationFailed,
        MatcherDuplicate,
        MatcherConflict
    }

    public enum QmProUnpublishStatus
    {
        Deactivated,
        AlreadyInactive,
        NotFound,
        ValidationFailed
    }

    public sealed record QmProPublishResult(
        QmProPublishStatus Status,
        RedirectRule? Rule = null,
        string? Error = null,
        RedirectRule? ConflictingRule = null);

    public sealed record QmProUnpublishResult(
        QmProUnpublishStatus Status,
        RedirectRule? Rule = null,
        string? Error = null);

    public sealed class QmProRuleService
    {
        private readonly IRedirectRuleRepository _repository;
        private readonly IRuleCacheService _cacheService;
        private readonly RuleValidationService _validationService;
        private readonly IntegrationWriteLock _writeLock;
        private readonly ILogger<QmProRuleService> _logger;

        public QmProRuleService(
            IRedirectRuleRepository repository,
            IRuleCacheService cacheService,
            RuleValidationService validationService,
            IntegrationWriteLock writeLock,
            ILogger<QmProRuleService> logger)
        {
            _repository = repository;
            _cacheService = cacheService;
            _validationService = validationService;
            _writeLock = writeLock;
            _logger = logger;
        }

        public async Task<QmProPublishResult> PublishAsync(
            QmProPublishRequest request,
            CancellationToken cancellationToken = default)
        {
            var documentId = request.DocumentId?.Trim();

            if (string.IsNullOrEmpty(documentId))
            {
                return new QmProPublishResult(QmProPublishStatus.ValidationFailed, Error: "Dokument-ID darf nicht leer sein");
            }

            if (string.IsNullOrWhiteSpace(request.OldUrl))
            {
                return new QmProPublishResult(QmProPublishStatus.ValidationFailed, Error: "Alte URL darf nicht leer sein");
            }

            if (string.IsNullOrWhiteSpace(request.PublicationUrl))
            {
                return new QmProPublishResult(QmProPublishStatus.ValidationFailed, Error: "Publikations-URL darf nicht leer sein");
            }

            if (!TryBuildMatcher(request.OldUrl, out var matcher))
            {
                return new QmProPublishResult(QmProPublishStatus.ValidationFailed, Error: "Alte URL muss ein Pfad (beginnend mit /) oder eine gültige absolute URL sein (http:// oder https://)");
            }

            if (!TryParseAbsoluteHttpUrl(request.PublicationUrl, out _))
            {
                return new QmProPublishResult(QmProPublishStatus.ValidationFailed, Error: "Publikations-URL muss eine gültige absolute URL sein (http:// oder https://)");
            }

            var targetUrl = RuleImportExportService.PercentEncodePreservingSlashes(request.PublicationUrl.Trim());

            var validationErrors = _validationService.ValidateCreate(new CreateRuleRequest
            {
                Matcher = matcher,
                TargetUrl = targetUrl,
                RedirectType = "wildcard"
            });

            if (validationErrors.Count > 0)
            {
                return new QmProPublishResult(QmProPublishStatus.ValidationFailed, Error: validationErrors[0]);
            }

            await _writeLock.WaitAsync(cancellationToken);

            try
            {
                var existingRule = GetQmProRule(documentId);

                if (existingRule != null
                    && existingRule.IsActive
                    && string.Equals(RuleImportExportService.CanonicalizeMatcher(existingRule.Matcher), matcher, StringComparison.Ordinal)
                    && string.Equals(existingRule.TargetUrl, targetUrl, StringComparison.Ordinal))
                {
                    return new QmProPublishResult(QmProPublishStatus.Unchanged, existingRule);
                }

                var equivalentRule = _cacheService.FindEquivalentMatcher(matcher, existingRule?.Id);

                if (equivalentRule != null)
                {
                    _logger.LogWarning(
                        "QM Pro publish for document {DocumentId} rejected: matcher '{Matcher}' is already used by rule {ConflictingRuleId}.",
                        documentId,
                        matcher,
                        equivalentRule.Id);

                    return new QmProPublishResult(
                        QmProPublishStatus.MatcherDuplicate,
                        Error: "Die alte URL ist bereits Matcher einer anderen Regel",
                        ConflictingRule: equivalentRule);
                }

                var overlappingRule = _cacheService.FindOverlappingMatcher(matcher, RedirectType.Wildcard, existingRule?.Id);

                if (overlappingRule != null)
                {
                    _logger.LogWarning(
                        "QM Pro publish for document {DocumentId} rejected: matcher '{Matcher}' overlaps rule {ConflictingRuleId}.",
                        documentId,
                        matcher,
                        overlappingRule.Id);

                    return new QmProPublishResult(
                        QmProPublishStatus.MatcherConflict,
                        Error: "Die alte URL überschneidet sich mit einer bestehenden Regel",
                        ConflictingRule: overlappingRule);
                }

                if (existingRule == null)
                {
                    var newRule = new RedirectRule
                    {
                        Id = Guid.NewGuid(),
                        Matcher = matcher,
                        TargetUrl = targetUrl,
                        RedirectType = RedirectType.Wildcard,
                        Source = RuleSource.QmPro,
                        AutoRedirect = false,
                        ExternalDocumentId = documentId,
                        IsActive = true,
                        CreatedAt = DateTimeOffset.UtcNow
                    };

                    await _repository.CreateAsync(newRule, CancellationToken.None);

                    _cacheService.AddRule(newRule);

                    _logger.LogInformation(
                        "QM Pro created rule {RuleId} for document {DocumentId} with matcher '{Matcher}'.",
                        newRule.Id,
                        documentId,
                        matcher);

                    return new QmProPublishResult(QmProPublishStatus.Created, newRule);
                }

                var updatedRule = CopyRule(existingRule);
                updatedRule.Matcher = matcher;
                updatedRule.TargetUrl = targetUrl;
                updatedRule.IsActive = true;

                await PersistUpdateAsync(updatedRule);

                _logger.LogInformation(
                    "QM Pro updated rule {RuleId} for document {DocumentId} with matcher '{Matcher}' (reactivated: {Reactivated}).",
                    updatedRule.Id,
                    documentId,
                    matcher,
                    !existingRule.IsActive);

                return new QmProPublishResult(QmProPublishStatus.Updated, updatedRule);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<QmProUnpublishResult> UnpublishAsync(
            QmProUnpublishRequest request,
            CancellationToken cancellationToken = default)
        {
            var documentId = request.DocumentId?.Trim();

            if (string.IsNullOrEmpty(documentId))
            {
                return new QmProUnpublishResult(QmProUnpublishStatus.ValidationFailed, Error: "Dokument-ID darf nicht leer sein");
            }

            await _writeLock.WaitAsync(cancellationToken);

            try
            {
                var existingRule = GetQmProRule(documentId);

                if (existingRule == null)
                {
                    _logger.LogInformation("QM Pro unpublish for document {DocumentId}: no rule found.", documentId);

                    return new QmProUnpublishResult(QmProUnpublishStatus.NotFound);
                }

                if (!existingRule.IsActive)
                {
                    return new QmProUnpublishResult(QmProUnpublishStatus.AlreadyInactive, existingRule);
                }

                var deactivatedRule = CopyRule(existingRule);
                deactivatedRule.IsActive = false;

                await PersistUpdateAsync(deactivatedRule);

                _logger.LogInformation(
                    "QM Pro deactivated rule {RuleId} for document {DocumentId}.",
                    deactivatedRule.Id,
                    documentId);

                return new QmProUnpublishResult(QmProUnpublishStatus.Deactivated, deactivatedRule);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public static string BuildMatcher(Uri oldUri)
        {
            return RuleImportExportService.CanonicalizeMatcher(oldUri.PathAndQuery);
        }

        public static bool TryBuildMatcher(string oldUrl, [NotNullWhen(true)] out string? matcher)
        {
            var value = oldUrl.Trim();

            if (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal))
            {
                var fragmentIndex = value.IndexOf('#');
                var pathAndQuery = fragmentIndex >= 0 ? value[..fragmentIndex] : value;

                matcher = RuleImportExportService.CanonicalizeMatcher(pathAndQuery);

                return true;
            }

            if (TryParseAbsoluteHttpUrl(value, out var oldUri))
            {
                matcher = BuildMatcher(oldUri);

                return true;
            }

            matcher = null;

            return false;
        }

        private RedirectRule? GetQmProRule(string documentId)
        {
            var rule = _cacheService.GetByExternalDocumentId(documentId);

            return rule?.Source == RuleSource.QmPro ? rule : null;
        }

        private async Task PersistUpdateAsync(RedirectRule rule)
        {
            var persisted = await _repository.UpdateAsync(rule, CancellationToken.None);

            if (!persisted)
            {
                throw new InvalidOperationException($"Rule {rule.Id} could not be written to the backing store.");
            }

            _cacheService.UpdateRule(rule);
        }

        public static bool TryParseAbsoluteHttpUrl(string value, [NotNullWhen(true)] out Uri? uri)
        {
            if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
                && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrEmpty(parsed.Host))
            {
                uri = parsed;

                return true;
            }

            uri = null;

            return false;
        }

        private static RedirectRule CopyRule(RedirectRule rule)
        {
            return new RedirectRule
            {
                Id = rule.Id,
                Matcher = rule.Matcher,
                TargetUrl = rule.TargetUrl,
                RedirectType = rule.RedirectType,
                Source = rule.Source,
                InfoText = rule.InfoText,
                AutoRedirect = rule.AutoRedirect,
                DiscardQueryParams = rule.DiscardQueryParams,
                ForwardQueryParams = rule.ForwardQueryParams,
                KeptQueryParams = rule.KeptQueryParams,
                StaticQueryParams = rule.StaticQueryParams,
                SearchAndReplace = rule.SearchAndReplace,
                CreatedAt = rule.CreatedAt,
                ExternalDocumentId = rule.ExternalDocumentId,
                IsActive = rule.IsActive
            };
        }
    }
}
