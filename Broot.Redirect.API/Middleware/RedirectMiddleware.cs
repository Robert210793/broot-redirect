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

        private static readonly string[] SpaRoutes =
        {
            "/login",
            "/rules",
            "/global-rules",
            "/settings",
            "/import",
            "/stats",
            "/blocked-ips",
            "/validate"
        };

        public RedirectMiddleware(RequestDelegate next, ILogger<RedirectMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            IRuleMatchingService ruleMatchingService,
            IUrlTransformService urlTransformService,
            IAppSettingsCacheService settingsCache,
            ISmartSearchService smartSearchService,
            ITrackingRepository trackingRepository,
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

            var matchingConfig = RuleMatchingConfigFactory.Create(options.Value);

            var matchResult = ruleMatchingService.ResolveMatch(fullPath, matchingConfig);

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

                        try
                        {
                            var trackingEntry = new TrackingEntry
                            {
                                Id = Guid.NewGuid(),
                                OldUrl = context.Request.Scheme + "://" + context.Request.Host + fullPath,
                                NewUrl = targetUrl,
                                Path = fullPath,
                                Timestamp = DateTimeOffset.UtcNow,
                                UserAgent = context.Request.Headers.UserAgent.ToString(),
                                Referrer = context.Request.Headers.Referer.ToString(),
                                RuleId = rule.Id.ToString(),
                                MatchQuality = matchResult.Quality,
                                Feedback = "auto-redirect",
                                RedirectStrategy = "rule"
                            };

                            await trackingRepository.CreateAsync(trackingEntry);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to track auto-redirect for {Path}", path);
                        }

                        WriteRedirect(context.Response, targetUrl);

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

                WriteRedirect(context.Response, appSettings.DefaultNewDomain);

                return;
            }

            if (appSettings.NoMatchBehavior.Equals("SmartSearch", StringComparison.OrdinalIgnoreCase))
            {
                var searchUrl = smartSearchService.BuildSearchUrl(fullPath, appSettings);

                if (!string.IsNullOrEmpty(searchUrl))
                {
                    _logger.LogInformation("No match for {Path}, smart search redirect to {SearchUrl}", path, searchUrl);

                    WriteRedirect(context.Response, searchUrl);

                    return;
                }

                _logger.LogDebug("No match for {Path}, smart search could not build URL, falling through to SPA", path);
            }

            await _next(context);
        }

        private static void WriteRedirect(HttpResponse response, string targetUrl)
        {
            response.StatusCode = StatusCodes.Status302Found;
            response.Headers.Location = targetUrl;
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