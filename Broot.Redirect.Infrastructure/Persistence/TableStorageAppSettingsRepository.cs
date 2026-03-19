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
    public class TableStorageAppSettingsRepository : IAppSettingsRepository
    {
        private readonly TableClient _tableClient;
        private readonly ILogger<TableStorageAppSettingsRepository> _logger;

        public TableStorageAppSettingsRepository(
            IOptions<TableStorageOptions> options,
            ILogger<TableStorageAppSettingsRepository> logger)
        {
            _logger = logger;

            var tableServiceClient = new TableServiceClient(options.Value.ConnectionString);

            _tableClient = tableServiceClient.GetTableClient("AppSettings");
        }

        public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
        {
            await _tableClient.CreateIfNotExistsAsync(cancellationToken);

            _logger.LogInformation("AppSettings table ensured.");
        }

        public async Task<AppSettings?> GetAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<AppSettingsEntity>(
                    AppSettingsEntity.DefaultPartitionKey,
                    AppSettingsEntity.DefaultRowKey,
                    cancellationToken: cancellationToken);

                _logger.LogDebug("Loaded AppSettings from Table Storage.");

                return response.Value.ToDomainModel();
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                _logger.LogInformation("No AppSettings row found in Table Storage (first boot).");

                return null;
            }
        }

        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            var entity = AppSettingsEntity.FromDomainModel(settings);

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);

            _logger.LogInformation("Saved AppSettings to Table Storage.");
        }
    }
}
