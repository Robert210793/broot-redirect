using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Interfaces
{
    /// <summary>
    /// Repository interface for TrackingEntry persistence.
    /// No cache layer is used -- tracking data is write-heavy and read-rarely (admin stats only).
    /// Partitioned by date (yyyy-MM-dd) for efficient range queries.
    /// </summary>
    public interface ITrackingRepository
    {
        /// <summary>
        /// Creates a new tracking entry. Returns the generated ID.
        /// </summary>
        Task<Guid> CreateAsync(TrackingEntry entry, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates an existing tracking entry (typically to add feedback).
        /// Returns false if not found.
        /// </summary>
        Task<bool> UpdateAsync(TrackingEntry entry, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a single tracking entry by ID.
        /// Requires the date partition key hint for efficient lookup.
        /// Returns null if not found.
        /// </summary>
        Task<TrackingEntry?> GetByIdAsync(Guid id, string datePartition, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a single tracking entry by ID, scanning across all date partitions.
        /// Slower than the date-hinted overload -- use when the date is unknown (e.g. feedback endpoint).
        /// Returns null if not found.
        /// </summary>
        Task<TrackingEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns paginated tracking entries across all date partitions.
        /// Ordered by timestamp descending (newest first).
        /// Supports an optional search filter (matches against OldUrl, NewUrl, Path, RuleId, Feedback).
        /// </summary>
        Task<(List<TrackingEntry> Entries, int TotalCount)> GetPagedAsync(
            int page,
            int limit,
            string? search = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns aggregated statistics across all tracking entries.
        /// </summary>
        Task<TrackingStats> GetStatsAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Aggregated tracking statistics returned by GetStatsAsync.
    /// </summary>
    public sealed class TrackingStats
    {
        public int TotalVisits { get; set; }

        public int MatchedVisits { get; set; }

        public int UnmatchedVisits { get; set; }

        public double MatchRate { get; set; }

        public int FeedbackOk { get; set; }

        public int FeedbackNok { get; set; }

        public int FeedbackAutoRedirect { get; set; }

        public int FeedbackNone { get; set; }

        /// <summary>
        /// Top 10 most-hit rule IDs with their visit counts.
        /// </summary>
        /// 
        public List<TopRuleStat> TopRules { get; set; } = new();
    }

    public sealed class TopRuleStat
    {
        public string RuleId { get; set; } = string.Empty;

        public int Count { get; set; }
    }
}
