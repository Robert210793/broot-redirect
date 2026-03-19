using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Broot.Redirect.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Infrastructure.Cache
{
    public class CacheWarmupService : IHostedService
    {
        private readonly TableStorageRuleRepository _ruleRepository;
        private readonly TableStorageAppSettingsRepository _settingsRepository;
        private readonly TableStorageGlobalRuleRepository _globalRuleRepository;
        private readonly TableStorageTrackingRepository _trackingRepository;
        private readonly IRuleCacheService _ruleCacheService;
        private readonly IAppSettingsCacheService _settingsCacheService;
        private readonly IGlobalRuleCacheService _globalRuleCacheService;
        private readonly ILogger<CacheWarmupService> _logger;

        public CacheWarmupService(
            TableStorageRuleRepository ruleRepository,
            TableStorageAppSettingsRepository settingsRepository,
            TableStorageGlobalRuleRepository globalRuleRepository,
            TableStorageTrackingRepository trackingRepository,
            IRuleCacheService ruleCacheService,
            IAppSettingsCacheService settingsCacheService,
            IGlobalRuleCacheService globalRuleCacheService,
            ILogger<CacheWarmupService> logger)
        {
            _ruleRepository = ruleRepository;
            _settingsRepository = settingsRepository;
            _globalRuleRepository = globalRuleRepository;
            _trackingRepository = trackingRepository;
            _ruleCacheService = ruleCacheService;
            _settingsCacheService = settingsCacheService;
            _globalRuleCacheService = globalRuleCacheService;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Cache warmup starting...");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            await _ruleRepository.EnsureTableExistsAsync(cancellationToken);

            await _settingsRepository.EnsureTableExistsAsync(cancellationToken);

            await _globalRuleRepository.EnsureTableExistsAsync(cancellationToken);

            await _trackingRepository.EnsureTableExistsAsync(cancellationToken);

            var rules = await _ruleRepository.GetAllAsync(cancellationToken);

            _ruleCacheService.Initialize(rules);

            var settings = await _settingsRepository.GetAsync(cancellationToken);

            if (settings == null)
            {
                _logger.LogInformation("No AppSettings row found. Seeding defaults...");

                settings = new AppSettings();

                await _settingsRepository.SaveAsync(settings, cancellationToken);
            }

            _settingsCacheService.Initialize(settings);

            var globalRules = await _globalRuleRepository.GetAllAsync(cancellationToken);

            _globalRuleCacheService.Initialize(globalRules);

            stopwatch.Stop();

            _logger.LogInformation(
                "Cache warmup completed in {ElapsedMs}ms. {RuleCount} rules, {GlobalRuleCount} global rules loaded, settings initialized.",
                stopwatch.ElapsedMilliseconds,
                rules.Count,
                globalRules.Count);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}