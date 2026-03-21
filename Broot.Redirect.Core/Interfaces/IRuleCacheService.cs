using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Interfaces
{
    /// <summary>
    /// In-memory cache for redirect rules with type-partitioned lookup structures.
    /// All read operations hit the cache; write operations go through Table Storage
    /// first, then update the cache.
    /// </summary>
    /// 
    public interface IRuleCacheService
    {
        /// <summary>
        /// Whether the cache has been populated from the backing store.
        /// False during startup before the warmup service completes.
        /// </summary>
        /// 
        bool IsWarmedUp { get; }

        /// <summary>
        /// Total number of rules currently in cache.
        /// </summary>
        /// 
        int RuleCount { get; }

        /// <summary>
        /// Gets all rules in the cache. Returns the full collection.
        /// Used by paginated list endpoints and export.
        /// </summary>
        /// 
        IReadOnlyList<RedirectRule> GetAll();

        /// <summary>
        /// Gets a single rule by ID. Returns null if not found.
        /// </summary>
        /// 
        RedirectRule? GetById(Guid id);

        /// <summary>
        /// Attempts an O(1) wildcard lookup by normalized path.
        /// Returns null if no wildcard rule matches.
        /// </summary>
        /// 
        RedirectRule? LookupWildcard(string normalizedPath);

        /// <summary>
        /// Returns partial and domain rules sorted by matcher length (longest first).
        /// Used by the matching engine for prefix scanning.
        /// </summary>
        /// 
        IReadOnlyList<RedirectRule> GetPartialAndDomainRules();

        /// <summary>
        /// Returns all regex rules with their pre-compiled Regex objects.
        /// Used by the matching engine for linear regex evaluation.
        /// </summary>
        /// 
        IReadOnlyList<(Regex CompiledRegex, RedirectRule Rule)> GetRegexRules();

        /// <summary>
        /// Checks if a matcher value already exists in the cache (for duplicate validation).
        /// Optionally excludes a specific rule ID (for update validation).
        /// </summary>
        /// 
        bool MatcherExists(string matcher, Guid? excludeRuleId = null);

        /// <summary>
        /// Finds an existing non-regex rule whose matcher overlaps hierarchically
        /// with the given matcher (one is a path-segment prefix of the other).
        /// Returns null if no overlap is found. Skips regex rules.
        /// </summary>
        RedirectRule? FindOverlappingMatcher(string matcher, Guid? excludeRuleId = null);

        /// <summary>
        /// Populates the cache from a complete set of rules.
        /// Called once on startup by the warmup service.
        /// </summary>
        /// 
        void Initialize(IReadOnlyList<RedirectRule> rules);

        /// <summary>
        /// Adds a single rule to the cache and updates all lookup indexes.
        /// </summary>
        /// 
        void AddRule(RedirectRule rule);

        /// <summary>
        /// Updates an existing rule in the cache.
        /// Removes old index entries and adds new ones.
        /// </summary>
        /// 
        void UpdateRule(RedirectRule rule);

        /// <summary>
        /// Removes a single rule from the cache and all lookup indexes.
        /// </summary>
        /// 
        void RemoveRule(Guid ruleId);

        /// <summary>
        /// Removes multiple rules from the cache.
        /// More efficient than calling RemoveRule in a loop (single index rebuild).
        /// </summary>
        /// 
        void RemoveRules(IReadOnlySet<Guid> ruleIds);

        /// <summary>
        /// Replaces the entire cache contents with a new set of rules.
        /// Used after bulk import to ensure consistency.
        /// </summary>
        /// 
        void ReplaceAll(IReadOnlyList<RedirectRule> rules);
    }
}