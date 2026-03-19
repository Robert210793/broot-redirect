using Azure;
using Azure.Data.Tables;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Broot.Redirect.Infrastructure.Persistence
{
    public class TableStorageTrackingRepository : ITrackingRepository
    {
        private readonly TableClient _tableClient;
        private readonly ILogger<TableStorageTrackingRepository> _logger;

        public TableStorageTrackingRepository(
            IOptions<TableStorageOptions> options,
            ILogger<TableStorageTrackingRepository> logger)
        {
            _logger = logger;

            var tableServiceClient = new TableServiceClient(options.Value.ConnectionString);

            _tableClient = tableServiceClient.GetTableClient("Tracking");
        }

        public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
        {
            await _tableClient.CreateIfNotExistsAsync(cancellationToken);

            _logger.LogInformation("Tracking table ensured.");
        }

        public async Task<Guid> CreateAsync(TrackingEntry entry, CancellationToken cancellationToken = default)
        {
            if (entry.Id == Guid.Empty)
            {
                entry.Id = Guid.NewGuid();
            }

            var entity = TrackingEntity.FromDomainModel(entry);

            await _tableClient.AddEntityAsync(entity, cancellationToken);

            _logger.LogDebug("Created tracking entry {EntryId} in partition {Partition}.", entry.Id, entity.PartitionKey);

            return entry.Id;
        }

        public async Task<bool> UpdateAsync(TrackingEntry entry, CancellationToken cancellationToken = default)
        {
            var entity = TrackingEntity.FromDomainModel(entry);

            try
            {
                await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);

                _logger.LogDebug("Updated tracking entry {EntryId}.", entry.Id);

                return true;
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                return false;
            }
        }

        public async Task<TrackingEntry?> GetByIdAsync(Guid id, string datePartition, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<TrackingEntity>(
                    datePartition,
                    id.ToString("N"),
                    cancellationToken: cancellationToken);

                return response.Value.ToDomainModel();
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                return null;
            }
        }

        public async Task<TrackingEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var rowKeyFilter = $"RowKey eq '{id:N}'";

            await foreach (var entity in _tableClient.QueryAsync<TrackingEntity>(
                filter: rowKeyFilter,
                maxPerPage: 1,
                cancellationToken: cancellationToken))
            {
                return entity.ToDomainModel();
            }

            return null;
        }

        public async Task<(List<TrackingEntry> Entries, int TotalCount)> GetPagedAsync(
            int page,
            int limit,
            string? search = null,
            int retentionDays = 30,
            int? qualityMin = null,
            int? qualityMax = null,
            string? feedbackType = null,
            string? ruleId = null,
            CancellationToken cancellationToken = default)
        {
            var allEntries = await LoadFilteredEntriesAsync(
                retentionDays,
                search,
                qualityMin,
                qualityMax,
                feedbackType,
                ruleId,
                cancellationToken);

            var totalCount = allEntries.Count;

            var paged = allEntries
                .OrderByDescending(entry => entry.Timestamp)
                .Skip((page - 1) * limit)
                .Take(limit)
                .ToList();

            return (paged, totalCount);
        }

        public async Task<List<TrackingEntry>> GetAllFilteredAsync(
            int retentionDays = 30,
            string? search = null,
            int? qualityMin = null,
            int? qualityMax = null,
            string? feedbackType = null,
            string? ruleId = null,
            CancellationToken cancellationToken = default)
        {
            var allEntries = await LoadFilteredEntriesAsync(
                retentionDays,
                search,
                qualityMin,
                qualityMax,
                feedbackType,
                ruleId,
                cancellationToken);

            return allEntries
                .OrderByDescending(entry => entry.Timestamp)
                .ToList();
        }

        public async Task<List<TrendDataPoint>> GetTrendAsync(
            int days = 30,
            string aggregation = "day",
            CancellationToken cancellationToken = default)
        {
            var allEntries = new List<TrackingEntry>();
            var filter = BuildDateRangeFilter(days);

            await foreach (var entity in _tableClient.QueryAsync<TrackingEntity>(
                filter: filter,
                cancellationToken: cancellationToken))
            {
                allEntries.Add(entity.ToDomainModel());
            }

            var grouped = allEntries
                .GroupBy(entry => GetTrendBucketKey(entry.Timestamp, aggregation))
                .ToDictionary(group => group.Key, group => group.ToList());

            // Build complete set of buckets so the chart has no gaps
            var buckets = GenerateTrendBuckets(days, aggregation);
            var result = new List<TrendDataPoint>();

            foreach (var bucketDate in buckets)
            {
                var bucketKey = GetTrendBucketKey(bucketDate, aggregation);

                if (grouped.TryGetValue(bucketKey, out var entries))
                {
                    result.Add(new TrendDataPoint
                    {
                        Date = bucketKey,
                        Ok = entries.Count(entry => entry.Feedback == "OK"),
                        Nok = entries.Count(entry => entry.Feedback == "NOK"),
                        AutoRedirect = entries.Count(entry => entry.Feedback == "auto-redirect"),
                        None = entries.Count(entry => string.IsNullOrEmpty(entry.Feedback)),
                        Total = entries.Count
                    });
                }
                else
                {
                    result.Add(new TrendDataPoint
                    {
                        Date = bucketKey,
                        Ok = 0,
                        Nok = 0,
                        AutoRedirect = 0,
                        None = 0,
                        Total = 0
                    });
                }
            }

            return result;
        }

        public async Task<TrackingStats> GetStatsAsync(int retentionDays = 30, string? timeRange = null, CancellationToken cancellationToken = default)
        {
            var allEntries = new List<TrackingEntry>();
            var filter = BuildDateRangeFilter(retentionDays, timeRange);

            await foreach (var entity in _tableClient.QueryAsync<TrackingEntity>(
                filter: filter,
                cancellationToken: cancellationToken))
            {
                allEntries.Add(entity.ToDomainModel());
            }

            var totalVisits = allEntries.Count;

            var matchedVisits = allEntries.Count(entry =>
                entry.MatchQuality > 0 || entry.RedirectStrategy == "rule");

            var unmatchedVisits = totalVisits - matchedVisits;

            var matchRate = totalVisits > 0
                ? Math.Round((double)matchedVisits / totalVisits * 100, 1)
                : 0;

            var feedbackOk = allEntries.Count(entry => entry.Feedback == "OK");
            var feedbackNok = allEntries.Count(entry => entry.Feedback == "NOK");
            var feedbackAutoRedirect = allEntries.Count(entry => entry.Feedback == "auto-redirect");
            var feedbackNone = allEntries.Count(entry => entry.Feedback == null);

            var topRules = allEntries
                .Where(entry => !string.IsNullOrEmpty(entry.RuleId))
                .GroupBy(entry => entry.RuleId!)
                .Select(group => new TopRuleStat
                {
                    RuleId = group.Key,
                    Count = group.Count()
                })
                .OrderByDescending(stat => stat.Count)
                .Take(10)
                .ToList();

            return new TrackingStats
            {
                TotalVisits = totalVisits,
                MatchedVisits = matchedVisits,
                UnmatchedVisits = unmatchedVisits,
                MatchRate = matchRate,
                FeedbackOk = feedbackOk,
                FeedbackNok = feedbackNok,
                FeedbackAutoRedirect = feedbackAutoRedirect,
                FeedbackNone = feedbackNone,
                TopRules = topRules
            };
        }

        public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        {
            var cutoffPartition = TrackingEntity.ToDatePartition(cutoff);
            var filter = $"PartitionKey lt '{cutoffPartition}'";
            var deleted = 0;

            await foreach (var entity in _tableClient.QueryAsync<TrackingEntity>(
                filter: filter,
                select: new[] { "PartitionKey", "RowKey" },
                cancellationToken: cancellationToken))
            {
                try
                {
                    await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, cancellationToken: cancellationToken);
                    deleted++;
                }
                catch (RequestFailedException exception) when (exception.Status == 404)
                {
                    // Already deleted, skip.
                }
            }

            return deleted;
        }

        public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
        {
            var deleted = 0;

            await foreach (var entity in _tableClient.QueryAsync<TrackingEntity>(
                select: new[] { "PartitionKey", "RowKey" },
                cancellationToken: cancellationToken))
            {
                try
                {
                    await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, cancellationToken: cancellationToken);

                    deleted++;
                }
                catch (RequestFailedException exception) when (exception.Status == 404)
                {
                    // Already deleted, skip.
                }
            }

            _logger.LogInformation("Deleted all tracking entries. Count: {Deleted}.", deleted);

            return deleted;
        }

        // -- Private helpers --

        private async Task<List<TrackingEntry>> LoadFilteredEntriesAsync(
            int retentionDays,
            string? search,
            int? qualityMin,
            int? qualityMax,
            string? feedbackType,
            string? ruleId,
            CancellationToken cancellationToken)
        {
            var allEntries = new List<TrackingEntry>();
            var filter = BuildDateRangeFilter(retentionDays);

            await foreach (var entity in _tableClient.QueryAsync<TrackingEntity>(
                filter: filter,
                cancellationToken: cancellationToken))
            {
                allEntries.Add(entity.ToDomainModel());
            }

            // Text search
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.ToLowerInvariant();

                allEntries = allEntries.Where(entry =>
                    entry.OldUrl.ToLowerInvariant().Contains(searchLower) ||
                    entry.NewUrl.ToLowerInvariant().Contains(searchLower) ||
                    entry.Path.ToLowerInvariant().Contains(searchLower) ||
                    (entry.RuleId != null && entry.RuleId.ToLowerInvariant().Contains(searchLower)) ||
                    (entry.Feedback != null && entry.Feedback.ToLowerInvariant().Contains(searchLower)) ||
                    (entry.RedirectStrategy != null && entry.RedirectStrategy.ToLowerInvariant().Contains(searchLower))
                ).ToList();
            }

            // Quality range filter
            if (qualityMin.HasValue)
            {
                allEntries = allEntries.Where(entry => entry.MatchQuality >= qualityMin.Value).ToList();
            }

            if (qualityMax.HasValue)
            {
                allEntries = allEntries.Where(entry => entry.MatchQuality <= qualityMax.Value).ToList();
            }

            // Feedback type filter
            if (!string.IsNullOrEmpty(feedbackType))
            {
                if (feedbackType.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    allEntries = allEntries.Where(entry => string.IsNullOrEmpty(entry.Feedback)).ToList();
                }
                else
                {
                    allEntries = allEntries.Where(entry =>
                        entry.Feedback != null &&
                        entry.Feedback.Equals(feedbackType, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }
            }

            // Rule ID filter
            if (!string.IsNullOrEmpty(ruleId))
            {
                allEntries = allEntries.Where(entry =>
                    entry.RuleId != null &&
                    entry.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            return allEntries;
        }

        private static string GetTrendBucketKey(DateTimeOffset timestamp, string aggregation)
        {
            return aggregation.ToLowerInvariant() switch
            {
                "week" => GetIsoWeekStart(timestamp).ToString("yyyy-MM-dd"),
                "month" => new DateTimeOffset(timestamp.Year, timestamp.Month, 1, 0, 0, 0, TimeSpan.Zero).ToString("yyyy-MM-dd"),
                _ => timestamp.ToString("yyyy-MM-dd")
            };
        }

        private static DateTimeOffset GetIsoWeekStart(DateTimeOffset date)
        {
            var dayOfWeek = (int)date.DayOfWeek;

            // ISO week starts on Monday (DayOfWeek.Sunday = 0, Monday = 1)
            var daysToSubtract = dayOfWeek == 0 ? 6 : dayOfWeek - 1;

            return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero).AddDays(-daysToSubtract);
        }

        private static List<DateTimeOffset> GenerateTrendBuckets(int days, string aggregation)
        {
            var buckets = new List<DateTimeOffset>();
            var now = DateTimeOffset.UtcNow;
            var start = now.AddDays(-days);

            switch (aggregation.ToLowerInvariant())
            {
                case "week":
                    {
                        var current = GetIsoWeekStart(start);
                        var end = GetIsoWeekStart(now);

                        while (current <= end)
                        {
                            buckets.Add(current);
                            current = current.AddDays(7);
                        }

                        break;
                    }

                case "month":
                    {
                        var current = new DateTimeOffset(start.Year, start.Month, 1, 0, 0, 0, TimeSpan.Zero);
                        var end = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

                        while (current <= end)
                        {
                            buckets.Add(current);
                            current = current.AddMonths(1);
                        }

                        break;
                    }

                default:
                    {
                        var current = new DateTimeOffset(start.Year, start.Month, start.Day, 0, 0, 0, TimeSpan.Zero);
                        var end = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);

                        while (current <= end)
                        {
                            buckets.Add(current);
                            current = current.AddDays(1);
                        }

                        break;
                    }
            }

            return buckets;
        }

        private static string BuildDateRangeFilter(int retentionDays, string? timeRange = null)
        {
            var effectiveDays = timeRange switch
            {
                "24h" => 1,
                "7d" => 7,
                _ => retentionDays
            };

            var from = TrackingEntity.ToDatePartition(DateTimeOffset.UtcNow.AddDays(-effectiveDays));
            var to = TrackingEntity.ToDatePartition(DateTimeOffset.UtcNow);

            return $"PartitionKey ge '{from}' and PartitionKey le '{to}'";
        }
    }
}