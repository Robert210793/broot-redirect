using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Broot.Redirect.Infrastructure.Cache
{
    public class CacheOptions
    {
        public bool CaseSensitiveMatching { get; set; } = false;

        public string TrailingSlashPolicy { get; set; } = "ignore";

        public TimeSpan RegexMatchTimeout { get; set; } = TimeSpan.FromSeconds(1);
    }

    public class RuleCacheService : IRuleCacheService
    {
        private readonly ILogger<RuleCacheService> _logger;
        private readonly CacheOptions _options;

        private readonly ConcurrentDictionary<Guid, RedirectRule> _rulesById = new();

        private Dictionary<string, RedirectRule> _wildcardIndex = new();

        private List<RedirectRule> _partialAndDomainRules = new();

        private List<(Regex CompiledRegex, RedirectRule Rule)> _regexRules = new();

        private Dictionary<string, Guid> _matcherToId = new();

        private readonly ReaderWriterLockSlim _indexLock = new();

        private volatile bool _isWarmedUp;

        public RuleCacheService(
            IOptions<CacheOptions> options,
            ILogger<RuleCacheService> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public bool IsWarmedUp => _isWarmedUp;

        public int RuleCount => _rulesById.Count;

        public IReadOnlyList<RedirectRule> GetAll()
        {
            return _rulesById.Values.ToList();
        }

        public RedirectRule? GetById(Guid id)
        {
            _rulesById.TryGetValue(id, out var rule);

            return rule;
        }

        public RedirectRule? LookupWildcard(string normalizedPath)
        {
            _indexLock.EnterReadLock();

            try
            {
                _wildcardIndex.TryGetValue(normalizedPath, out var rule);

                return rule;
            }
            finally
            {
                _indexLock.ExitReadLock();
            }
        }

        public IReadOnlyList<RedirectRule> GetPartialAndDomainRules()
        {
            _indexLock.EnterReadLock();

            try
            {
                return _partialAndDomainRules;
            }
            finally
            {
                _indexLock.ExitReadLock();
            }
        }

        public IReadOnlyList<(Regex CompiledRegex, RedirectRule Rule)> GetRegexRules()
        {
            _indexLock.EnterReadLock();

            try
            {
                return _regexRules;
            }
            finally
            {
                _indexLock.ExitReadLock();
            }
        }

        public bool MatcherExists(string matcher, Guid? excludeRuleId = null)
        {
            _indexLock.EnterReadLock();

            try
            {
                if (!_matcherToId.TryGetValue(matcher, out var existingId))
                {
                    return false;
                }

                if (excludeRuleId.HasValue && existingId == excludeRuleId.Value)
                {
                    return false;
                }

                return true;
            }
            finally
            {
                _indexLock.ExitReadLock();
            }
        }

        public RedirectRule? FindOverlappingMatcher(string matcher, Guid? excludeRuleId = null)
        {
            var inputSegments = NormalizeAndSplitSegments(matcher);

            if (inputSegments.Length == 0)
            {
                return null;
            }

            _indexLock.EnterReadLock();

            try
            {
                foreach (var rule in _rulesById.Values)
                {
                    if (rule.RedirectType == RedirectType.Regex)
                    {
                        continue;
                    }

                    if (excludeRuleId.HasValue && rule.Id == excludeRuleId.Value)
                    {
                        continue;
                    }

                    var existingSegments = NormalizeAndSplitSegments(rule.Matcher);

                    if (existingSegments.Length == 0)
                    {
                        continue;
                    }

                    if (existingSegments.Length == inputSegments.Length)
                    {
                        continue;
                    }

                    var shorterLength = Math.Min(inputSegments.Length, existingSegments.Length);
                    var allSharedMatch = true;

                    for (var i = 0; i < shorterLength; i++)
                    {
                        if (!string.Equals(inputSegments[i], existingSegments[i], StringComparison.OrdinalIgnoreCase))
                        {
                            allSharedMatch = false;

                            break;
                        }
                    }

                    if (allSharedMatch)
                    {
                        return rule;
                    }
                }

                return null;
            }
            finally
            {
                _indexLock.ExitReadLock();
            }
        }

        public void Initialize(IReadOnlyList<RedirectRule> rules)
        {
            _rulesById.Clear();

            foreach (var rule in rules)
            {
                _rulesById[rule.Id] = rule;
            }

            RebuildIndexes();

            _isWarmedUp = true;

            _logger.LogInformation(
                "Cache initialized with {RuleCount} rules ({WildcardCount} wildcard, {PartialDomainCount} partial/domain, {RegexCount} regex).",
                rules.Count,
                _wildcardIndex.Count,
                _partialAndDomainRules.Count,
                _regexRules.Count);
        }

        public void AddRule(RedirectRule rule)
        {
            _rulesById[rule.Id] = rule;

            RebuildIndexes();

            _logger.LogDebug("Added rule {RuleId} to cache.", rule.Id);
        }

        public void UpdateRule(RedirectRule rule)
        {
            _rulesById[rule.Id] = rule;

            RebuildIndexes();

            _logger.LogDebug("Updated rule {RuleId} in cache.", rule.Id);
        }

        public void RemoveRule(Guid ruleId)
        {
            _rulesById.TryRemove(ruleId, out _);

            RebuildIndexes();

            _logger.LogDebug("Removed rule {RuleId} from cache.", ruleId);
        }

        public void RemoveRules(IReadOnlySet<Guid> ruleIds)
        {
            foreach (var ruleId in ruleIds)
            {
                _rulesById.TryRemove(ruleId, out _);
            }

            RebuildIndexes();

            _logger.LogDebug("Removed {Count} rules from cache.", ruleIds.Count);
        }

        public void ReplaceAll(IReadOnlyList<RedirectRule> rules)
        {
            _rulesById.Clear();

            foreach (var rule in rules)
            {
                _rulesById[rule.Id] = rule;
            }

            RebuildIndexes();

            _logger.LogInformation("Cache replaced with {RuleCount} rules.", rules.Count);
        }

        private void RebuildIndexes()
        {
            var wildcardIndex = new Dictionary<string, RedirectRule>(
                _options.CaseSensitiveMatching ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

            var partialAndDomainRules = new List<RedirectRule>();

            var regexRules = new List<(Regex, RedirectRule)>();

            var matcherToId = new Dictionary<string, Guid>(StringComparer.Ordinal);

            foreach (var rule in _rulesById.Values)
            {
                matcherToId[rule.Matcher] = rule.Id;

                switch (rule.RedirectType)
                {
                    case Core.Models.RedirectType.Wildcard:
                        {
                            var normalizedKey = NormalizeMatcher(rule.Matcher);

                            wildcardIndex[normalizedKey] = rule;

                            break;
                        }

                    case Core.Models.RedirectType.Partial:
                    case Core.Models.RedirectType.Domain:
                        {
                            partialAndDomainRules.Add(rule);

                            break;
                        }

                    case Core.Models.RedirectType.Regex:
                        {
                            try
                            {
                                var compiledRegex = new Regex(
                                    rule.Matcher,
                                    RegexOptions.Compiled,
                                    _options.RegexMatchTimeout);

                                regexRules.Add((compiledRegex, rule));
                            }
                            catch (ArgumentException exception)
                            {
                                _logger.LogWarning(
                                    exception,
                                    "Rule {RuleId} has invalid regex matcher '{Matcher}'. Skipping.",
                                    rule.Id,
                                    rule.Matcher);
                            }

                            break;
                        }
                }
            }

            partialAndDomainRules.Sort((a, b) => b.Matcher.Length.CompareTo(a.Matcher.Length));

            _indexLock.EnterWriteLock();

            try
            {
                _wildcardIndex = wildcardIndex;
                _partialAndDomainRules = partialAndDomainRules;
                _regexRules = regexRules;
                _matcherToId = matcherToId;
            }
            finally
            {
                _indexLock.ExitWriteLock();
            }
        }

        private string NormalizeMatcher(string matcher)
        {
            var normalized = matcher;

            try { normalized = Uri.UnescapeDataString(normalized); }
            catch { /* keep original if decoding fails */ }

            if (string.Equals(_options.TrailingSlashPolicy, "ignore", StringComparison.OrdinalIgnoreCase))
            {
                if (normalized.Length > 1 && normalized.EndsWith('/'))
                {
                    normalized = normalized.TrimEnd('/');
                }
            }

            if (!_options.CaseSensitiveMatching)
            {
                normalized = normalized.ToLowerInvariant();
            }

            return normalized;
        }

        private static string[] NormalizeAndSplitSegments(string matcher)
        {
            var normalized = matcher.Trim().ToLowerInvariant().TrimEnd('/');

            return normalized
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
        }
    }
}