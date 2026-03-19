using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Interfaces
{
    /// <summary>
    /// In-memory cache for global search-and-replace rules.
    /// All read operations hit the cache; write operations go through Table Storage
    /// first, then update the cache.
    /// Rules are always returned ordered by Priority ascending (lower = applied first).
    /// </summary>
    /// 
    public interface IGlobalRuleCacheService
    {
        /// <summary>
        /// Whether the cache has been populated from the backing store.
        /// </summary>
        /// 
        bool IsWarmedUp { get; }

        /// <summary>
        /// Total number of global rules currently in cache.
        /// </summary>
        /// 
        int RuleCount { get; }

        /// <summary>
        /// Returns all global rules ordered by Priority ascending.
        /// </summary>
        /// 
        IReadOnlyList<GlobalRule> GetAll();

        /// <summary>
        /// Gets a single global rule by ID. Returns null if not found.
        /// </summary>
        /// 
        GlobalRule? GetById(Guid id);

        /// <summary>
        /// Populates the cache from a complete set of rules.
        /// Called once on startup by the warmup service.
        /// </summary>
        /// 
        void Initialize(IReadOnlyList<GlobalRule> rules);

        /// <summary>
        /// Adds a single global rule to the cache and re-sorts by Priority.
        /// </summary>
        /// 
        void AddRule(GlobalRule rule);

        /// <summary>
        /// Updates an existing global rule in the cache and re-sorts by Priority.
        /// </summary>
        /// 
        void UpdateRule(GlobalRule rule);

        /// <summary>
        /// Removes a single global rule from the cache.
        /// </summary>
        /// 
        void RemoveRule(Guid ruleId);
    }
}
