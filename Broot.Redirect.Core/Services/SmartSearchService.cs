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
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        private string? _cachedPattern;
        private Regex? _cachedRegex;

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

        internal string? ExtractSearchTerm(string path, string? regexPattern)
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

        private string? ExtractViaRegex(string path, string regexPattern)
        {
            try
            {
                if (_cachedRegex == null || _cachedPattern != regexPattern)
                {
                    _cachedPattern = regexPattern;
                    _cachedRegex = new Regex(regexPattern, RegexOptions.Compiled, RegexTimeout);
                }

                var regex = _cachedRegex;

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
