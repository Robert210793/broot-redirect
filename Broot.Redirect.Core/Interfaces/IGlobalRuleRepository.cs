using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Interfaces
{
    /// <summary>
    /// Repository interface for GlobalRule persistence.
    /// All methods operate against the backing store (Azure Table Storage).
    /// The cache layer sits above this and delegates writes through here.
    /// </summary>
    /// 
    public interface IGlobalRuleRepository
    {
        /// <summary>
        /// Loads all global rules from the backing store.
        /// Used on startup to populate the in-memory cache.
        /// </summary>
        /// 
        Task<List<GlobalRule>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a single global rule by ID. Returns null if not found.
        /// </summary>
        /// 
        Task<GlobalRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new global rule. The rule must have a valid Id set before calling.
        /// </summary>
        /// 
        Task CreateAsync(GlobalRule rule, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates an existing global rule. Uses upsert semantics (replace mode).
        /// </summary>
        /// 
        Task UpdateAsync(GlobalRule rule, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a global rule by ID. Returns false if the rule does not exist.
        /// </summary>
        /// 
        Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
