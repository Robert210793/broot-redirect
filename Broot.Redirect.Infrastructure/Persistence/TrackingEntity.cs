using Azure;
using Azure.Data.Tables;
using Broot.Redirect.Core.Models;
using System;

namespace Broot.Redirect.Infrastructure.Persistence
{
    /// <summary>
    /// Azure Table Storage entity for TrackingEntry.
    /// PartitionKey = date string (yyyy-MM-dd) for efficient range queries.
    /// RowKey = Id (GUID without hyphens).
    /// </summary>
    public class TrackingEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;

        public string RowKey { get; set; } = string.Empty;

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }

        public string OldUrl { get; set; } = string.Empty;

        public string NewUrl { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public DateTimeOffset EntryTimestamp { get; set; }

        public string? UserAgent { get; set; }

        public string? Referrer { get; set; }

        public string? RuleId { get; set; }

        public int MatchQuality { get; set; }

        public string? Feedback { get; set; }

        public string? UserProposedUrl { get; set; }

        public string? RedirectStrategy { get; set; }

        /// <summary>
        /// Derives the date partition key from a DateTimeOffset.
        /// </summary>
        public static string ToDatePartition(DateTimeOffset timestamp)
        {
            return timestamp.UtcDateTime.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// Maps a domain TrackingEntry to a Table Storage entity.
        /// </summary>
        public static TrackingEntity FromDomainModel(TrackingEntry entry)
        {
            return new TrackingEntity
            {
                PartitionKey = ToDatePartition(entry.Timestamp),
                RowKey = entry.Id.ToString("N"),
                OldUrl = entry.OldUrl,
                NewUrl = entry.NewUrl,
                Path = entry.Path,
                EntryTimestamp = entry.Timestamp,
                UserAgent = entry.UserAgent,
                Referrer = entry.Referrer,
                RuleId = entry.RuleId,
                MatchQuality = entry.MatchQuality,
                Feedback = entry.Feedback,
                UserProposedUrl = entry.UserProposedUrl,
                RedirectStrategy = entry.RedirectStrategy
            };
        }

        /// <summary>
        /// Maps a Table Storage entity back to a domain TrackingEntry.
        /// </summary>
        public TrackingEntry ToDomainModel()
        {
            return new TrackingEntry
            {
                Id = Guid.ParseExact(RowKey, "N"),
                OldUrl = OldUrl,
                NewUrl = NewUrl,
                Path = Path,
                Timestamp = EntryTimestamp,
                UserAgent = UserAgent,
                Referrer = Referrer,
                RuleId = RuleId,
                MatchQuality = MatchQuality,
                Feedback = Feedback,
                UserProposedUrl = UserProposedUrl,
                RedirectStrategy = RedirectStrategy
            };
        }
    }
}