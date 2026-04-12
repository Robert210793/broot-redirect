using Broot.Redirect.Core.Models;
using Broot.Redirect.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Broot.Redirect.Tests.Infrastructure.Persistence
{
    public class RedirectRuleEntityTests
    {
        [Fact]
        public void FromDomainModel_MapsAllProperties()
        {
            var rule = new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = "/old-path",
                TargetUrl = "https://new.com",
                RedirectType = RedirectType.Wildcard,
                InfoText = "Some info",
                AutoRedirect = true,
                DiscardQueryParams = true,
                ForwardQueryParams = false,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var entity = RedirectRuleEntity.FromDomainModel(rule);

            entity.RowKey.Should().Be(rule.Id.ToString("N"));
            entity.Matcher.Should().Be("/old-path");
            entity.TargetUrl.Should().Be("https://new.com");
            entity.RedirectType.Should().Be("wildcard");
            entity.InfoText.Should().Be("Some info");
            entity.AutoRedirect.Should().BeTrue();
            entity.DiscardQueryParams.Should().BeTrue();
            entity.ForwardQueryParams.Should().BeFalse();
            entity.PartitionKey.Should().Be("rule");
        }

        [Fact]
        public void ToDomainModel_MapsAllProperties()
        {
            var id = Guid.NewGuid();

            var entity = new RedirectRuleEntity
            {
                RowKey = id.ToString("N"),
                Matcher = "/test",
                TargetUrl = "/target",
                RedirectType = "partial",
                InfoText = "Info",
                AutoRedirect = false,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var rule = entity.ToDomainModel();

            rule.Id.Should().Be(id);
            rule.Matcher.Should().Be("/test");
            rule.RedirectType.Should().Be(RedirectType.Partial);
        }

        [Fact]
        public void FromDomainModel_WithKeptQueryParams_SerializesToJson()
        {
            var rule = new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = "/path",
                KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^utm_" }
                }
            };

            var entity = RedirectRuleEntity.FromDomainModel(rule);

            entity.KeptQueryParamsJson.Should().NotBeNullOrEmpty();
            entity.KeptQueryParamsJson.Should().Contain("utm_");
        }

        [Fact]
        public void ToDomainModel_WithKeptQueryParamsJson_DeserializesFromJson()
        {
            var entity = new RedirectRuleEntity
            {
                RowKey = Guid.NewGuid().ToString("N"),
                Matcher = "/path",
                KeptQueryParamsJson = "[{\"keyPattern\":\"^utm_\",\"skipEncoding\":false}]"
            };

            var rule = entity.ToDomainModel();

            rule.KeptQueryParams.Should().HaveCount(1);
            rule.KeptQueryParams[0].KeyPattern.Should().Be("^utm_");
        }

        [Fact]
        public void FromDomainModel_WithStaticQueryParams_SerializesToJson()
        {
            var rule = new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = "/path",
                StaticQueryParams = new List<StaticQueryParam>
                {
                    new StaticQueryParam { Key = "source", Value = "redirect" }
                }
            };

            var entity = RedirectRuleEntity.FromDomainModel(rule);

            entity.StaticQueryParamsJson.Should().Contain("source");
        }

        [Fact]
        public void ToDomainModel_WithStaticQueryParamsJson_Deserializes()
        {
            var entity = new RedirectRuleEntity
            {
                RowKey = Guid.NewGuid().ToString("N"),
                Matcher = "/path",
                StaticQueryParamsJson = "[{\"key\":\"ref\",\"value\":\"test\",\"skipEncoding\":false}]"
            };

            var rule = entity.ToDomainModel();

            rule.StaticQueryParams.Should().HaveCount(1);
        }

        [Fact]
        public void FromDomainModel_WithSearchAndReplace_SerializesToJson()
        {
            var rule = new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = "/path",
                SearchAndReplace = new List<SearchAndReplaceEntry>
                {
                    new SearchAndReplaceEntry { Search = "old", Replace = "new" }
                }
            };

            var entity = RedirectRuleEntity.FromDomainModel(rule);

            entity.SearchAndReplaceJson.Should().Contain("old");
        }

        [Fact]
        public void ToDomainModel_WithSearchAndReplaceJson_Deserializes()
        {
            var entity = new RedirectRuleEntity
            {
                RowKey = Guid.NewGuid().ToString("N"),
                Matcher = "/path",
                SearchAndReplaceJson = "[{\"search\":\"old\",\"replace\":\"new\",\"caseSensitive\":false}]"
            };

            var rule = entity.ToDomainModel();

            rule.SearchAndReplace.Should().HaveCount(1);
            rule.SearchAndReplace[0].Search.Should().Be("old");
        }

        [Fact]
        public void RoundTrip_PreservesAllData()
        {
            var original = new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = "/round-trip",
                TargetUrl = "https://target.com",
                RedirectType = RedirectType.Domain,
                InfoText = "Round trip test",
                AutoRedirect = true,
                DiscardQueryParams = true,
                ForwardQueryParams = true,
                KeptQueryParams = new List<KeptQueryParam>
                {
                    new KeptQueryParam { KeyPattern = "^key$", TargetKey = "renamed" }
                },
                StaticQueryParams = new List<StaticQueryParam>
                {
                    new StaticQueryParam { Key = "static", Value = "val" }
                },
                SearchAndReplace = new List<SearchAndReplaceEntry>
                {
                    new SearchAndReplaceEntry { Search = "a", Replace = "b", CaseSensitive = true }
                },
                CreatedAt = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero)
            };

            var entity = RedirectRuleEntity.FromDomainModel(original);
            var restored = entity.ToDomainModel();

            restored.Id.Should().Be(original.Id);
            restored.Matcher.Should().Be(original.Matcher);
            restored.TargetUrl.Should().Be(original.TargetUrl);
            restored.RedirectType.Should().Be(original.RedirectType);
            restored.AutoRedirect.Should().Be(original.AutoRedirect);
            restored.KeptQueryParams.Should().HaveCount(1);
            restored.StaticQueryParams.Should().HaveCount(1);
            restored.SearchAndReplace.Should().HaveCount(1);
            restored.SearchAndReplace[0].CaseSensitive.Should().BeTrue();
        }

        [Theory]
        [InlineData("wildcard", RedirectType.Wildcard)]
        [InlineData("partial", RedirectType.Partial)]
        [InlineData("domain", RedirectType.Domain)]
        [InlineData("regex", RedirectType.Regex)]
        public void ToDomainModel_ParsesAllRedirectTypes(string typeString, RedirectType expected)
        {
            var entity = new RedirectRuleEntity
            {
                RowKey = Guid.NewGuid().ToString("N"),
                Matcher = "/path",
                RedirectType = typeString
            };

            var rule = entity.ToDomainModel();

            rule.RedirectType.Should().Be(expected);
        }

        [Fact]
        public void ToDomainModel_UnknownRedirectType_DefaultsToPartial()
        {
            var entity = new RedirectRuleEntity
            {
                RowKey = Guid.NewGuid().ToString("N"),
                Matcher = "/path",
                RedirectType = "unknown"
            };

            var rule = entity.ToDomainModel();

            rule.RedirectType.Should().Be(RedirectType.Partial);
        }

        [Fact]
        public void FromDomainModel_EmptyCollections_DoesNotSerializeJson()
        {
            var rule = new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = "/path"
            };

            var entity = RedirectRuleEntity.FromDomainModel(rule);

            entity.KeptQueryParamsJson.Should().BeNull();
            entity.StaticQueryParamsJson.Should().BeNull();
            entity.SearchAndReplaceJson.Should().BeNull();
        }
    }

    public class AppSettingsEntityTests
    {
        [Fact]
        public void FromDomainModel_MapsAllProperties()
        {
            var settings = new AppSettings
            {
                DefaultNewDomain = "https://test.com",
                NoMatchBehavior = "SmartSearch",
                AutoRedirect = true,
                HeaderTitle = "Test",
                SmartSearchUrl = "https://search.com/q=",
                CaseSensitiveLinkDetection = true,
                EnableFeedbackSurvey = true
            };

            var entity = AppSettingsEntity.FromDomainModel(settings);

            entity.DefaultNewDomain.Should().Be("https://test.com");
            entity.NoMatchBehavior.Should().Be("SmartSearch");
            entity.AutoRedirect.Should().BeTrue();
            entity.HeaderTitle.Should().Be("Test");
            entity.SmartSearchUrl.Should().Be("https://search.com/q=");
            entity.CaseSensitiveLinkDetection.Should().BeTrue();
            entity.EnableFeedbackSurvey.Should().BeTrue();
            entity.PartitionKey.Should().Be("Settings");
            entity.RowKey.Should().Be("default");
        }

        [Fact]
        public void ToDomainModel_MapsAllProperties()
        {
            var entity = new AppSettingsEntity
            {
                DefaultNewDomain = "https://mapped.com",
                NoMatchBehavior = "Return404",
                AutoRedirect = false,
                PopupMode = "active",
                ShowLinkQualityGauge = false
            };

            var settings = entity.ToDomainModel();

            settings.DefaultNewDomain.Should().Be("https://mapped.com");
            settings.NoMatchBehavior.Should().Be("Return404");
            settings.AutoRedirect.Should().BeFalse();
            settings.PopupMode.Should().Be("active");
            settings.ShowLinkQualityGauge.Should().BeFalse();
        }

        [Fact]
        public void RoundTrip_PreservesAllData()
        {
            var original = new AppSettings
            {
                DefaultNewDomain = "https://round.com",
                HeaderTitle = "Round Trip",
                SmartSearchSkipEncoding = true,
                EnableFeedbackComment = true
            };

            var entity = AppSettingsEntity.FromDomainModel(original);
            var restored = entity.ToDomainModel();

            restored.DefaultNewDomain.Should().Be(original.DefaultNewDomain);
            restored.HeaderTitle.Should().Be(original.HeaderTitle);
            restored.SmartSearchSkipEncoding.Should().Be(original.SmartSearchSkipEncoding);
            restored.EnableFeedbackComment.Should().Be(original.EnableFeedbackComment);
        }
    }

    public class GlobalRuleEntityTests
    {
        [Fact]
        public void FromDomainModel_MapsAllProperties()
        {
            var rule = new GlobalRule
            {
                Id = Guid.NewGuid(),
                Search = "old",
                Replace = "new",
                CaseSensitive = true,
                Priority = 5,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var entity = GlobalRuleEntity.FromDomainModel(rule);

            entity.RowKey.Should().Be(rule.Id.ToString("N"));
            entity.Search.Should().Be("old");
            entity.Replace.Should().Be("new");
            entity.CaseSensitive.Should().BeTrue();
            entity.Priority.Should().Be(5);
            entity.PartitionKey.Should().Be("GlobalRule");
        }

        [Fact]
        public void ToDomainModel_MapsAllProperties()
        {
            var id = Guid.NewGuid();
            var created = DateTimeOffset.UtcNow;

            var entity = new GlobalRuleEntity
            {
                RowKey = id.ToString("N"),
                Search = "find",
                Replace = "replace",
                CaseSensitive = false,
                Priority = 10,
                CreatedAt = created
            };

            var rule = entity.ToDomainModel();

            rule.Id.Should().Be(id);
            rule.Search.Should().Be("find");
            rule.Replace.Should().Be("replace");
            rule.CaseSensitive.Should().BeFalse();
            rule.Priority.Should().Be(10);
            rule.CreatedAt.Should().Be(created);
        }

        [Fact]
        public void RoundTrip_PreservesAllData()
        {
            var original = new GlobalRule
            {
                Id = Guid.NewGuid(),
                Search = "round",
                Replace = "trip",
                CaseSensitive = true,
                Priority = 3,
                CreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };

            var entity = GlobalRuleEntity.FromDomainModel(original);
            var restored = entity.ToDomainModel();

            restored.Id.Should().Be(original.Id);
            restored.Search.Should().Be(original.Search);
            restored.Replace.Should().Be(original.Replace);
            restored.CaseSensitive.Should().Be(original.CaseSensitive);
            restored.Priority.Should().Be(original.Priority);
        }
    }

    public class TrackingEntityTests
    {
        [Fact]
        public void ToDatePartition_FormatsCorrectly()
        {
            var timestamp = new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.Zero);

            var partition = TrackingEntity.ToDatePartition(timestamp);

            partition.Should().Be("2024-03-15");
        }

        [Fact]
        public void FromDomainModel_MapsAllProperties()
        {
            var entry = new TrackingEntry
            {
                Id = Guid.NewGuid(),
                OldUrl = "http://old.com/page",
                NewUrl = "https://new.com/page",
                Path = "/page",
                Timestamp = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero),
                UserAgent = "Mozilla/5.0",
                Referrer = "http://google.com",
                RuleId = "rule-123",
                MatchQuality = 100,
                Feedback = "OK",
                UserProposedUrl = null,
                RedirectStrategy = "rule"
            };

            var entity = TrackingEntity.FromDomainModel(entry);

            entity.RowKey.Should().Be(entry.Id.ToString("N"));
            entity.PartitionKey.Should().Be("2024-06-01");
            entity.OldUrl.Should().Be("http://old.com/page");
            entity.NewUrl.Should().Be("https://new.com/page");
            entity.Path.Should().Be("/page");
            entity.UserAgent.Should().Be("Mozilla/5.0");
            entity.MatchQuality.Should().Be(100);
            entity.Feedback.Should().Be("OK");
            entity.RedirectStrategy.Should().Be("rule");
        }

        [Fact]
        public void ToDomainModel_MapsAllProperties()
        {
            var id = Guid.NewGuid();
            var timestamp = DateTimeOffset.UtcNow;

            var entity = new TrackingEntity
            {
                RowKey = id.ToString("N"),
                OldUrl = "http://old.com",
                NewUrl = "https://new.com",
                Path = "/path",
                EntryTimestamp = timestamp,
                MatchQuality = 75,
                Feedback = "NOK",
                UserProposedUrl = "https://correct.com",
                RedirectStrategy = "smart-search"
            };

            var entry = entity.ToDomainModel();

            entry.Id.Should().Be(id);
            entry.OldUrl.Should().Be("http://old.com");
            entry.MatchQuality.Should().Be(75);
            entry.Feedback.Should().Be("NOK");
            entry.UserProposedUrl.Should().Be("https://correct.com");
            entry.RedirectStrategy.Should().Be("smart-search");
        }

        [Fact]
        public void RoundTrip_PreservesAllData()
        {
            var original = new TrackingEntry
            {
                Id = Guid.NewGuid(),
                OldUrl = "http://old.com/page",
                NewUrl = "https://new.com/page",
                Path = "/page",
                Timestamp = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero),
                UserAgent = "TestAgent",
                Referrer = "http://ref.com",
                RuleId = "abc",
                MatchQuality = 50,
                Feedback = "auto-redirect",
                RedirectStrategy = "rule"
            };

            var entity = TrackingEntity.FromDomainModel(original);
            var restored = entity.ToDomainModel();

            restored.Id.Should().Be(original.Id);
            restored.OldUrl.Should().Be(original.OldUrl);
            restored.NewUrl.Should().Be(original.NewUrl);
            restored.Path.Should().Be(original.Path);
            restored.UserAgent.Should().Be(original.UserAgent);
            restored.MatchQuality.Should().Be(original.MatchQuality);
            restored.Feedback.Should().Be(original.Feedback);
        }
    }
}