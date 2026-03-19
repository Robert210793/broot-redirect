using Azure;
using Azure.Data.Tables;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Broot.Redirect.Infrastructure.Persistence
{
    public class TableStorageOptions
    {
        public string ConnectionString { get; set; } = string.Empty;

        public string TableName { get; set; } = "RedirectRules";
    }

    public class TableStorageRuleRepository : IRedirectRuleRepository
    {
        private const string PartitionKeyValue = "rule";
        private const int BatchSize = 100;

        private readonly TableClient _tableClient;
        private readonly ILogger<TableStorageRuleRepository> _logger;

        public TableStorageRuleRepository(
            IOptions<TableStorageOptions> options,
            ILogger<TableStorageRuleRepository> logger)
        {
            _logger = logger;

            var tableServiceClient = new TableServiceClient(options.Value.ConnectionString);

            _tableClient = tableServiceClient.GetTableClient(options.Value.TableName);
        }

        /// <summary>
        /// Ensures the table exists. Called once on startup by the warmup service.
        /// </summary>
        /// 
        public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
        {
            await _tableClient.CreateIfNotExistsAsync(cancellationToken);

            _logger.LogInformation("Table Storage table ensured.");
        }

        public async Task<List<RedirectRule>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var rules = new List<RedirectRule>();
            var pageCount = 0;

            await foreach (var entity in _tableClient.QueryAsync<RedirectRuleEntity>(
                filter: $"PartitionKey eq '{PartitionKeyValue}'",
                cancellationToken: cancellationToken))
            {
                rules.Add(entity.ToDomainModel());

                if (rules.Count % 1000 == 0)
                {
                    pageCount++;

                    _logger.LogDebug("Loaded page {PageCount}, total rules so far: {RuleCount}", pageCount, rules.Count);
                }
            }

            _logger.LogInformation("Loaded {RuleCount} rules from Table Storage.", rules.Count);

            return rules;
        }

        public async Task<RedirectRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<RedirectRuleEntity>(
                    PartitionKeyValue,
                    id.ToString("N"),
                    cancellationToken: cancellationToken);

                return response.Value.ToDomainModel();
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                return null;
            }
        }

        public async Task CreateAsync(RedirectRule rule, CancellationToken cancellationToken = default)
        {
            var entity = RedirectRuleEntity.FromDomainModel(rule);

            await _tableClient.AddEntityAsync(entity, cancellationToken);

            _logger.LogDebug("Created rule {RuleId} in Table Storage.", rule.Id);
        }

        public async Task<bool> UpdateAsync(RedirectRule rule, CancellationToken cancellationToken = default)
        {
            var entity = RedirectRuleEntity.FromDomainModel(rule);

            try
            {
                await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);

                _logger.LogDebug("Updated rule {RuleId} in Table Storage.", rule.Id);

                return true;
            }
            catch (RequestFailedException exception)
            {
                _logger.LogWarning(exception, "Failed to update rule {RuleId}.", rule.Id);

                return false;
            }
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(
                    PartitionKeyValue,
                    id.ToString("N"),
                    ETag.All,
                    cancellationToken);

                _logger.LogDebug("Deleted rule {RuleId} from Table Storage.", id);

                return true;
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                return false;
            }
        }

        public async Task<int> BulkDeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
        {
            var deletedCount = 0;

            foreach (var batch in ChunkBy(ids, BatchSize))
            {
                var transactionActions = new List<TableTransactionAction>();

                foreach (var id in batch)
                {
                    transactionActions.Add(new TableTransactionAction(
                        TableTransactionActionType.Delete,
                        new TableEntity(PartitionKeyValue, id.ToString("N"))
                        {
                            ETag = ETag.All
                        }));
                }

                try
                {
                    await _tableClient.SubmitTransactionAsync(transactionActions, cancellationToken);

                    deletedCount += batch.Count;
                }
                catch (TableTransactionFailedException exception)
                {
                    _logger.LogWarning(exception, "Batch delete failed, falling back to individual deletes.");

                    foreach (var id in batch)
                    {
                        if (await DeleteAsync(id, cancellationToken))
                        {
                            deletedCount++;
                        }
                    }
                }
            }

            _logger.LogInformation("Bulk deleted {DeletedCount} of {RequestedCount} rules.", deletedCount, ids.Count);

            return deletedCount;
        }

        public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
        {
            var allIds = new List<Guid>();

            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{PartitionKeyValue}'",
                select: new[] { "RowKey" },
                cancellationToken: cancellationToken))
            {
                if (Guid.TryParse(entity.RowKey, out var id))
                {
                    allIds.Add(id);
                }
            }

            if (allIds.Count == 0)
            {
                return 0;
            }

            _logger.LogInformation("DeleteAll: found {Count} rules to delete.", allIds.Count);

            return await BulkDeleteAsync(allIds, cancellationToken);
        }

        public async Task BulkUpsertAsync(IReadOnlyList<RedirectRule> rules, CancellationToken cancellationToken = default)
        {
            var entities = rules.Select(RedirectRuleEntity.FromDomainModel).ToList();

            foreach (var batch in ChunkBy(entities, BatchSize))
            {
                var transactionActions = batch
                    .Select(entity => new TableTransactionAction(TableTransactionActionType.UpsertReplace, entity))
                    .ToList();

                try
                {
                    await _tableClient.SubmitTransactionAsync(transactionActions, cancellationToken);
                }
                catch (TableTransactionFailedException exception)
                {
                    _logger.LogWarning(exception, "Batch upsert failed, falling back to individual upserts.");

                    foreach (var entity in batch)
                    {
                        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
                    }
                }
            }

            _logger.LogInformation("Bulk upserted {RuleCount} rules to Table Storage.", rules.Count);
        }

        public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await foreach (var _ in _tableClient.QueryAsync<TableEntity>(
                    filter: $"PartitionKey eq '{PartitionKeyValue}'",
                    maxPerPage: 1,
                    cancellationToken: cancellationToken))
                {
                    break;
                }

                return true;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Table Storage health check failed.");

                return false;
            }
        }

        private static List<List<T>> ChunkBy<T>(IReadOnlyList<T> source, int chunkSize)
        {
            var chunks = new List<List<T>>();

            for (var i = 0; i < source.Count; i += chunkSize)
            {
                var chunk = new List<T>();

                for (var j = i; j < Math.Min(i + chunkSize, source.Count); j++)
                {
                    chunk.Add(source[j]);
                }

                chunks.Add(chunk);
            }

            return chunks;
        }
    }
}