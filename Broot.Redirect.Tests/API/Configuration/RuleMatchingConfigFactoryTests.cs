using Broot.Redirect.API.Configuration;
using Broot.Redirect.Core.Models;
using FluentAssertions;
using Xunit;

namespace Broot.Redirect.Tests.API.Configuration
{
    public class RuleMatchingConfigFactoryTests
    {
        [Fact]
        public void Create_DefaultOptions_MapsAllProperties()
        {
            var options = new BrootRedirectOptions();

            var config = RuleMatchingConfigFactory.Create(options);

            config.CaseSensitivePath.Should().Be(options.CaseSensitivePath);
            config.CaseSensitiveQuery.Should().Be(options.CaseSensitiveQuery);
            config.TrailingSlashPolicy.Should().Be(TrailingSlashPolicy.Ignore);
            config.WeightPathSegment.Should().Be(options.WeightPathSegment);
            config.WeightQueryPair.Should().Be(options.WeightQueryPair);
            config.PenaltyWildcard.Should().Be(options.PenaltyWildcard);
            config.BonusExactMatch.Should().Be(options.BonusExactMatch);
        }

        [Fact]
        public void Create_ValidTrailingSlashPolicy_ParsesCorrectly()
        {
            var options = new BrootRedirectOptions { TrailingSlashPolicy = "Strict" };

            var config = RuleMatchingConfigFactory.Create(options);

            config.TrailingSlashPolicy.Should().Be(TrailingSlashPolicy.Strict);
        }

        [Fact]
        public void Create_CaseInsensitiveTrailingSlashPolicy_ParsesCorrectly()
        {
            var options = new BrootRedirectOptions { TrailingSlashPolicy = "strict" };

            var config = RuleMatchingConfigFactory.Create(options);

            config.TrailingSlashPolicy.Should().Be(TrailingSlashPolicy.Strict);
        }

        [Fact]
        public void Create_InvalidTrailingSlashPolicy_DefaultsToIgnore()
        {
            var options = new BrootRedirectOptions { TrailingSlashPolicy = "completely_invalid_value" };

            var config = RuleMatchingConfigFactory.Create(options);

            config.TrailingSlashPolicy.Should().Be(TrailingSlashPolicy.Ignore);
        }

        [Fact]
        public void Create_EmptyTrailingSlashPolicy_DefaultsToIgnore()
        {
            var options = new BrootRedirectOptions { TrailingSlashPolicy = "" };

            var config = RuleMatchingConfigFactory.Create(options);

            config.TrailingSlashPolicy.Should().Be(TrailingSlashPolicy.Ignore);
        }

        [Fact]
        public void Create_CustomWeights_MapsCorrectly()
        {
            var options = new BrootRedirectOptions
            {
                WeightPathSegment = 20,
                WeightQueryPair = 10,
                PenaltyWildcard = 5,
                BonusExactMatch = 100,
                CaseSensitivePath = true,
                CaseSensitiveQuery = true
            };

            var config = RuleMatchingConfigFactory.Create(options);

            config.WeightPathSegment.Should().Be(20);
            config.WeightQueryPair.Should().Be(10);
            config.PenaltyWildcard.Should().Be(5);
            config.BonusExactMatch.Should().Be(100);
            config.CaseSensitivePath.Should().BeTrue();
            config.CaseSensitiveQuery.Should().BeTrue();
        }
    }
}
