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
    public class TableStorageTrackingRepositoryTests : IAsyncLifetime
    {
        private readonly TableStorageTrackingRepository _sut;
        private readonly TableClient _tableClient;

        public TableStorageTrackingRepositoryTests(AzuriteFixture fixture)
        {
            var options = Options.Create(new TableStorageOptions
            {
                ConnectionString = AzuriteFixture.ConnectionString,
                TableName = "Tracking"
            });

            var logger = Substitute.For<ILogger<TableStorageTrackingRepository>>();
            _sut = new TableStorageTrackingRepository(options, logger);
            _tableClient = fixture.ServiceClient.GetTableClient("Tracking");
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();

            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                select: new[] { "PartitionKey", "RowKey" }))
            {
                try
                {
                    await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
                }
                catch
                {
                }
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private static TrackingEntry CreateEntry(
            string path = "/test",
            string feedback = "OK",
            int matchQuality = 100,
            string? ruleId = null)
        {
            return new TrackingEntry
            {
                Id = Guid.NewGuid(),
                OldUrl = $"http://old.com{path}",
                NewUrl = $"https://new.com{path}",
                Path = path,
                Timestamp = DateTimeOffset.UtcNow,
                Feedback = feedback,
                MatchQuality = matchQuality,
                RuleId = ruleId,
                RedirectStrategy = "rule"
            };
        }

        [Fact]
        public async Task CreateAsync_ReturnsId()
        {
            var entry = CreateEntry();

            var id = await _sut.CreateAsync(entry);

            id.Should().NotBe(Guid.Empty);
        }

        [Fact]
        public async Task CreateAsync_EmptyId_GeneratesNewId()
        {
            var entry = CreateEntry();
            entry.Id = Guid.Empty;

            var id = await _sut.CreateAsync(entry);

            id.Should().NotBe(Guid.Empty);
        }

        [Fact]
        public async Task GetByIdAsync_WithPartition_ReturnsEntry()
        {
            var entry = CreateEntry();

            await _sut.CreateAsync(entry);

            var partition = TrackingEntity.ToDatePartition(entry.Timestamp);

            var result = await _sut.GetByIdAsync(entry.Id, partition);

            result.Should().NotBeNull();
            result!.Path.Should().Be("/test");
        }

        [Fact]
        public async Task GetByIdAsync_WithoutPartition_ScansAndReturns()
        {
            var entry = CreateEntry();

            await _sut.CreateAsync(entry);

            var result = await _sut.GetByIdAsync(entry.Id);

            result.Should().NotBeNull();
            result!.Id.Should().Be(entry.Id);
        }

        [Fact]
        public async Task GetByIdAsync_NotFound_ReturnsNull()
        {
            var result = await _sut.GetByIdAsync(Guid.NewGuid(), "2024-01-01");

            result.Should().BeNull();
        }

        [Fact]
        public async Task UpdateAsync_ExistingEntry_ReturnsTrue()
        {
            var entry = CreateEntry(feedback: "OK");

            await _sut.CreateAsync(entry);

            entry.Feedback = "NOK";
            entry.UserProposedUrl = "https://correct.com";

            var result = await _sut.UpdateAsync(entry);

            result.Should().BeTrue();

            var fetched = await _sut.GetByIdAsync(entry.Id);

            fetched!.Feedback.Should().Be("NOK");
            fetched.UserProposedUrl.Should().Be("https://correct.com");
        }

        [Fact]
        public async Task GetPagedAsync_ReturnsPaginatedResults()
        {
            await _sut.CreateAsync(CreateEntry("/page1"));
            await _sut.CreateAsync(CreateEntry("/page2"));
            await _sut.CreateAsync(CreateEntry("/page3"));

            var (entries, totalCount) = await _sut.GetPagedAsync(page: 1, limit: 2, retentionDays: 1);

            totalCount.Should().Be(3);
            entries.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetPagedAsync_WithSearch_FiltersResults()
        {
            await _sut.CreateAsync(CreateEntry("/matching-path"));
            await _sut.CreateAsync(CreateEntry("/other"));

            var (entries, totalCount) = await _sut.GetPagedAsync(
                page: 1, limit: 50, search: "matching", retentionDays: 1);

            totalCount.Should().Be(1);
            entries[0].Path.Should().Be("/matching-path");
        }

        [Fact]
        public async Task GetPagedAsync_WithQualityFilter_FiltersResults()
        {
            await _sut.CreateAsync(CreateEntry(matchQuality: 100));
            await _sut.CreateAsync(CreateEntry(matchQuality: 50));

            var (entries, _) = await _sut.GetPagedAsync(
                page: 1, limit: 50, retentionDays: 1, qualityMin: 75);

            entries.Should().ContainSingle();
            entries[0].MatchQuality.Should().Be(100);
        }

        [Fact]
        public async Task GetPagedAsync_WithFeedbackFilter_FiltersResults()
        {
            await _sut.CreateAsync(CreateEntry(feedback: "OK"));
            await _sut.CreateAsync(CreateEntry(feedback: "NOK"));

            var (entries, _) = await _sut.GetPagedAsync(
                page: 1, limit: 50, retentionDays: 1, feedbackType: "OK");

            entries.Should().ContainSingle();
            entries[0].Feedback.Should().Be("OK");
        }

        [Fact]
        public async Task GetPagedAsync_WithRuleIdFilter_FiltersResults()
        {
            await _sut.CreateAsync(CreateEntry(ruleId: "rule-A"));
            await _sut.CreateAsync(CreateEntry(ruleId: "rule-B"));

            var (entries, _) = await _sut.GetPagedAsync(
                page: 1, limit: 50, retentionDays: 1, ruleId: "rule-A");

            entries.Should().ContainSingle();
        }

        [Fact]
        public async Task GetStatsAsync_ReturnsAggregatedStats()
        {
            await _sut.CreateAsync(CreateEntry(feedback: "OK", matchQuality: 100, ruleId: "r1"));
            await _sut.CreateAsync(CreateEntry(feedback: "NOK", matchQuality: 50));
            await _sut.CreateAsync(CreateEntry(feedback: "auto-redirect", matchQuality: 100, ruleId: "r1"));

            var stats = await _sut.GetStatsAsync(retentionDays: 1);

            stats.TotalVisits.Should().Be(3);
            stats.FeedbackOk.Should().Be(1);
            stats.FeedbackNok.Should().Be(1);
            stats.FeedbackAutoRedirect.Should().Be(1);
            stats.TopRules.Should().NotBeEmpty();
        }

        [Fact]
        public async Task GetTrendAsync_ReturnsBuckets()
        {
            await _sut.CreateAsync(CreateEntry(feedback: "OK"));

            var trend = await _sut.GetTrendAsync(days: 1, aggregation: "day");

            trend.Should().NotBeEmpty();
        }

        [Fact]
        public async Task DeleteAllAsync_ClearsTable()
        {
            await _sut.CreateAsync(CreateEntry());
            await _sut.CreateAsync(CreateEntry());

            var deleted = await _sut.DeleteAllAsync();

            deleted.Should().Be(2);

            var (_, total) = await _sut.GetPagedAsync(1, 100, retentionDays: 1);

            total.Should().Be(0);
        }

        [Fact]
        public async Task GetAllFilteredAsync_ReturnsOrderedResults()
        {
            await _sut.CreateAsync(CreateEntry("/first"));
            await _sut.CreateAsync(CreateEntry("/second"));

            var results = await _sut.GetAllFilteredAsync(retentionDays: 1);

            results.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetStatsAsync_WithTimeRange_Filters()
        {
            await _sut.CreateAsync(CreateEntry());

            var stats = await _sut.GetStatsAsync(retentionDays: 30, timeRange: "24h");

            stats.TotalVisits.Should().BeGreaterOrEqualTo(1);
        }

        [Fact]
        public async Task GetStatsAsync_TimeRange7d_Filters()
        {
            await _sut.CreateAsync(CreateEntry());

            var stats = await _sut.GetStatsAsync(retentionDays: 30, timeRange: "7d");

            stats.TotalVisits.Should().BeGreaterOrEqualTo(1);
        }

        [Fact]
        public async Task GetPagedAsync_WithQualityMax_FiltersResults()
        {
            await _sut.CreateAsync(CreateEntry(matchQuality: 100));
            await _sut.CreateAsync(CreateEntry(matchQuality: 50));

            var (entries, _) = await _sut.GetPagedAsync(
                page: 1, limit: 50, retentionDays: 1, qualityMax: 75);

            entries.Should().ContainSingle();
            entries[0].MatchQuality.Should().Be(50);
        }

        [Fact]
        public async Task GetPagedAsync_FeedbackNone_FiltersEmptyFeedback()
        {
            await _sut.CreateAsync(CreateEntry(feedback: "OK"));

            var entryNoFeedback = CreateEntry();
            entryNoFeedback.Feedback = null;
            await _sut.CreateAsync(entryNoFeedback);

            var (entries, _) = await _sut.GetPagedAsync(
                page: 1, limit: 50, retentionDays: 1, feedbackType: "none");

            entries.Should().ContainSingle();
            entries[0].Feedback.Should().BeNullOrEmpty();
        }

        [Fact]
        public async Task GetTrendAsync_WeekAggregation_ReturnsBuckets()
        {
            await _sut.CreateAsync(CreateEntry(feedback: "OK"));

            var trend = await _sut.GetTrendAsync(days: 7, aggregation: "week");

            trend.Should().NotBeEmpty();
        }

        [Fact]
        public async Task GetTrendAsync_MonthAggregation_ReturnsBuckets()
        {
            await _sut.CreateAsync(CreateEntry(feedback: "OK"));

            var trend = await _sut.GetTrendAsync(days: 30, aggregation: "month");

            trend.Should().NotBeEmpty();
        }

        [Fact]
        public async Task GetStatsAsync_NoFeedback_CountsAsNone()
        {
            var entry = CreateEntry();
            entry.Feedback = null;

            await _sut.CreateAsync(entry);

            var stats = await _sut.GetStatsAsync(retentionDays: 1);

            stats.FeedbackNone.Should().Be(1);
        }

        [Fact]
        public async Task GetStatsAsync_MatchRate_CalculatedCorrectly()
        {
            await _sut.CreateAsync(CreateEntry(matchQuality: 100, ruleId: "r1"));

            var unmatchedEntry = CreateEntry(matchQuality: 0);
            unmatchedEntry.RuleId = null;
            unmatchedEntry.NewUrl = null;
            await _sut.CreateAsync(unmatchedEntry);

            var stats = await _sut.GetStatsAsync(retentionDays: 1);

            stats.TotalVisits.Should().Be(2);
            stats.MatchedVisits.Should().BeGreaterOrEqualTo(1);
            stats.MatchRate.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task DeleteOlderThanAsync_DeletesOldEntries()
        {
            await _sut.CreateAsync(CreateEntry());

            // Delete entries older than now + 1 day (should delete everything)
            var deleted = await _sut.DeleteOlderThanAsync(DateTimeOffset.UtcNow.AddDays(1));

            deleted.Should().BeGreaterOrEqualTo(1);
        }

        [Fact]
        public async Task GetAllFilteredAsync_WithSearch_Filters()
        {
            await _sut.CreateAsync(CreateEntry("/findable-path"));
            await _sut.CreateAsync(CreateEntry("/other"));

            var results = await _sut.GetAllFilteredAsync(
                retentionDays: 1, search: "findable");

            results.Should().ContainSingle();
        }

        [Fact]
        public async Task GetAllFilteredAsync_WithQualityFilter_Filters()
        {
            await _sut.CreateAsync(CreateEntry(matchQuality: 100));
            await _sut.CreateAsync(CreateEntry(matchQuality: 30));

            var results = await _sut.GetAllFilteredAsync(
                retentionDays: 1, qualityMin: 50);

            results.Should().ContainSingle();
        }
    }
}
