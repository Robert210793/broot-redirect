using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;

namespace Broot.Redirect.API.Controllers
{
    [ApiController]
    [Route("api/redirect")]
    public class RedirectController : ControllerBase
    {
        private readonly IRuleCacheService _cacheService;
        private readonly IRuleMatchingService _ruleMatchingService;
        private readonly IUrlTransformService _urlTransformService;
        private readonly IAppSettingsCacheService _settingsCache;
        private readonly ISmartSearchService _smartSearchService;
        private readonly BrootRedirectOptions _options;
        private readonly ILogger<RedirectController> _logger;

        public RedirectController(
            IRuleCacheService cacheService,
            IRuleMatchingService ruleMatchingService,
            IUrlTransformService urlTransformService,
            IAppSettingsCacheService settingsCache,
            ISmartSearchService smartSearchService,
            IOptions<BrootRedirectOptions> options,
            ILogger<RedirectController> logger)
        {
            _cacheService = cacheService;
            _ruleMatchingService = ruleMatchingService;
            _urlTransformService = urlTransformService;
            _settingsCache = settingsCache;
            _smartSearchService = smartSearchService;
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/redirect/resolve?path=/old-path
        /// Called by the Angular info page component.
        /// Returns the matched rule with the fully resolved target URL,
        /// or a smart search fallback URL if configured, or 404 if no match.
        /// No authentication required (public endpoint).
        /// </summary>
        [HttpGet("resolve")]
        public IActionResult Resolve([FromQuery] string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return BadRequest(new { error = "Query parameter 'path' is required" });
            }

            var matchingConfig = RuleMatchingConfigFactory.Create(_options);

            var allRules = _cacheService.GetAll();

            var processedRules = allRules
                .Select(rule => _ruleMatchingService.PreprocessRule(rule, matchingConfig))
                .ToList();

            var matchResult = _ruleMatchingService.FindMatchingRule(path, processedRules, matchingConfig);

            var appSettings = _settingsCache.GetSettings();

            if (matchResult != null)
            {
                var resolvedUrl = _urlTransformService.ResolveTargetUrl(
                    path,
                    matchResult.Rule,
                    appSettings.DefaultNewDomain);

                _logger.LogDebug(
                    "Resolve: {Path} -> {ResolvedUrl} (Rule: {RuleId}, Score: {Score}, Quality: {Quality}%, Level: {Level})",
                    path,
                    resolvedUrl,
                    matchResult.Rule.Id,
                    matchResult.Score,
                    matchResult.Quality,
                    matchResult.Level);

                return Ok(new RedirectResolveResponse
                {
                    Rule = matchResult.Rule,
                    ResolvedUrl = resolvedUrl,
                    MatchQuality = matchResult.Score,
                    Quality = matchResult.Quality,
                    Level = matchResult.Level,
                    IsSmartSearchFallback = false,
                    FallbackSearchUrl = null
                });
            }

            if (appSettings.NoMatchBehavior.Equals("SmartSearch", StringComparison.OrdinalIgnoreCase))
            {
                var searchUrl = _smartSearchService.BuildSearchUrl(path, appSettings);

                if (!string.IsNullOrEmpty(searchUrl))
                {
                    _logger.LogDebug(
                        "Resolve: {Path} -> smart search fallback: {SearchUrl}",
                        path,
                        searchUrl);

                    return Ok(new RedirectResolveResponse
                    {
                        Rule = null,
                        ResolvedUrl = searchUrl,
                        MatchQuality = 0,
                        Quality = 0,
                        Level = MatchQualityLevel.Red,
                        IsSmartSearchFallback = true,
                        FallbackSearchUrl = searchUrl
                    });
                }
            }

            return NotFound(new { error = "No matching rule found" });
        }
    }
}