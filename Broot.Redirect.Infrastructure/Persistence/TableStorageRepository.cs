using Azure;
using Azure.Data.Tables;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
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
            CancellationToken cancellationToken = default)
        {
            var allEntries = new List<TrackingEntry>();
            var filter = BuildDateRangeFilter(retentionDays);

            await foreach (var entity in _tableClient.QueryAsync<TrackingEntity>(
                filter: filter,
                cancellationToken: cancellationToken))
            {
                allEntries.Add(entity.ToDomainModel());
            }

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

            var totalCount = allEntries.Count;

            var paged = allEntries
                .OrderByDescending(entry => entry.Timestamp)
                .Skip((page - 1) * limit)
                .Take(limit)
                .ToList();

            return (paged, totalCount);
        }

        public async Task<TrackingStats> GetStatsAsync(int retentionDays = 30, CancellationToken cancellationToken = default)
        {
            var allEntries = new List<TrackingEntry>();
            var filter = BuildDateRangeFilter(retentionDays);

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

        private static string BuildDateRangeFilter(int retentionDays)
        {
            var from = TrackingEntity.ToDatePartition(DateTimeOffset.UtcNow.AddDays(-retentionDays));
            var to = TrackingEntity.ToDatePartition(DateTimeOffset.UtcNow);

            return $"PartitionKey ge '{from}' and PartitionKey le '{to}'";
        }
    }
}