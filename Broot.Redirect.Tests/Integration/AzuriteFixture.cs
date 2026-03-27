using Azure.Data.Tables;
using Xunit;

namespace Broot.Redirect.Tests.Integration
{
    public class AzuriteFixture : IAsyncLifetime
    {
        public const string ConnectionString =
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
            "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            "TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";

        private readonly List<string> _tableNames = new();

        public TableServiceClient ServiceClient { get; private set; } = null!;

        public Task InitializeAsync()
        {
            ServiceClient = new TableServiceClient(ConnectionString);

            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            foreach (var tableName in _tableNames)
            {
                try
                {
                    await ServiceClient.DeleteTableAsync(tableName);
                }
                catch
                {
                }
            }
        }

        public async Task<TableClient> CreateTableAsync(string tableName)
        {
            var client = ServiceClient.GetTableClient(tableName);

            await client.CreateIfNotExistsAsync();

            _tableNames.Add(tableName);

            return client;
        }
    }

    [CollectionDefinition("Azurite")]
    public class AzuriteCollection : ICollectionFixture<AzuriteFixture>
    {
    }
}
