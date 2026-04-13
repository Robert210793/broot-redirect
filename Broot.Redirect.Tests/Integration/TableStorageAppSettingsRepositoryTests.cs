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
    public class TableStorageAppSettingsRepositoryTests : IAsyncLifetime
    {
        private readonly TableStorageAppSettingsRepository _sut;
        private readonly TableClient _tableClient;

        public TableStorageAppSettingsRepositoryTests(AzuriteFixture fixture)
        {
            var options = Options.Create(new TableStorageOptions
            {
                ConnectionString = AzuriteFixture.ConnectionString,
                TableName = "AppSettings"
            });

            var logger = Substitute.For<ILogger<TableStorageAppSettingsRepository>>();
            _sut = new TableStorageAppSettingsRepository(options, logger);
            _tableClient = fixture.ServiceClient.GetTableClient("AppSettings");
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();

            try
            {
                await _tableClient.DeleteEntityAsync(
                    AppSettingsEntity.DefaultPartitionKey,
                    AppSettingsEntity.DefaultRowKey);
            }
            catch
            {
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task GetAsync_NoData_ReturnsNull()
        {
            var result = await _sut.GetAsync();

            result.Should().BeNull();
        }

        [Fact]
        public async Task SaveAsync_AndGetAsync_RoundTrips()
        {
            var settings = new AppSettings
            {
                DefaultNewDomain = "https://test.com",
                NoMatchBehavior = "SmartSearch",
                HeaderTitle = "Integration Test",
                SmartSearchUrl = "https://search.com/q=",
                CaseSensitiveLinkDetection = true,
                EnableFeedbackSurvey = true
            };

            await _sut.SaveAsync(settings);

            var result = await _sut.GetAsync();

            result.Should().NotBeNull();
            result!.DefaultNewDomain.Should().Be("https://test.com");
            result.NoMatchBehavior.Should().Be("SmartSearch");
            result.HeaderTitle.Should().Be("Integration Test");
            result.CaseSensitiveLinkDetection.Should().BeTrue();
        }

        [Fact]
        public async Task SaveAsync_Twice_OverwritesPreviousSettings()
        {
            await _sut.SaveAsync(new AppSettings { HeaderTitle = "First" });

            await _sut.SaveAsync(new AppSettings { HeaderTitle = "Second" });

            var result = await _sut.GetAsync();

            result!.HeaderTitle.Should().Be("Second");
        }
    }
}