using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Broot.Redirect.Core.Services
{
    public sealed class RuleMatchingService : IRuleMatchingService
    {
        private readonly IRuleCacheService _cacheService;

        public RuleMatchingService(IRuleCacheService cacheService)
        {
            _cacheService = cacheService;
        }

        public RuleMatchResult? ResolveMatch(string requestUrl, RuleMatchingConfig config)
        {
            // Phase 1: Wildcard O(1) lookup
            var wildcardResult = TryWildcardLookup(requestUrl, config);
            if (wildcardResult != null)
            {
                return wildcardResult;
            }

            // Phase 2: Partial and domain rules (pre-sorted by matcher length)
            var partialAndDomainRules = _cacheService.GetPartialAndDomainRules();
            if (partialAndDomainRules.Count > 0)
            {
                var processedRules = partialAndDomainRules
                    .Select(rule => PreprocessRule(rule, config))
                    .ToList();

                var partialResult = FindMatchingRule(requestUrl, processedRules, config);
                if (partialResult != null)
                {
                    return partialResult;
                }
            }

            // Phase 3: Regex rules (pre-compiled)
            var regexResult = TryRegexMatch(requestUrl);
            return regexResult;
        }

        private RuleMatchResult? TryWildcardLookup(string requestUrl, RuleMatchingConfig config)
        {
            var parsed = ParseRequestUrl(requestUrl);
            var path = Uri.UnescapeDataString(parsed.AbsolutePath);

            if (config.TrailingSlashPolicy == TrailingSlashPolicy.Ignore && path.Length > 1 && path.EndsWith('/'))
            {
                path = path.TrimEnd('/');
            }

            if (!config.CaseSensitivePath)
            {
                path = path.ToLowerInvariant();
            }

            var rule = _cacheService.LookupWildcard(path);
            if (rule == null)
            {
                return null;
            }

            var requestQuery = NormalizeQuery(parsed.Query, config);
            var matcherParts = rule.Matcher.Split('?', 2);
            var ruleQuery = matcherParts.Length > 1
                ? NormalizeQuery("?" + matcherParts[1], config)
                : new Dictionary<string, List<string>>(StringComparer.Ordinal);

            if (!QueryMatches(ruleQuery, requestQuery, out var queryPairs))
            {
                return null;
            }

            var isExact = ruleQuery.Count == requestQuery.Count;
            var quality = isExact ? 100 : 75;

            return new RuleMatchResult
            {
                Rule = rule,
                Score = 1000 + (queryPairs * config.WeightQueryPair) + (isExact ? config.BonusExactMatch : 0),
                Quality = quality,
                Level = QualityToLevel(quality)
            };
        }

        private RuleMatchResult? TryRegexMatch(string requestUrl)
        {
            var regexRules = _cacheService.GetRegexRules();

            foreach (var (compiledRegex, rule) in regexRules)
            {
                try
                {
                    if (compiledRegex.IsMatch(requestUrl))
                    {
                        return new RuleMatchResult
                        {
                            Rule = rule,
                            Score = 500,
                            Quality = 100,
                            Level = MatchQualityLevel.Green
                        };
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                }
            }

            return null;
        }

        public ProcessedRule PreprocessRule(RedirectRule rule, RuleMatchingConfig config)
        {
            var matcherParts = rule.Matcher.Split('?', 2);
            var matcherPath = matcherParts[0];
            var matcherQuery = matcherParts.Length > 1 ? "?" + matcherParts[1] : "";

            var isDomainMatcher = !matcherPath.StartsWith('/');

            return new ProcessedRule
            {
                Rule = rule,
                NormalizedPath = isDomainMatcher
                    ? Array.Empty<string>()
                    : NormalizePath(matcherPath, config),
                NormalizedQuery = NormalizeQuery(matcherQuery, config),
                IsDomainMatcher = isDomainMatcher
            };
        }

        public RuleMatchResult? FindMatchingRule(
            string requestUrl,
            IReadOnlyList<ProcessedRule> rules,
            RuleMatchingConfig config)
        {
            var parsedRequest = ParseRequestUrl(requestUrl);

            var requestPath = NormalizePath(parsedRequest.AbsolutePath, config);
            var requestQuery = NormalizeQuery(parsedRequest.Query, config);
            var requestHostname = parsedRequest.Host.ToLowerInvariant();

            BestCandidate? best = null;

            for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                var processed = rules[ruleIndex];

                if (processed.IsDomainMatcher)
                {
                    best = EvaluateDomainCandidate(processed, requestHostname, requestQuery, config, best);
                }
                else
                {
                    best = EvaluatePathCandidate(processed, requestPath, requestQuery, config, best);
                }
            }

            if (best == null)
            {
                return null;
            }

            var quality = CalculateQuality(best, requestPath, requestQuery);
            var level = QualityToLevel(quality);

            return new RuleMatchResult
            {
                Rule = best.Rule,
                Score = best.Score,
                Quality = quality,
                Level = level
            };
        }

        private static BestCandidate? EvaluateDomainCandidate(
            ProcessedRule processed,
            string requestHostname,
            Dictionary<string, List<string>> requestQuery,
            RuleMatchingConfig config,
            BestCandidate? best)
        {
            var rule = processed.Rule;
            var ruleQuery = processed.NormalizedQuery;
            var matcherDomain = rule.Matcher.Split('?', 2)[0].ToLowerInvariant();

            if (requestHostname != matcherDomain)
            {
                return best;
            }

            if (!QueryMatches(ruleQuery, requestQuery, out var domainQueryPairs))
            {
                return best;
            }

            var domainScore = 1000 + (domainQueryPairs * config.WeightQueryPair);

            if (IsBetterCandidate(best, domainScore, 0, domainQueryPairs, 0, false, rule))
            {
                return new BestCandidate
                {
                    Rule = rule,
                    Score = domainScore,
                    StaticSegments = 0,
                    QueryPairs = domainQueryPairs,
                    Wildcards = 0,
                    Start = 0,
                    RuleQuery = ruleQuery,
                    HasPartialSegmentMatch = false
                };
            }

            return best;
        }

        private static BestCandidate? EvaluatePathCandidate(
            ProcessedRule processed,
            string[] requestPath,
            Dictionary<string, List<string>> requestQuery,
            RuleMatchingConfig config,
            BestCandidate? best)
        {
            var rule = processed.Rule;
            var rulePath = processed.NormalizedPath;
            var ruleQuery = processed.NormalizedQuery;

            if (rulePath.Length > requestPath.Length)
            {
                return best;
            }

            for (var start = 0; start <= requestPath.Length - rulePath.Length; start++)
            {
                if (!TryMatchSegments(rulePath, requestPath, start, out var staticMatches, out var wildcards, out var hasPartialSegmentMatch))
                {
                    continue;
                }

                if (!QueryMatches(ruleQuery, requestQuery, out var queryPairs))
                {
                    continue;
                }

                var isExact = start == 0
                    && rulePath.Length == requestPath.Length
                    && !hasPartialSegmentMatch
                    && ruleQuery.Count == requestQuery.Count;

                var score =
                    staticMatches * config.WeightPathSegment
                    + queryPairs * config.WeightQueryPair
                    + wildcards * config.PenaltyWildcard
                    + (isExact ? config.BonusExactMatch : 0);

                if (IsBetterCandidate(best, score, staticMatches, queryPairs, wildcards, isExact, rule))
                {
                    best = new BestCandidate
                    {
                        Rule = rule,
                        Score = score,
                        StaticSegments = staticMatches,
                        QueryPairs = queryPairs,
                        Wildcards = wildcards,
                        Start = start,
                        RuleQuery = ruleQuery,
                        HasPartialSegmentMatch = hasPartialSegmentMatch
                    };
                }
            }

            return best;
        }

        private static bool TryMatchSegments(
            string[] rulePath,
            string[] requestPath,
            int start,
            out int staticMatches,
            out int wildcards,
            out bool hasPartialSegmentMatch)
        {
            staticMatches = 0;
            wildcards = 0;
            hasPartialSegmentMatch = false;

            for (var segmentIndex = 0; segmentIndex < rulePath.Length; segmentIndex++)
            {
                var segment = rulePath[segmentIndex];
                var requestSegment = requestPath[start + segmentIndex];

                if (segment == "*" || segment.StartsWith(':'))
                {
                    wildcards++;
                    continue;
                }

                if (segment.EndsWith('*') && segment.Length > 1)
                {
                    var prefix = segment[..^1];

                    if (requestSegment.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        staticMatches++;
                        hasPartialSegmentMatch = true;
                        continue;
                    }
                }

                if (segment == requestSegment)
                {
                    staticMatches++;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        internal static string[] NormalizePath(string path, RuleMatchingConfig config)
        {
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                return Array.Empty<string>();
            }

            var normalizedInput = path.StartsWith('/') ? path : "/" + path;

            if (!Uri.TryCreate("http://example.com" + normalizedInput, UriKind.Absolute, out var uri))
            {
                return Array.Empty<string>();
            }

            var pathname = uri.AbsolutePath;

            if (config.TrailingSlashPolicy == TrailingSlashPolicy.Ignore)
            {
                pathname = pathname.TrimEnd('/');
            }

            pathname = Regex.Replace(pathname, "//{1,}", "/");

            var segments = pathname
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => Uri.UnescapeDataString(segment))
                .ToArray();

            if (!config.CaseSensitivePath)
            {
                for (var index = 0; index < segments.Length; index++)
                {
                    segments[index] = segments[index].ToLowerInvariant();
                }
            }

            return segments;
        }

        internal static Dictionary<string, List<string>> NormalizeQuery(
            string query,
            RuleMatchingConfig config)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            if (string.IsNullOrEmpty(query))
            {
                return map;
            }

            var queryString = query.StartsWith('?') ? query[1..] : query;
            var collection = HttpUtility.ParseQueryString(queryString);

            foreach (string? key in collection.AllKeys)
            {
                if (key == null)
                {
                    continue;
                }

                var normalizedKey = config.CaseSensitiveQuery ? key : key.ToLowerInvariant();
                var values = collection.GetValues(key);

                if (values == null)
                {
                    continue;
                }

                if (!map.TryGetValue(normalizedKey, out var list))
                {
                    list = new List<string>();
                    map[normalizedKey] = list;
                }

                foreach (var value in values)
                {
                    list.Add(value);
                }
            }

            foreach (var entry in map)
            {
                entry.Value.Sort(StringComparer.Ordinal);
            }

            return map;
        }

        private static bool QueryMatches(
            Dictionary<string, List<string>> ruleQuery,
            Dictionary<string, List<string>> requestQuery,
            out int matchedPairs)
        {
            matchedPairs = 0;

            foreach (var (key, ruleValues) in ruleQuery)
            {
                if (!requestQuery.TryGetValue(key, out var requestValues))
                {
                    return false;
                }

                foreach (var ruleValue in ruleValues)
                {
                    if (!requestValues.Contains(ruleValue))
                    {
                        return false;
                    }

                    matchedPairs++;
                }
            }

            return true;
        }

        private static bool IsBetterCandidate(
            BestCandidate? current,
            int score,
            int staticSegments,
            int queryPairs,
            int wildcards,
            bool isExact,
            RedirectRule rule)
        {
            if (current == null)
            {
                return true;
            }

            return CompareToCandidate(current, score, staticSegments, queryPairs, wildcards, isExact, rule) < 0;
        }

        private static int CompareToCandidate(
            BestCandidate current,
            int score,
            int staticSegments,
            int queryPairs,
            int wildcards,
            bool isExact,
            RedirectRule rule)
        {
            var cmp = current.Score.CompareTo(score);
            if (cmp != 0) return cmp;

            cmp = current.StaticSegments.CompareTo(staticSegments);
            if (cmp != 0) return cmp;

            cmp = current.QueryPairs.CompareTo(queryPairs);
            if (cmp != 0) return cmp;

            cmp = wildcards.CompareTo(current.Wildcards);
            if (cmp != 0) return cmp;

            return CompareMatcherLength(rule, current.Rule, isExact);
        }

        private static int CompareMatcherLength(RedirectRule candidate, RedirectRule current, bool isExact)
        {
            var candidateLen = candidate.Matcher.Length;
            var currentLen = current.Matcher.Length;

            if (!isExact && candidateLen > currentLen)
            {
                return -1;
            }

            if (candidateLen < currentLen)
            {
                return 1;
            }

            if (candidateLen == currentLen)
            {
                return CompareCreatedAtThenId(candidate, current);
            }

            return 1;
        }

        private static int CompareCreatedAtThenId(RedirectRule candidate, RedirectRule current)
        {
            var cmp = candidate.CreatedAt.CompareTo(current.CreatedAt);
            if (cmp != 0) return cmp;

            return string.CompareOrdinal(
                candidate.Id.ToString("N"),
                current.Id.ToString("N"));
        }

        private static int CalculateQuality(
            BestCandidate best,
            string[] requestPath,
            Dictionary<string, List<string>> requestQuery)
        {
            var isDomainRule = !best.Rule.Matcher.StartsWith('/');

            bool hasExtraQuery = false;

            foreach (var key in requestQuery.Keys)
            {
                if (!best.RuleQuery.ContainsKey(key))
                {
                    hasExtraQuery = true;
                    break;
                }
            }

            if (isDomainRule)
            {
                return hasExtraQuery ? 75 : 100;
            }

            var rulePathLength = best.StaticSegments + best.Wildcards;

            if (best.Start > 0 || rulePathLength < requestPath.Length || best.HasPartialSegmentMatch)
            {
                return 50;
            }

            if (hasExtraQuery)
            {
                return 75;
            }

            return 100;
        }

        private static MatchQualityLevel QualityToLevel(int quality)
        {
            if (quality < 60)
            {
                return MatchQualityLevel.Red;
            }

            if (quality < 90)
            {
                return MatchQualityLevel.Yellow;
            }

            return MatchQualityLevel.Green;
        }

        private static Uri ParseRequestUrl(string requestUrl)
        {
            if (requestUrl.StartsWith('/'))
            {
                return new Uri("http://example.com" + requestUrl);
            }

            if (requestUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || requestUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri(requestUrl);
            }

            return new Uri("http://" + requestUrl);
        }

        private sealed class BestCandidate
        {
            public required RedirectRule Rule { get; set; }

            public int Score { get; set; }

            public int StaticSegments { get; set; }

            public int QueryPairs { get; set; }

            public int Wildcards { get; set; }

            public int Start { get; set; }

            public Dictionary<string, List<string>> RuleQuery { get; set; } = new();

            public bool HasPartialSegmentMatch { get; set; }
        }
    }
}
