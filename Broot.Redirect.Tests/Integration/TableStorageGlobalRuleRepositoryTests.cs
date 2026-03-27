using Azure.Data.Tables;
using Broot.Redirect.Core.Models;
using Broot.Redirect.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.Integration
{
    [Collection("Azurite")]
    [Trait("Category", "Integration")]
    public class TableStorageGlobalRuleRepositoryTests : IAsyncLifetime
    {
        private readonly TableStorageGlobalRuleRepository _sut;
        private readonly TableClient _tableClient;

        public TableStorageGlobalRuleRepositoryTests(AzuriteFixture fixture)
        {
            var options = Options.Create(new TableStorageOptions
            {
                ConnectionString = AzuriteFixture.ConnectionString,
                TableName = "GlobalRules"
            });

            var logger = Substitute.For<ILogger<TableStorageGlobalRuleRepository>>();
            _sut = new TableStorageGlobalRuleRepository(options, logger);
            _tableClient = fixture.ServiceClient.GetTableClient("GlobalRules");
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();

            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                select: new[] { "PartitionKey", "RowKey" }))
            {
                await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task CreateAsync_AndGetAllAsync_RoundTrips()
        {
            var rule = new GlobalRule
            {
                Id = Guid.NewGuid(),
                Search = "old",
                Replace = "new",
                CaseSensitive = true,
                Priority = 1,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _sut.CreateAsync(rule);

            var all = await _sut.GetAllAsync();

            all.Should().ContainSingle();
            all[0].Id.Should().Be(rule.Id);
            all[0].Search.Should().Be("old");
            all[0].CaseSensitive.Should().BeTrue();
        }

        [Fact]
        public async Task GetByIdAsync_Exists_ReturnsRule()
        {
            var rule = new GlobalRule
            {
                Id = Guid.NewGuid(),
                Search = "find",
                Replace = "replace",
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _sut.CreateAsync(rule);

            var result = await _sut.GetByIdAsync(rule.Id);

            result.Should().NotBeNull();
            result!.Search.Should().Be("find");
        }

        [Fact]
        public async Task GetByIdAsync_NotFound_ReturnsNull()
        {
            var result = await _sut.GetByIdAsync(Guid.NewGuid());

            result.Should().BeNull();
        }

        [Fact]
        public async Task UpdateAsync_UpdatesExisting()
        {
            var rule = new GlobalRule
            {
                Id = Guid.NewGuid(),
                Search = "original",
                Replace = "value",
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _sut.CreateAsync(rule);

            rule.Search = "updated";

            await _sut.UpdateAsync(rule);

            var fetched = await _sut.GetByIdAsync(rule.Id);

            fetched!.Search.Should().Be("updated");
        }

        [Fact]
        public async Task DeleteAsync_ExistingRule_ReturnsTrueAndRemoves()
        {
            var rule = new GlobalRule
            {
                Id = Guid.NewGuid(),
                Search = "delete-me",
                Replace = "value",
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _sut.CreateAsync(rule);

            var result = await _sut.DeleteAsync(rule.Id);

            result.Should().BeTrue();

            var fetched = await _sut.GetByIdAsync(rule.Id);

            fetched.Should().BeNull();
        }

        [Fact]
        public async Task DeleteAsync_NotFound_DoesNotThrow()
        {
            var act = () => _sut.DeleteAsync(Guid.NewGuid());

            await act.Should().NotThrowAsync();
        }
    }
}
