using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Infrastructure.Cache;
using Broot.Redirect.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddSmartRedirectInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<TableStorageOptions>(configuration.GetSection("AzureTableStorage"));

            services.Configure<CacheOptions>(options =>
            {
                var section = configuration.GetSection("SmartRedirect");

                options.CaseSensitiveMatching = section.GetValue<bool>("CaseSensitivePath", false);
                options.TrailingSlashPolicy = section.GetValue<string>("TrailingSlashPolicy", "ignore") ?? "ignore";

                var regexTimeoutSeconds = section.GetValue<int>("RegexMatchTimeoutSeconds", 1);

                options.RegexMatchTimeout = TimeSpan.FromSeconds(regexTimeoutSeconds);
            });

            services.AddSingleton<TableStorageRuleRepository>();
            services.AddSingleton<IRedirectRuleRepository>(provider => provider.GetRequiredService<TableStorageRuleRepository>());

            services.AddSingleton<IRuleCacheService, RuleCacheService>();

            services.AddSingleton<TableStorageAppSettingsRepository>();
            services.AddSingleton<IAppSettingsRepository>(provider => provider.GetRequiredService<TableStorageAppSettingsRepository>());

            services.AddSingleton<IAppSettingsCacheService, AppSettingsCacheService>();

            services.AddSingleton<TableStorageGlobalRuleRepository>();
            services.AddSingleton<IGlobalRuleRepository>(provider => provider.GetRequiredService<TableStorageGlobalRuleRepository>());

            services.AddSingleton<IGlobalRuleCacheService, GlobalRuleCacheService>();

            services.AddSingleton<TableStorageTrackingRepository>();
            services.AddSingleton<ITrackingRepository>(provider => provider.GetRequiredService<TableStorageTrackingRepository>());

            services.AddHostedService<CacheWarmupService>();

            var retentionDays = configuration.GetSection("SmartRedirect").GetValue<int>("TrackingRetentionDays", 30);

            services.AddSingleton(provider => new TrackingCleanupService(
                provider.GetRequiredService<ITrackingRepository>(),
                retentionDays,
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TrackingCleanupService>>()));

            services.AddHostedService(provider => provider.GetRequiredService<TrackingCleanupService>());

            return services;
        }
    }
}