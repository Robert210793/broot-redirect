using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Services
{
    public sealed class SmartSearchService : ISmartSearchService
    {
        /// <summary>
        /// Regex match timeout to prevent ReDoS from user-supplied patterns.
        /// </summary>
        /// 
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        public string? BuildSearchUrl(string path, AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.SmartSearchUrl))
            {
                return null;
            }

            var searchTerm = ExtractSearchTerm(path, settings.SmartSearchRegex);

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return null;
            }

            if (!settings.SmartSearchSkipEncoding)
            {
                searchTerm = Uri.EscapeDataString(searchTerm);
            }

            return settings.SmartSearchUrl + searchTerm;
        }

        /// <summary>
        /// Extracts a search term from the path.
        /// If a regex is configured, uses the first capture group.
        /// Otherwise falls back to extracting and cleaning the last path segment.
        /// </summary>
        /// 
        internal static string? ExtractSearchTerm(string path, string? regexPattern)
        {
            if (!string.IsNullOrWhiteSpace(regexPattern))
            {
                var regexTerm = ExtractViaRegex(path, regexPattern);

                if (!string.IsNullOrWhiteSpace(regexTerm))
                {
                    return regexTerm;
                }
            }

            return ExtractLastPathSegment(path);
        }

        /// <summary>
        /// Attempts to extract a search term using the configured regex pattern.
        /// Returns the first capture group match, or null if no match.
        /// </summary>
        /// 
        private static string? ExtractViaRegex(string path, string regexPattern)
        {
            try
            {
                var regex = new Regex(regexPattern, RegexOptions.None, RegexTimeout);

                var match = regex.Match(path);

                if (match.Success && match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                {
                    try
                    {
                        return Uri.UnescapeDataString(match.Groups[1].Value);
                    }
                    catch
                    {
                        return match.Groups[1].Value;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// Extracts the last path segment and cleans it for use as a search term.
        /// Strips query params and fragment, decodes percent-encoding,
        /// replaces hyphens and underscores with spaces, removes file extensions.
        /// </summary>
        /// 
        internal static string? ExtractLastPathSegment(string path)
        {
            try
            {
                var uri = new Uri("http://dummy.com" + (path.StartsWith('/') ? path : "/" + path));

                var pathname = uri.AbsolutePath;

                var segments = pathname.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (segments.Length == 0)
                {
                    return null;
                }

                var lastSegment = segments[segments.Length - 1];

                try
                {
                    lastSegment = Uri.UnescapeDataString(lastSegment);
                }
                catch
                {
                }

                var extensionIndex = lastSegment.LastIndexOf('.');

                if (extensionIndex > 0)
                {
                    lastSegment = lastSegment[..extensionIndex];
                }

                lastSegment = lastSegment.Replace('-', ' ').Replace('_', ' ');

                while (lastSegment.Contains("  "))
                {
                    lastSegment = lastSegment.Replace("  ", " ");
                }

                var trimmed = lastSegment.Trim();

                return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
            }
            catch
            {
                return null;
            }
        }
    }
}
