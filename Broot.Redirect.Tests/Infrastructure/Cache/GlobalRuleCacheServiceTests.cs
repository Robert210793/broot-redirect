using Broot.Redirect.Core.Models;
using Broot.Redirect.Infrastructure.Cache;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.Infrastructure.Cache
{
    public class GlobalRuleCacheServiceTests
    {
        private static GlobalRuleCacheService CreateService()
        {
            var logger = Substitute.For<ILogger<GlobalRuleCacheService>>();

            return new GlobalRuleCacheService(logger);
        }

        private static GlobalRule CreateRule(int priority = 0, string search = "old", string replace = "new")
        {
            return new GlobalRule
            {
                Id = Guid.NewGuid(),
                Search = search,
                Replace = replace,
                CaseSensitive = false,
                Priority = priority,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        [Fact]
        public void IsWarmedUp_FalseBeforeInit_TrueAfter()
        {
            var sut = CreateService();

            sut.IsWarmedUp.Should().BeFalse();

            sut.Initialize(Array.Empty<GlobalRule>());

            sut.IsWarmedUp.Should().BeTrue();
        }

        [Fact]
        public void Initialize_PopulatesRules()
        {
            var sut = CreateService();
            var rules = new[] { CreateRule(1), CreateRule(2) };

            sut.Initialize(rules);

            sut.RuleCount.Should().Be(2);
            sut.GetAll().Should().HaveCount(2);
        }

        [Fact]
        public void Initialize_SortsByPriority()
        {
            var sut = CreateService();
            var highPriority = CreateRule(priority: 1);
            var lowPriority = CreateRule(priority: 0);

            sut.Initialize(new[] { highPriority, lowPriority });

            var all = sut.GetAll();

            all[0].Priority.Should().BeLessThanOrEqualTo(all[1].Priority);
        }

        [Fact]
        public void GetById_Exists_ReturnsRule()
        {
            var sut = CreateService();
            var rule = CreateRule();

            sut.Initialize(new[] { rule });

            sut.GetById(rule.Id).Should().Be(rule);
        }

        [Fact]
        public void GetById_NotFound_ReturnsNull()
        {
            var sut = CreateService();

            sut.Initialize(Array.Empty<GlobalRule>());

            sut.GetById(Guid.NewGuid()).Should().BeNull();
        }

        [Fact]
        public void AddRule_AddsAndSorts()
        {
            var sut = CreateService();

            sut.Initialize(Array.Empty<GlobalRule>());

            var rule = CreateRule(priority: 5);

            sut.AddRule(rule);

            sut.RuleCount.Should().Be(1);
            sut.GetById(rule.Id).Should().Be(rule);
        }

        [Fact]
        public void UpdateRule_UpdatesExisting()
        {
            var sut = CreateService();
            var rule = CreateRule(search: "original");

            sut.Initialize(new[] { rule });

            var updated = new GlobalRule
            {
                Id = rule.Id,
                Search = "updated",
                Replace = "new",
                Priority = rule.Priority,
                CreatedAt = rule.CreatedAt
            };

            sut.UpdateRule(updated);

            sut.GetById(rule.Id)!.Search.Should().Be("updated");
        }

        [Fact]
        public void RemoveRule_RemovesExisting()
        {
            var sut = CreateService();
            var rule = CreateRule();

            sut.Initialize(new[] { rule });

            sut.RemoveRule(rule.Id);

            sut.RuleCount.Should().Be(0);
            sut.GetById(rule.Id).Should().BeNull();
        }

        [Fact]
        public void SortOrder_SamePriority_SortsByCreatedAt()
        {
            var sut = CreateService();

            var older = new GlobalRule
            {
                Id = Guid.NewGuid(),
                Search = "older",
                Replace = "a",
                Priority = 0,
                CreatedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };

            var newer = new GlobalRule
            {
                Id = Guid.NewGuid(),
                Search = "newer",
                Replace = "b",
                Priority = 0,
                CreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };

            sut.Initialize(new[] { newer, older });

            var all = sut.GetAll();

            all[0].Search.Should().Be("older");
            all[1].Search.Should().Be("newer");
        }
    }
}
