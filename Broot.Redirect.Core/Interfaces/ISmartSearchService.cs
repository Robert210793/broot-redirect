using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Interfaces
{
    /// <summary>
    /// Builds smart search fallback URLs when no redirect rule matches.
    /// Extracts a search term from the request path and appends it
    /// to the configured SmartSearchUrl from AppSettings.
    /// </summary>
    /// 
    public interface ISmartSearchService
    {
        /// <summary>
        /// Builds a search URL from the incoming path using the settings configuration.
        /// Returns null if SmartSearchUrl is not configured or no search term can be extracted.
        /// </summary>
        /// <param name="path">The original request path (e.g. "/old-section/some-article-name?key=value").</param>
        /// <param name="settings">Current runtime settings with SmartSearchUrl, SmartSearchRegex, SmartSearchSkipEncoding.</param>
        /// 
        string? BuildSearchUrl(string path, AppSettings settings);
    }
}
