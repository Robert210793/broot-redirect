using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    /// <summary>
    /// Records a single redirect visit for analytics purposes.
    /// Written by the public /api/track endpoint when the info page displays a resolved URL.
    /// Feedback (OK/NOK) and an optional user-proposed URL are added later via /api/feedback.
    /// </summary>
    /// 
    public sealed class TrackingEntry
    {
        public Guid Id { get; set; }

        /// <summary>
        /// The original (old) URL the user arrived at.
        /// </summary>
        /// 
        public string OldUrl { get; set; } = string.Empty;

        /// <summary>
        /// The resolved (new) URL that was displayed or redirected to.
        /// </summary>
        /// 
        public string NewUrl { get; set; } = string.Empty;

        /// <summary>
        /// The extracted path segment used for rule matching.
        /// </summary>
        /// 
        public string Path { get; set; } = string.Empty;

        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        public string? UserAgent { get; set; }

        public string? Referrer { get; set; }

        /// <summary>
        /// The ID of the matched redirect rule, if any.
        /// </summary>
        /// 
        public string? RuleId { get; set; }

        /// <summary>
        /// Match quality percentage (0-100) at the time of the redirect.
        /// </summary>
        /// 
        public int MatchQuality { get; set; }

        /// <summary>
        /// User feedback recorded after interacting with the new URL.
        /// Values: "OK", "NOK", "auto-redirect", or null (no feedback given).
        /// </summary>
        /// 
        public string? Feedback { get; set; }

        /// <summary>
        /// If the user reported NOK, they may suggest the correct URL here.
        /// </summary>
        /// 
        public string? UserProposedUrl { get; set; }

        /// <summary>
        /// How the redirect was resolved: "rule", "smart-search", or "domain-fallback".
        /// </summary>
        /// 
        public string? RedirectStrategy { get; set; }
    }
}
