using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Interfaces
{
    /// <summary>
    /// Repository interface for RedirectRule persistence.
    /// All methods operate against the backing store (Azure Table Storage).
    /// The cache layer sits above this and delegates writes through here.
    /// </summary>
    /// 
    public interface IRedirectRuleRepository
    {
        /// <summary>
        /// Loads all rules from the backing store.
        /// Used on startup to populate the in-memory cache.
        /// Returns rules in pages internally; callers receive the complete set.
        /// </summary>
        /// 
        Task<List<RedirectRule>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a single rule by ID. Returns null if not found.
        /// </summary>
        /// 
        Task<RedirectRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new rule. The rule must have a valid Id set before calling.
        /// </summary>
        /// 
        Task CreateAsync(RedirectRule rule, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates an existing rule. Uses upsert semantics (replace mode).
        /// Returns false if the rule does not exist.
        /// </summary>
        /// 
        Task<bool> UpdateAsync(RedirectRule rule, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a rule by ID. Returns false if the rule does not exist.
        /// </summary>
        /// 
        Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes multiple rules by ID. Returns the count of successfully deleted rules.
        /// </summary>
        /// 
        Task<int> BulkDeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes all rules from the backing store. Returns the count of deleted rules.
        /// </summary>
        /// 
        Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Upserts a batch of rules (used by JSON import).
        /// Rules with existing IDs are updated; new IDs are created.
        /// </summary>
        /// 
        Task BulkUpsertAsync(IReadOnlyList<RedirectRule> rules, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks connectivity to the backing store.
        /// Returns true if the table is accessible.
        /// </summary>
        /// 
        Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
    }
}