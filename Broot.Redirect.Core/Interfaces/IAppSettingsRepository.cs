using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Interfaces
{
    /// <summary>
    /// Repository interface for AppSettings persistence.
    /// Operates against a single-row Azure Table Storage table.
    /// The cache layer sits above this and delegates writes through here.
    /// </summary>
    /// 
    public interface IAppSettingsRepository
    {
        /// <summary>
        /// Loads the settings row from the backing store.
        /// Returns null if no row exists (first boot before seeding).
        /// </summary>
        /// 
        Task<AppSettings?> GetAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Upserts the settings row. Creates if missing, replaces if existing.
        /// </summary>
        /// 
        Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    }
}
