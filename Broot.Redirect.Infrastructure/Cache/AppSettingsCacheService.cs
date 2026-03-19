using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Infrastructure.Cache
{
    public class AppSettingsCacheService : IAppSettingsCacheService
    {
        private readonly IAppSettingsRepository _repository;
        private readonly ILogger<AppSettingsCacheService> _logger;

        private volatile AppSettings _cachedSettings = new();
        private volatile bool _isWarmedUp;

        public AppSettingsCacheService(
            IAppSettingsRepository repository,
            ILogger<AppSettingsCacheService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public bool IsWarmedUp => _isWarmedUp;

        public AppSettings GetSettings()
        {
            return _cachedSettings;
        }

        public void Initialize(AppSettings settings)
        {
            _cachedSettings = settings;
            _isWarmedUp = true;

            _logger.LogInformation("AppSettings cache initialized.");
        }

        public async Task<AppSettings> UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            await _repository.SaveAsync(settings, cancellationToken);

            _cachedSettings = settings;

            _logger.LogInformation("AppSettings cache updated.");

            return settings;
        }
    }
}
