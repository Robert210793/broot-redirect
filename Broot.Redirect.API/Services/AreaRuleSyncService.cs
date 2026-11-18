using System.Diagnostics.CodeAnalysis;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Broot.Redirect.Core.Services;

namespace Broot.Redirect.API.Services
{
    public sealed class AreaRuleSyncService
    {
        public const string ValidationErrorCode = "VALIDATION_ERROR";
        public const string DuplicateEntryCode = "DUPLICATE_ENTRY";
        public const string MatcherDuplicateCode = "MATCHER_DUPLICATE";

        private readonly IRedirectRuleRepository _repository;
        private readonly IRuleCacheService _cacheService;
        private readonly RuleValidationService _validationService;
        private readonly IntegrationWriteLock _writeLock;
        private readonly ILogger<AreaRuleSyncService> _logger;

        public AreaRuleSyncService(
            IRedirectRuleRepository repository,
            IRuleCacheService cacheService,
            RuleValidationService validationService,
            IntegrationWriteLock writeLock,
            ILogger<AreaRuleSyncService> logger)
        {
            _repository = repository;
            _cacheService = cacheService;
            _validationService = validationService;
            _writeLock = writeLock;
            _logger = logger;
        }

        public async Task<AreaRuleSyncResponse> SyncAsync(
            IReadOnlyList<AreaRuleSyncEntry?> entries,
            CancellationToken cancellationToken = default)
        {
            await _writeLock.WaitAsync(cancellationToken);

            try
            {
                return await SyncLockedAsync(entries);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task<AreaRuleSyncResponse> SyncLockedAsync(IReadOnlyList<AreaRuleSyncEntry?> entries)
        {
            var response = new AreaRuleSyncResponse();
            var skipped = new List<(int Index, AreaRuleSyncSkippedEntry Entry)>();
            var submittedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<AreaRuleCandidate>();
            var invalidInactiveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var id = NormalizeId(entry?.Id);

                if (id == null)
                {
                    skipped.Add((index, Skip(entry?.Id, ValidationErrorCode, "Identifier darf nicht leer sein")));

                    continue;
                }

                if (!submittedIds.Add(id))
                {
                    skipped.Add((index, Skip(id, ValidationErrorCode, "Identifier wurde mehrfach übermittelt")));

                    continue;
                }

                if (!TryBuildCandidate(index, id, entry!, out var candidate, out var error))
                {
                    skipped.Add((index, Skip(id, ValidationErrorCode, error)));

                    if (!IsActive(entry!))
                    {
                        invalidInactiveIds.Add(id);
                    }

                    continue;
                }

                candidates.Add(candidate);
            }

            var removedRuleIds = new HashSet<Guid>();
            var existingById = new Dictionary<string, RedirectRule>(StringComparer.OrdinalIgnoreCase);

            var existingAreaRules = _cacheService.GetAll()
                .Where(rule => rule.Source == RuleSource.AreaList)
                .OrderBy(rule => rule.CreatedAt)
                .ThenBy(rule => rule.Id);

            foreach (var rule in existingAreaRules)
            {
                var id = NormalizeId(rule.ExternalDocumentId);

                if (id == null || !submittedIds.Contains(id) || existingById.ContainsKey(id))
                {
                    removedRuleIds.Add(rule.Id);

                    continue;
                }

                existingById[id] = rule;
            }

            var upsertedRules = new List<RedirectRule>();

            void Deactivate(RedirectRule rule)
            {
                upsertedRules.Add(Copy(rule, rule.Matcher, rule.RedirectType, rule.TargetUrl, isActive: false));

                response.Updated++;
                response.Deactivated++;
            }

            foreach (var group in candidates.GroupBy(candidate => MatcherEquivalence.GetKey(candidate.Matcher)))
            {
                var winner = group
                    .OrderByDescending(candidate => candidate.IsActive)
                    .ThenByDescending(candidate => HasRuleOnMatcher(existingById, candidate))
                    .ThenBy(candidate => candidate.Index)
                    .First();

                foreach (var duplicate in group.Where(candidate => !ReferenceEquals(candidate, winner)))
                {
                    skipped.Add((duplicate.Index, Skip(
                        duplicate.Id,
                        DuplicateEntryCode,
                        $"Die Quell-URL ist gleichwertig mit der Quell-URL des Eintrags {winner.Id}",
                        existingById.GetValueOrDefault(winner.Id)?.Id)));

                    if (existingById.TryGetValue(duplicate.Id, out var duplicateRule)
                        && duplicateRule.IsActive
                        && (!duplicate.IsActive || MatcherEquivalence.AreEquivalent(duplicateRule.Matcher, winner.Matcher)))
                    {
                        Deactivate(duplicateRule);
                    }
                }

                existingById.TryGetValue(winner.Id, out var existingRule);

                var conflictingRule = _cacheService.FindEquivalentMatcher(winner.Matcher, excludedSource: RuleSource.AreaList);

                if (conflictingRule != null)
                {
                    skipped.Add((winner.Index, Skip(
                        winner.Id,
                        MatcherDuplicateCode,
                        "Die Quell-URL ist bereits Matcher einer Regel anderer Herkunft",
                        conflictingRule.Id)));

                    if (existingRule is { IsActive: true } && !winner.IsActive)
                    {
                        Deactivate(existingRule);
                    }

                    continue;
                }

                if (existingRule == null)
                {
                    upsertedRules.Add(new RedirectRule
                    {
                        Id = Guid.NewGuid(),
                        Matcher = winner.Matcher,
                        TargetUrl = winner.TargetUrl,
                        RedirectType = RedirectType.Wildcard,
                        Source = RuleSource.AreaList,
                        AutoRedirect = false,
                        ExternalDocumentId = winner.Id,
                        IsActive = winner.IsActive,
                        CreatedAt = DateTimeOffset.UtcNow
                    });

                    response.Created++;

                    continue;
                }

                if (IsUnchanged(existingRule, winner))
                {
                    response.Unchanged++;

                    continue;
                }

                upsertedRules.Add(Copy(existingRule, winner.Matcher, RedirectType.Wildcard, winner.TargetUrl, winner.IsActive));

                response.Updated++;

                if (existingRule.IsActive != winner.IsActive)
                {
                    if (winner.IsActive)
                    {
                        response.Activated++;
                    }
                    else
                    {
                        response.Deactivated++;
                    }
                }
            }

            foreach (var id in invalidInactiveIds)
            {
                if (existingById.TryGetValue(id, out var rule) && rule.IsActive)
                {
                    Deactivate(rule);
                }
            }

            response.Deleted = removedRuleIds.Count;
            response.Skipped = skipped
                .OrderBy(item => item.Index)
                .Select(item => item.Entry)
                .ToList();

            await PersistAsync(upsertedRules, removedRuleIds);

            foreach (var entry in response.Skipped)
            {
                _logger.LogWarning(
                    "Area list sync skipped entry {EntryId}: {Code} {Error} (conflicting rule {ConflictingRuleId}).",
                    entry.Id,
                    entry.Code,
                    entry.Error,
                    entry.ConflictingRuleId);
            }

            _logger.LogInformation(
                "Area list sync of {EntryCount} entries: {Created} created, {Updated} updated ({Activated} activated, {Deactivated} deactivated), {Deleted} deleted, {Unchanged} unchanged, {Skipped} skipped.",
                entries.Count,
                response.Created,
                response.Updated,
                response.Activated,
                response.Deactivated,
                response.Deleted,
                response.Unchanged,
                response.Skipped.Count);

            return response;
        }

        private async Task PersistAsync(List<RedirectRule> upsertedRules, HashSet<Guid> removedRuleIds)
        {
            if (upsertedRules.Count == 0 && removedRuleIds.Count == 0)
            {
                return;
            }

            try
            {
                if (removedRuleIds.Count > 0)
                {
                    await _repository.BulkDeleteAsync(removedRuleIds.ToList(), CancellationToken.None);
                }

                if (upsertedRules.Count > 0)
                {
                    await _repository.BulkUpsertAsync(upsertedRules, CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Area list sync could not write to the backing store. Reloading the rule cache from storage.");

                await ReloadCacheAsync();

                throw;
            }

            _cacheService.ApplyChanges(upsertedRules, removedRuleIds);
        }

        private async Task ReloadCacheAsync()
        {
            try
            {
                var rules = await _repository.GetAllAsync(CancellationToken.None);

                _cacheService.ReplaceAll(rules);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Reloading the rule cache after a failed area list sync failed.");
            }
        }

        private bool TryBuildCandidate(
            int index,
            string id,
            AreaRuleSyncEntry entry,
            [NotNullWhen(true)] out AreaRuleCandidate? candidate,
            [NotNullWhen(false)] out string? error)
        {
            candidate = null;

            if (string.IsNullOrWhiteSpace(entry.SourceUrl))
            {
                error = "Quell-URL darf nicht leer sein";

                return false;
            }

            if (!TryGetPathAndQuery(entry.SourceUrl, out var pathAndQuery))
            {
                error = "Quell-URL muss eine gültige absolute URL sein (http:// oder https://)";

                return false;
            }

            var path = pathAndQuery.Split('?', 2)[0];

            if (path.Trim('/').Length == 0)
            {
                error = "Quell-URL muss einen Pfad enthalten";

                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.TargetUrl))
            {
                error = "Ziel-URL darf nicht leer sein";

                return false;
            }

            if (!QmProRuleService.TryParseAbsoluteHttpUrl(entry.TargetUrl, out _))
            {
                error = "Ziel-URL muss eine gültige absolute URL sein (http:// oder https://)";

                return false;
            }

            var matcher = RuleImportExportService.CanonicalizeMatcher(pathAndQuery);
            var targetUrl = RuleImportExportService.PercentEncodePreservingSlashes(entry.TargetUrl.Trim());

            var validationErrors = _validationService.ValidateCreate(new CreateRuleRequest
            {
                Matcher = matcher,
                TargetUrl = targetUrl,
                RedirectType = RedirectType.Wildcard.ToString().ToLowerInvariant()
            });

            if (validationErrors.Count > 0)
            {
                error = validationErrors[0];

                return false;
            }

            candidate = new AreaRuleCandidate(index, id, matcher, targetUrl, IsActive(entry));
            error = null;

            return true;
        }

        private static bool TryGetPathAndQuery(string sourceUrl, [NotNullWhen(true)] out string? pathAndQuery)
        {
            var value = sourceUrl.Trim();

            if (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal))
            {
                var fragmentIndex = value.IndexOf('#');

                pathAndQuery = fragmentIndex >= 0 ? value[..fragmentIndex] : value;

                return true;
            }

            if (QmProRuleService.TryParseAbsoluteHttpUrl(value, out var uri))
            {
                pathAndQuery = uri.PathAndQuery;

                return true;
            }

            pathAndQuery = null;

            return false;
        }

        private static bool IsActive(AreaRuleSyncEntry entry)
        {
            return entry.Active ?? true;
        }

        private static bool HasRuleOnMatcher(Dictionary<string, RedirectRule> existingById, AreaRuleCandidate candidate)
        {
            return existingById.TryGetValue(candidate.Id, out var rule)
                && MatcherEquivalence.AreEquivalent(rule.Matcher, candidate.Matcher);
        }

        private static bool IsUnchanged(RedirectRule rule, AreaRuleCandidate candidate)
        {
            return string.Equals(RuleImportExportService.CanonicalizeMatcher(rule.Matcher), candidate.Matcher, StringComparison.Ordinal)
                && rule.RedirectType == RedirectType.Wildcard
                && string.Equals(rule.TargetUrl, candidate.TargetUrl, StringComparison.Ordinal)
                && rule.IsActive == candidate.IsActive;
        }

        private static RedirectRule Copy(
            RedirectRule rule,
            string matcher,
            RedirectType redirectType,
            string targetUrl,
            bool isActive)
        {
            return new RedirectRule
            {
                Id = rule.Id,
                Matcher = matcher,
                TargetUrl = targetUrl,
                RedirectType = redirectType,
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
                IsActive = isActive
            };
        }

        private static string? NormalizeId(string? id)
        {
            var value = id?.Trim();

            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            return Guid.TryParse(value, out var guid) ? guid.ToString("D") : value;
        }

        private static AreaRuleSyncSkippedEntry Skip(string? id, string code, string error, Guid? conflictingRuleId = null)
        {
            return new AreaRuleSyncSkippedEntry
            {
                Id = id,
                Code = code,
                Error = error,
                ConflictingRuleId = conflictingRuleId
            };
        }

        private sealed record AreaRuleCandidate(
            int Index,
            string Id,
            string Matcher,
            string TargetUrl,
            bool IsActive);
    }
}
