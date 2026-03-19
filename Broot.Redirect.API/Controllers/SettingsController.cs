using Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Broot.Redirect.API.Controllers
{
    [ApiController]
    [Route("api/settings")]
    public class SettingsController : ControllerBase
    {
        private readonly IAppSettingsCacheService _settingsCache;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(
            IAppSettingsCacheService settingsCache,
            ILogger<SettingsController> logger)
        {
            _settingsCache = settingsCache;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Get()
        {
            var settings = _settingsCache.GetSettings();

            return Ok(settings);
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] UpdateSettingsRequest request)
        {
            var existing = _settingsCache.GetSettings();

            var merged = new AppSettings
            {
                DefaultNewDomain = request.DefaultNewDomain ?? existing.DefaultNewDomain,
                NoMatchBehavior = request.NoMatchBehavior ?? existing.NoMatchBehavior,
                AutoRedirect = request.AutoRedirect ?? existing.AutoRedirect,
                HeaderTitle = request.HeaderTitle ?? existing.HeaderTitle,
                InfoPageTitle = request.InfoPageTitle ?? existing.InfoPageTitle,
                InfoPageMessage = request.InfoPageMessage ?? existing.InfoPageMessage,
                PopupMode = request.PopupMode ?? existing.PopupMode,
                NewUrlLabel = request.NewUrlLabel ?? existing.NewUrlLabel,
                OldUrlLabel = request.OldUrlLabel ?? existing.OldUrlLabel,
                CopyButtonText = request.CopyButtonText ?? existing.CopyButtonText,
                OpenButtonText = request.OpenButtonText ?? existing.OpenButtonText,
                SpecialHintsTitle = request.SpecialHintsTitle ?? existing.SpecialHintsTitle,
                SpecialHintsDescription = request.SpecialHintsDescription ?? existing.SpecialHintsDescription,
                FooterCopyright = request.FooterCopyright ?? existing.FooterCopyright,
                SmartSearchUrl = request.SmartSearchUrl ?? existing.SmartSearchUrl,
                SmartSearchRegex = request.SmartSearchRegex ?? existing.SmartSearchRegex,
                SmartSearchSkipEncoding = request.SmartSearchSkipEncoding ?? existing.SmartSearchSkipEncoding,
                MatchHighExplanation = request.MatchHighExplanation ?? existing.MatchHighExplanation,
                MatchMediumExplanation = request.MatchMediumExplanation ?? existing.MatchMediumExplanation,
                MatchLowExplanation = request.MatchLowExplanation ?? existing.MatchLowExplanation,
                MatchNoneExplanation = request.MatchNoneExplanation ?? existing.MatchNoneExplanation
            };

            var validBehaviors = new[] { "RedirectToDefault", "SmartSearch", "Return404" };

            if (!validBehaviors.Contains(merged.NoMatchBehavior, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = $"Invalid NoMatchBehavior: '{merged.NoMatchBehavior}'. Valid values: {string.Join(", ", validBehaviors)}" });
            }

            var validPopupModes = new[] { "inline", "active" };

            if (!validPopupModes.Contains(merged.PopupMode, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = $"Invalid PopupMode: '{merged.PopupMode}'. Valid values: {string.Join(", ", validPopupModes)}" });
            }

            var updated = await _settingsCache.UpdateSettingsAsync(merged);

            _logger.LogInformation("Settings updated by admin.");

            return Ok(updated);
        }
    }
}
