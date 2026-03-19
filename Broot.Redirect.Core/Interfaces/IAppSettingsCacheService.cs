using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Interfaces
{
    /// <summary>
    /// In-memory cache for application settings.
    /// Read operations return the cached instance; write operations go through
    /// Table Storage first, then update the cache.
    /// Warmed up on startup alongside the rule cache.
    /// </summary>
    /// 
    public interface IAppSettingsCacheService
    {
        /// <summary>
        /// Whether the cache has been populated from the backing store.
        /// False during startup before the warmup service completes.
        /// </summary>
        /// 
        bool IsWarmedUp { get; }

        /// <summary>
        /// Returns the current cached settings.
        /// Never returns null after warmup -- the warmup service seeds defaults if no row exists.
        /// </summary>
        /// 
        AppSettings GetSettings();

        /// <summary>
        /// Populates the cache from the loaded settings.
        /// Called once on startup by the warmup service.
        /// </summary>
        /// 
        void Initialize(AppSettings settings);

        /// <summary>
        /// Writes the updated settings to Table Storage, then replaces the cached instance.
        /// Returns the persisted settings.
        /// </summary>
        /// 
        Task<AppSettings> UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
    }
}
