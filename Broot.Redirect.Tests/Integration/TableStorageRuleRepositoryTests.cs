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
    public class TableStorageRuleRepositoryTests : IAsyncLifetime
    {
        private readonly AzuriteFixture _fixture;
        private readonly TableStorageRuleRepository _sut;
        private readonly string _tableName;

        public TableStorageRuleRepositoryTests(AzuriteFixture fixture)
        {
            _fixture = fixture;
            _tableName = $"Rules{Guid.NewGuid():N}";

            var options = Options.Create(new TableStorageOptions
            {
                ConnectionString = AzuriteFixture.ConnectionString,
                TableName = _tableName
            });

            var logger = Substitute.For<ILogger<TableStorageRuleRepository>>();
            _sut = new TableStorageRuleRepository(options, logger);
        }

        public async Task InitializeAsync()
        {
            await _sut.EnsureTableExistsAsync();
        }

        public async Task DisposeAsync()
        {
            try
            {
                await _fixture.ServiceClient.DeleteTableAsync(_tableName);
            }
            catch
            {
            }
        }

        private static RedirectRule CreateRule(string matcher = "/test")
        {
            return new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = matcher,
                TargetUrl = "/target",
                RedirectType = RedirectType.Partial,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        [Fact]
        public async Task CreateAsync_AndGetAllAsync_RoundTrips()
        {
            var rule = CreateRule("/create-test");

            await _sut.CreateAsync(rule);

            var all = await _sut.GetAllAsync();

            all.Should().ContainSingle();
            all[0].Id.Should().Be(rule.Id);
            all[0].Matcher.Should().Be("/create-test");
        }

        [Fact]
        public async Task GetByIdAsync_Exists_ReturnsRule()
        {
            var rule = CreateRule();

            await _sut.CreateAsync(rule);

            var result = await _sut.GetByIdAsync(rule.Id);

            result.Should().NotBeNull();
            result!.Id.Should().Be(rule.Id);
        }

        [Fact]
        public async Task GetByIdAsync_NotFound_ReturnsNull()
        {
            var result = await _sut.GetByIdAsync(Guid.NewGuid());

            result.Should().BeNull();
        }

        [Fact]
        public async Task UpdateAsync_ExistingRule_ReturnsTrue()
        {
            var rule = CreateRule();

            await _sut.CreateAsync(rule);

            rule.TargetUrl = "/updated-target";

            var result = await _sut.UpdateAsync(rule);

            result.Should().BeTrue();

            var fetched = await _sut.GetByIdAsync(rule.Id);

            fetched!.TargetUrl.Should().Be("/updated-target");
        }

        [Fact]
        public async Task DeleteAsync_ExistingRule_ReturnsTrueAndRemoves()
        {
            var rule = CreateRule();

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

        [Fact]
        public async Task BulkDeleteAsync_DeletesMultiple()
        {
            var rule1 = CreateRule("/bulk1");
            var rule2 = CreateRule("/bulk2");

            await _sut.CreateAsync(rule1);
            await _sut.CreateAsync(rule2);

            var deleted = await _sut.BulkDeleteAsync(new[] { rule1.Id, rule2.Id });

            deleted.Should().Be(2);

            var all = await _sut.GetAllAsync();

            all.Should().BeEmpty();
        }

        [Fact]
        public async Task BulkUpsertAsync_InsertsAndUpdates()
        {
            var rule = CreateRule("/bulk-upsert");

            await _sut.BulkUpsertAsync(new[] { rule });

            var all = await _sut.GetAllAsync();

            all.Should().ContainSingle();
            all[0].Matcher.Should().Be("/bulk-upsert");
        }

        [Fact]
        public async Task BulkUpsertAsync_EmptyList_NoError()
        {
            await _sut.BulkUpsertAsync(Array.Empty<RedirectRule>());

            var all = await _sut.GetAllAsync();

            all.Should().BeEmpty();
        }

        [Fact]
        public async Task BulkUpsertAsync_MultipleRules_InsertsAll()
        {
            var rules = Enumerable.Range(1, 5)
                .Select(i => CreateRule($"/bulk-{i}"))
                .ToList();

            await _sut.BulkUpsertAsync(rules);

            var all = await _sut.GetAllAsync();

            all.Should().HaveCount(5);
        }

        [Fact]
        public async Task BulkUpsertAsync_UpdatesExistingRule()
        {
            var rule = CreateRule("/bulk-update");
            rule.TargetUrl = "/original";

            await _sut.CreateAsync(rule);

            rule.TargetUrl = "/updated";

            await _sut.BulkUpsertAsync(new[] { rule });

            var all = await _sut.GetAllAsync();

            all.Should().ContainSingle();
            all[0].TargetUrl.Should().Be("/updated");
        }

        [Fact]
        public async Task DeleteAllAsync_ClearsTable()
        {
            await _sut.CreateAsync(CreateRule("/del1"));
            await _sut.CreateAsync(CreateRule("/del2"));

            var deleted = await _sut.DeleteAllAsync();

            deleted.Should().Be(2);

            var all = await _sut.GetAllAsync();

            all.Should().BeEmpty();
        }

        [Fact]
        public async Task HealthCheckAsync_ReturnsTrue()
        {
            var result = await _sut.HealthCheckAsync();

            result.Should().BeTrue();
        }

        [Fact]
        public async Task CreateAsync_WithJsonFields_RoundTripsCorrectly()
        {
            var rule = new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = "/json-test",
                TargetUrl = "/target",
                RedirectType = RedirectType.Wildcard,
                KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^utm_", TargetKey = "source" }
                },
                StaticQueryParams = new List<StaticQueryParam>
                {
                    new StaticQueryParam { Key = "ref", Value = "redirect" }
                },
                SearchAndReplace = new List<SearchAndReplaceEntry>
                {
                    new SearchAndReplaceEntry { Search = "old", Replace = "new", CaseSensitive = true }
                },
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _sut.CreateAsync(rule);

            var fetched = await _sut.GetByIdAsync(rule.Id);

            fetched!.KeptQueryParams.Should().HaveCount(1);
            fetched.KeptQueryParams[0].KeyPattern.Should().Be("^utm_");
            fetched.StaticQueryParams.Should().HaveCount(1);
            fetched.SearchAndReplace.Should().HaveCount(1);
            fetched.SearchAndReplace[0].CaseSensitive.Should().BeTrue();
        }
    }
}
