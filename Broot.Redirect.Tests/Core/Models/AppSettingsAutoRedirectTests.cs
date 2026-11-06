using Broot.Redirect.Core.Models;
using FluentAssertions;
using Xunit;

namespace Broot.Redirect.Tests.Core.Models
{
    public class AppSettingsAutoRedirectTests
    {
        [Theory]
        [InlineData(RuleSource.Manual)]
        [InlineData(RuleSource.Import)]
        [InlineData(RuleSource.QmPro)]
        [InlineData(RuleSource.AreaList)]
        [InlineData(RuleSource.Unknown)]
        public void GetAutoRedirectMode_Default_IsPerRule(RuleSource source)
        {
            var settings = new AppSettings();

            settings.GetAutoRedirectMode(source).Should().Be(AutoRedirectMode.PerRule);
        }

        [Theory]
        [InlineData(RuleSource.Manual)]
        [InlineData(RuleSource.Import)]
        [InlineData(RuleSource.QmPro)]
        [InlineData(RuleSource.AreaList)]
        [InlineData(RuleSource.Unknown)]
        public void ResolveAutoRedirect_PerRule_UsesRuleFlag(RuleSource source)
        {
            var settings = new AppSettings();

            settings.ResolveAutoRedirect(CreateRule(source, autoRedirect: true)).Should().BeTrue();
            settings.ResolveAutoRedirect(CreateRule(source, autoRedirect: false)).Should().BeFalse();
        }

        [Fact]
        public void ResolveAutoRedirect_Always_OverridesDisabledRuleFlag()
        {
            var settings = new AppSettings { AutoRedirectQmPro = AutoRedirectMode.Always };

            settings.ResolveAutoRedirect(CreateRule(RuleSource.QmPro, autoRedirect: false)).Should().BeTrue();
        }

        [Fact]
        public void ResolveAutoRedirect_Never_OverridesEnabledRuleFlag()
        {
            var settings = new AppSettings { AutoRedirectManual = AutoRedirectMode.Never };

            settings.ResolveAutoRedirect(CreateRule(RuleSource.Manual, autoRedirect: true)).Should().BeFalse();
        }

        [Fact]
        public void ResolveAutoRedirect_Always_OnlyAffectsConfiguredSource()
        {
            var settings = new AppSettings { AutoRedirectQmPro = AutoRedirectMode.Always };

            settings.ResolveAutoRedirect(CreateRule(RuleSource.Import, autoRedirect: false)).Should().BeFalse();
            settings.ResolveAutoRedirect(CreateRule(RuleSource.Manual, autoRedirect: false)).Should().BeFalse();
            settings.ResolveAutoRedirect(CreateRule(RuleSource.AreaList, autoRedirect: false)).Should().BeFalse();
            settings.ResolveAutoRedirect(CreateRule(RuleSource.Unknown, autoRedirect: false)).Should().BeFalse();
        }

        [Fact]
        public void ResolveAutoRedirect_Never_OnlyAffectsConfiguredSource()
        {
            var settings = new AppSettings { AutoRedirectImport = AutoRedirectMode.Never };

            settings.ResolveAutoRedirect(CreateRule(RuleSource.Import, autoRedirect: true)).Should().BeFalse();
            settings.ResolveAutoRedirect(CreateRule(RuleSource.Manual, autoRedirect: true)).Should().BeTrue();
            settings.ResolveAutoRedirect(CreateRule(RuleSource.QmPro, autoRedirect: true)).Should().BeTrue();
            settings.ResolveAutoRedirect(CreateRule(RuleSource.AreaList, autoRedirect: true)).Should().BeTrue();
        }

        [Fact]
        public void GetAutoRedirectMode_UnknownSource_StaysPerRuleWhenOtherSourcesAreConfigured()
        {
            var settings = new AppSettings
            {
                AutoRedirectManual = AutoRedirectMode.Always,
                AutoRedirectImport = AutoRedirectMode.Always,
                AutoRedirectQmPro = AutoRedirectMode.Never,
                AutoRedirectAreaList = AutoRedirectMode.Never
            };

            settings.GetAutoRedirectMode(RuleSource.Unknown).Should().Be(AutoRedirectMode.PerRule);
        }

        [Fact]
        public void GetAutoRedirectMode_MapsEverySourceToItsOwnSetting()
        {
            var settings = new AppSettings
            {
                AutoRedirectManual = AutoRedirectMode.Always,
                AutoRedirectImport = AutoRedirectMode.Never,
                AutoRedirectQmPro = AutoRedirectMode.Always,
                AutoRedirectAreaList = AutoRedirectMode.Never
            };

            settings.GetAutoRedirectMode(RuleSource.Manual).Should().Be(AutoRedirectMode.Always);
            settings.GetAutoRedirectMode(RuleSource.Import).Should().Be(AutoRedirectMode.Never);
            settings.GetAutoRedirectMode(RuleSource.QmPro).Should().Be(AutoRedirectMode.Always);
            settings.GetAutoRedirectMode(RuleSource.AreaList).Should().Be(AutoRedirectMode.Never);
        }

        private static RedirectRule CreateRule(RuleSource source, bool autoRedirect)
        {
            return new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = "/old-page",
                TargetUrl = "https://new.example.com/page",
                Source = source,
                AutoRedirect = autoRedirect
            };
        }
    }
}
