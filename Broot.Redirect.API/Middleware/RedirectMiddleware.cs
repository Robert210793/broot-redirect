using Microsoft.Extensions.Options;
using Broot.Redirect.API.Configuration;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;

namespace Broot.Redirect.API.Middleware
{
    public class RedirectMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RedirectMiddleware> _logger;

        /// <summary>
        /// Angular SPA routes that must not be processed as redirect paths.
        /// Without this, visiting /rules or /settings would trigger a redirect
        /// to the default domain or return 404 instead of loading the SPA.
        /// </summary>
        private static readonly string[] SpaRoutes =
        {
            "/login",
            "/rules",
            "/global-rules",
            "/settings",
            "/import",
            "/stats"
        };

        public RedirectMiddleware(RequestDelegate next, ILogger<RedirectMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            IRuleCacheService ruleCacheService,
            IRuleMatchingService ruleMatchingService,
            IUrlTransformService urlTransformService,
            IAppSettingsCacheService settingsCache,
            ISmartSearchService smartSearchService,
            IOptions<BrootRedirectOptions> options)
        {
            var path = context.Request.Path.Value ?? "/";
            var queryString = context.Request.QueryString.Value ?? string.Empty;
            var fullPath = path + queryString;

            if (ShouldSkip(path))
            {
                await _next(context);

                return;
            }

            var appSettings = settingsCache.GetSettings();

            var staticOptions = options.Value;

            var matchingConfig = RuleMatchingConfigFactory.Create(staticOptions);

            var allRules = ruleCacheService.GetAll();

            var processedRules = allRules
                .Select(rule => ruleMatchingService.PreprocessRule(rule, matchingConfig))
                .ToList();

            var matchResult = ruleMatchingService.FindMatchingRule(fullPath, processedRules, matchingConfig);

            if (matchResult != null)
            {
                var rule = matchResult.Rule;

                if (appSettings.AutoRedirect && rule.AutoRedirect)
                {
                    var targetUrl = urlTransformService.ResolveTargetUrl(
                        fullPath,
                        rule,
                        appSettings.DefaultNewDomain);

                    if (!string.IsNullOrEmpty(targetUrl))
                    {
                        _logger.LogInformation(
                            "Auto-redirect: {SourcePath} -> {TargetUrl} (Rule: {RuleId})",
                            path,
                            targetUrl,
                            rule.Id);

                        context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                        context.Response.Headers.Location = targetUrl;

                        return;
                    }
                }

                _logger.LogDebug(
                    "Info page fallthrough: {SourcePath} matched rule {RuleId}, AutoRedirect=false",
                    path,
                    rule.Id);

                await _next(context);

                return;
            }

            if (appSettings.NoMatchBehavior.Equals("RedirectToDefault", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(appSettings.DefaultNewDomain))
            {
                _logger.LogInformation("No match for {Path}, redirecting to default domain", path);

                context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                context.Response.Headers.Location = appSettings.DefaultNewDomain;

                return;
            }

            if (appSettings.NoMatchBehavior.Equals("SmartSearch", StringComparison.OrdinalIgnoreCase))
            {
                var searchUrl = smartSearchService.BuildSearchUrl(fullPath, appSettings);

                if (!string.IsNullOrEmpty(searchUrl))
                {
                    _logger.LogInformation("No match for {Path}, smart search redirect to {SearchUrl}", path, searchUrl);

                    context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                    context.Response.Headers.Location = searchUrl;

                    return;
                }

                _logger.LogDebug("No match for {Path}, smart search could not build URL, falling through to SPA", path);
            }

            await _next(context);
        }

        private static bool ShouldSkip(string path)
        {
            if (path == "/")
            {
                return true;
            }

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var spaRoute in SpaRoutes)
            {
                if (path.Equals(spaRoute, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(spaRoute + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}