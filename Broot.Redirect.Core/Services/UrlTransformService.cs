using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Broot.Redirect.Core.Services
{
    public sealed class UrlTransformService : IUrlTransformService
    {
        private readonly IGlobalRuleCacheService _globalRuleCache;

        public UrlTransformService(IGlobalRuleCacheService globalRuleCache)
        {
            _globalRuleCache = globalRuleCache;
        }

        public string ResolveTargetUrl(string originalUrl, RedirectRule rule, string defaultNewDomain)
        {
            var cleanDomain = (defaultNewDomain ?? "https://thisisthenewurl.com/").TrimEnd('/');
            var oldPath = ExtractPath(originalUrl);

            var currentUrl = rule.RedirectType switch
            {
                RedirectType.Partial => ResolvePartial(oldPath, rule, cleanDomain),
                RedirectType.Wildcard => ResolveWildcard(oldPath, originalUrl, rule, cleanDomain),
                RedirectType.Domain => ResolveDomain(originalUrl, rule, cleanDomain),
                _ => GenerateNewUrl(originalUrl, cleanDomain)
            };

            foreach (var entry in rule.SearchAndReplace)
            {
                currentUrl = ApplySearchAndReplace(currentUrl, entry);
            }

            var globalRules = _globalRuleCache.GetAll();

            foreach (var globalRule in globalRules)
            {
                currentUrl = ApplyGlobalRule(currentUrl, globalRule);
            }

            if (rule.ForwardQueryParams)
            {
                currentUrl = ForwardOriginalQueryParams(originalUrl, currentUrl);
            }
            else if (rule.DiscardQueryParams && rule.KeptQueryParams.Count > 0)
            {
                currentUrl = ApplyKeptQueryParams(originalUrl, currentUrl, rule.KeptQueryParams);
            }

            if (rule.StaticQueryParams.Count > 0)
            {
                currentUrl = ApplyStaticQueryParams(currentUrl, rule.StaticQueryParams);
            }

            return currentUrl;
        }

        private static string ResolvePartial(string oldPath, RedirectRule rule, string cleanDomain)
        {
            var workingOldPath = oldPath;

            if (rule.DiscardQueryParams)
            {
                workingOldPath = StripQueryParams(workingOldPath);
            }

            var isDomainMatcher = !rule.Matcher.StartsWith('/');

            if (isDomainMatcher)
            {
                var targetUrl = rule.TargetUrl ?? string.Empty;
                string targetBase;

                if (targetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    targetBase = targetUrl.TrimEnd('/');
                }
                else
                {
                    var targetPath = targetUrl.TrimEnd('/');
                    targetBase = cleanDomain + (targetPath.StartsWith('/') ? targetPath : "/" + targetPath);
                }

                var normalizedOldPath = workingOldPath.StartsWith('/') ? workingOldPath : "/" + workingOldPath;

                return targetBase + normalizedOldPath;
            }

            var cleanMatcher = rule.Matcher.TrimEnd('/');
            var cleanTarget = rule.TargetUrl.Trim('/');

            string decodedOldPath;
            string decodedMatcher;

            try { decodedOldPath = Uri.UnescapeDataString(workingOldPath); }
            catch { decodedOldPath = workingOldPath; }

            try { decodedMatcher = Uri.UnescapeDataString(cleanMatcher); }
            catch { decodedMatcher = cleanMatcher; }

            string newPath;

            if (decodedOldPath.StartsWith(decodedMatcher, StringComparison.OrdinalIgnoreCase))
            {
                var remaining = decodedOldPath[decodedMatcher.Length..];

                newPath = "/" + cleanTarget + remaining;
            }
            else
            {
                var matcherIndex = decodedOldPath.IndexOf(decodedMatcher, StringComparison.OrdinalIgnoreCase);

                if (matcherIndex >= 0)
                {
                    var beforeMatch = decodedOldPath[..matcherIndex];
                    var afterMatch = decodedOldPath[(matcherIndex + decodedMatcher.Length)..];

                    newPath = beforeMatch + "/" + cleanTarget + afterMatch;
                }
                else
                {
                    newPath = "/" + cleanTarget;
                }
            }

            return cleanDomain + newPath;
        }

        private static string ResolveWildcard(
            string oldPath,
            string originalUrl,
            RedirectRule rule,
            string cleanDomain)
        {
            var workingOldPath = oldPath;

            if (rule.DiscardQueryParams)
            {
                workingOldPath = StripQueryParams(workingOldPath);
            }

            var isDomainMatcher = !rule.Matcher.StartsWith('/');

            if (isDomainMatcher)
            {
                var targetDomain = !string.IsNullOrEmpty(rule.TargetUrl) ? rule.TargetUrl : cleanDomain;

                return GenerateNewUrl(originalUrl, targetDomain);
            }

            var cleanMatcher = rule.Matcher.TrimEnd('*');
            var rawTarget = rule.TargetUrl ?? string.Empty;

            string decodedOldPath;
            string decodedMatcher;

            try { decodedOldPath = Uri.UnescapeDataString(workingOldPath); }
            catch { decodedOldPath = workingOldPath; }

            try { decodedMatcher = Uri.UnescapeDataString(cleanMatcher); }
            catch { decodedMatcher = cleanMatcher; }

            if (decodedOldPath.StartsWith(decodedMatcher, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = decodedOldPath[decodedMatcher.Length..];
                var targetBase = rawTarget;

                if (cleanMatcher.EndsWith('/') && !targetBase.EndsWith('/'))
                {
                    targetBase += "/";
                }

                if (!targetBase.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && !targetBase.StartsWith('/')
                    && targetBase.Length > 0)
                {
                    targetBase = "/" + targetBase;
                }

                if (targetBase.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return targetBase + suffix;
                }

                return cleanDomain + targetBase + suffix;
            }

            return cleanDomain + workingOldPath;
        }

        private static string ResolveDomain(string originalUrl, RedirectRule rule, string cleanDomain)
        {
            var targetDomain = !string.IsNullOrEmpty(rule.TargetUrl) ? rule.TargetUrl : cleanDomain;

            return GenerateNewUrl(originalUrl, targetDomain);
        }

        /// <summary>
        /// Applies a single global search-and-replace rule to the URL.
        /// Uses Regex.Escape on the Search value for literal string matching,
        /// with case sensitivity controlled by the CaseSensitive flag.
        /// Same pattern as per-rule ApplySearchAndReplace.
        /// </summary>
        internal static string ApplyGlobalRule(string url, GlobalRule globalRule)
        {
            if (string.IsNullOrEmpty(globalRule.Search))
            {
                return url;
            }

            try
            {
                var escapedSearch = Regex.Escape(globalRule.Search);
                var options = globalRule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

                return Regex.Replace(url, escapedSearch, globalRule.Replace ?? string.Empty, options);
            }
            catch
            {
                return url;
            }
        }

        private static string ForwardOriginalQueryParams(string originalUrl, string currentUrl)
        {
            if (!Uri.TryCreate(EnsureAbsoluteUrl(originalUrl), UriKind.Absolute, out var originalUri))
            {
                return currentUrl;
            }

            if (string.IsNullOrEmpty(originalUri.Query) || originalUri.Query == "?")
            {
                return currentUrl;
            }

            return AppendQueryString(currentUrl, originalUri.Query);
        }

        private static string ApplyKeptQueryParams(
            string originalUrl,
            string currentUrl,
            IReadOnlyList<KeptQueryParam> keptRules)
        {
            if (!Uri.TryCreate(EnsureAbsoluteUrl(originalUrl), UriKind.Absolute, out var originalUri))
            {
                return currentUrl;
            }

            var queryCollection = HttpUtility.ParseQueryString(originalUri.Query);
            var parts = new List<string>();
            var usedIndices = new HashSet<int>();

            foreach (var keptRule in keptRules)
            {
                if (string.IsNullOrEmpty(keptRule.KeyPattern))
                {
                    continue;
                }

                Regex keyRegex;

                try { keyRegex = new Regex(keptRule.KeyPattern, RegexOptions.None, TimeSpan.FromSeconds(1)); }
                catch { continue; }

                Regex? valueRegex = null;

                if (!string.IsNullOrEmpty(keptRule.ValuePattern))
                {
                    try { valueRegex = new Regex(keptRule.ValuePattern, RegexOptions.None, TimeSpan.FromSeconds(1)); }
                    catch { continue; }
                }

                var allKeys = queryCollection.AllKeys;

                for (var index = 0; index < allKeys.Length; index++)
                {
                    if (usedIndices.Contains(index))
                    {
                        continue;
                    }

                    var key = allKeys[index];

                    if (key == null)
                    {
                        continue;
                    }

                    if (!keyRegex.IsMatch(key))
                    {
                        continue;
                    }

                    var value = queryCollection[key] ?? string.Empty;

                    if (valueRegex != null && !valueRegex.IsMatch(value))
                    {
                        continue;
                    }

                    var finalKey = !string.IsNullOrWhiteSpace(keptRule.TargetKey)
                        ? keptRule.TargetKey
                        : key;

                    var encodedKey = Uri.EscapeDataString(finalKey);
                    var encodedValue = keptRule.SkipEncoding ? value : Uri.EscapeDataString(value);

                    parts.Add($"{encodedKey}={encodedValue}");
                    usedIndices.Add(index);
                }
            }

            if (parts.Count == 0)
            {
                return currentUrl;
            }

            return AppendQueryString(currentUrl, "?" + string.Join("&", parts));
        }

        private static string ApplyStaticQueryParams(
            string currentUrl,
            IReadOnlyList<StaticQueryParam> staticParams)
        {
            var parts = new List<string>();

            foreach (var parameter in staticParams)
            {
                if (string.IsNullOrEmpty(parameter.Key))
                {
                    continue;
                }

                var encodedKey = Uri.EscapeDataString(parameter.Key);
                var encodedValue = parameter.SkipEncoding
                    ? (parameter.Value ?? string.Empty)
                    : Uri.EscapeDataString(parameter.Value ?? string.Empty);

                parts.Add($"{encodedKey}={encodedValue}");
            }

            if (parts.Count == 0)
            {
                return currentUrl;
            }

            return AppendQueryString(currentUrl, "?" + string.Join("&", parts));
        }

        internal static string ApplySearchAndReplace(string url, SearchAndReplaceEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Search))
            {
                return url;
            }

            try
            {
                var escapedSearch = Regex.Escape(entry.Search);
                var options = entry.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

                return Regex.Replace(url, escapedSearch, entry.Replace ?? string.Empty, options);
            }
            catch
            {
                return url;
            }
        }

        internal static string ExtractPath(string url)
        {
            if (Uri.TryCreate(EnsureAbsoluteUrl(url), UriKind.Absolute, out var uri))
            {
                return uri.PathAndQuery + uri.Fragment;
            }

            var match = Regex.Match(url, @"^https?://[^/]+(/.*)$");

            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return url.StartsWith('/') ? url : "/" + url;
        }

        internal static string GenerateNewUrl(string oldUrl, string? newDomain)
        {
            var url = oldUrl;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url[7..];
            }

            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            var targetDomain = (newDomain ?? "https://thisisthenewurl.com/").TrimEnd('/');

            url = Regex.Replace(url, @"^https?://[^/]+", targetDomain);

            return url;
        }

        internal static string AppendQueryString(string url, string queryString)
        {
            if (string.IsNullOrEmpty(queryString))
            {
                return url;
            }

            var hashIndex = url.IndexOf('#');
            string baseUrl;
            string fragment;

            if (hashIndex >= 0)
            {
                baseUrl = url[..hashIndex];
                fragment = url[hashIndex..];
            }
            else
            {
                baseUrl = url;
                fragment = string.Empty;
            }

            var queryPart = queryString.StartsWith('?') ? queryString[1..] : queryString;

            if (baseUrl.Contains('?'))
            {
                return $"{baseUrl}&{queryPart}{fragment}";
            }

            return $"{baseUrl}?{queryPart}{fragment}";
        }

        private static string StripQueryParams(string path)
        {
            if (!path.Contains('?'))
            {
                return path;
            }

            var queryIndex = path.IndexOf('?');
            var hashIndex = path.IndexOf('#');

            if (hashIndex >= 0 && hashIndex > queryIndex)
            {
                return path[..queryIndex] + path[hashIndex..];
            }

            return path[..queryIndex];
        }

        private static string EnsureAbsoluteUrl(string url)
        {
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            return "http://dummy.com" + (url.StartsWith('/') ? url : "/" + url);
        }
    }
}
