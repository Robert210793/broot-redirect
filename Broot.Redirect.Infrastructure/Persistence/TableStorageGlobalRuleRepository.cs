using Azure;
using Azure.Data.Tables;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Infrastructure.Persistence
{
    public class TableStorageGlobalRuleRepository : IGlobalRuleRepository
    {
        private readonly TableClient _tableClient;
        private readonly ILogger<TableStorageGlobalRuleRepository> _logger;

        public TableStorageGlobalRuleRepository(
            IOptions<TableStorageOptions> options,
            ILogger<TableStorageGlobalRuleRepository> logger)
        {
            _logger = logger;

            var tableServiceClient = new TableServiceClient(options.Value.ConnectionString);

            _tableClient = tableServiceClient.GetTableClient("GlobalRules");
        }

        /// <summary>
        /// Ensures the GlobalRules table exists. Called once on startup by the warmup service.
        /// </summary>
        public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
        {
            await _tableClient.CreateIfNotExistsAsync(cancellationToken);

            _logger.LogInformation("GlobalRules table ensured.");
        }

        public async Task<List<GlobalRule>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var rules = new List<GlobalRule>();

            await foreach (var entity in _tableClient.QueryAsync<GlobalRuleEntity>(
                filter: $"PartitionKey eq '{GlobalRuleEntity.DefaultPartitionKey}'",
                cancellationToken: cancellationToken))
            {
                rules.Add(entity.ToDomainModel());
            }

            _logger.LogInformation("Loaded {RuleCount} global rules from Table Storage.", rules.Count);

            return rules;
        }

        public async Task<GlobalRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<GlobalRuleEntity>(
                    GlobalRuleEntity.DefaultPartitionKey,
                    id.ToString("N"),
                    cancellationToken: cancellationToken);

                return response.Value.ToDomainModel();
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                return null;
            }
        }

        public async Task CreateAsync(GlobalRule rule, CancellationToken cancellationToken = default)
        {
            var entity = GlobalRuleEntity.FromDomainModel(rule);

            await _tableClient.AddEntityAsync(entity, cancellationToken);

            _logger.LogDebug("Created global rule {RuleId} in Table Storage.", rule.Id);
        }

        public async Task UpdateAsync(GlobalRule rule, CancellationToken cancellationToken = default)
        {
            var entity = GlobalRuleEntity.FromDomainModel(rule);

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);

            _logger.LogDebug("Updated global rule {RuleId} in Table Storage.", rule.Id);
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(
                    GlobalRuleEntity.DefaultPartitionKey,
                    id.ToString("N"),
                    ETag.All,
                    cancellationToken);

                _logger.LogDebug("Deleted global rule {RuleId} from Table Storage.", id);

                return true;
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                return false;
            }
        }
    }
}
