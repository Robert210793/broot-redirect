using Broot.Redirect.Core.Models;
using System.ComponentModel.DataAnnotations;

namespace Broot.Redirect.API.Dtos
{
    public class CreateRuleRequest
    {
        [Required]
        [MaxLength(500)]
        public string Matcher { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? TargetUrl { get; set; }

        public string RedirectType { get; set; } = "partial";

        public string? InfoText { get; set; }

        public bool AutoRedirect { get; set; } = false;

        public bool DiscardQueryParams { get; set; } = false;

        public bool ForwardQueryParams { get; set; } = false;

        public List<KeptQueryParam> KeptQueryParams { get; set; } = new();

        public List<StaticQueryParam> StaticQueryParams { get; set; } = new();

        public List<SearchAndReplaceEntry> SearchAndReplace { get; set; } = new();
    }

    public class UpdateRuleRequest
    {
        [MaxLength(500)]
        public string? Matcher { get; set; }

        [MaxLength(2000)]
        public string? TargetUrl { get; set; }

        public string? RedirectType { get; set; }

        public string? InfoText { get; set; }

        public bool? AutoRedirect { get; set; }

        public bool? DiscardQueryParams { get; set; }

        public bool? ForwardQueryParams { get; set; }

        public List<KeptQueryParam>? KeptQueryParams { get; set; }

        public List<StaticQueryParam>? StaticQueryParams { get; set; }

        public List<SearchAndReplaceEntry>? SearchAndReplace { get; set; }
    }

    public class PaginatedRulesResponse
    {
        public List<RedirectRule> Rules { get; set; } = new();

        public int Total { get; set; }

        public int TotalPages { get; set; }

        public int CurrentPage { get; set; }
    }

    public class BulkDeleteRequest
    {
        [Required]
        public List<string> Ids { get; set; } = new();
    }

    public class BulkDeleteResponse
    {
        public int Deleted { get; set; }

        public int NotFound { get; set; }
    }

    public class ImportRuleEntry
    {
        public string? Id { get; set; }

        public string Matcher { get; set; } = string.Empty;

        public string? TargetUrl { get; set; }

        public string? RedirectType { get; set; }

        public string? InfoText { get; set; }

        public bool? AutoRedirect { get; set; }

        public bool? DiscardQueryParams { get; set; }

        public bool? ForwardQueryParams { get; set; }

        public List<KeptQueryParam>? KeptQueryParams { get; set; }

        public List<StaticQueryParam>? StaticQueryParams { get; set; }

        public List<SearchAndReplaceEntry>? SearchAndReplace { get; set; }

        public string? CreatedAt { get; set; }
    }

    public class ImportResponse
    {
        public int Imported { get; set; }

        public int Updated { get; set; }

        public List<string> Errors { get; set; } = new();
    }

    public class RedirectResolveResponse
    {
        public RedirectRule? Rule { get; set; }

        public string? ResolvedUrl { get; set; }

        /// <summary>
        /// Raw matching score from the scoring engine.
        /// </summary>
        /// 
        public int MatchQuality { get; set; }

        /// <summary>
        /// Match quality as a percentage (0-100).
        /// 100 = exact match, 75 = extra query params, 50 = partial/prefix match.
        /// </summary>
        /// 
        public int Quality { get; set; }

        /// <summary>
        /// Traffic-light level derived from Quality: "green" (>= 90), "yellow" (>= 60), "red" (&lt; 60).
        /// </summary>
        /// 
        public MatchQualityLevel Level { get; set; }

        /// <summary>
        /// True when no rule matched and the ResolvedUrl is a smart search fallback URL.
        /// The frontend should display this differently (search link instead of redirect).
        /// </summary>
        /// 
        public bool IsSmartSearchFallback { get; set; }

        /// <summary>
        /// The generated search URL when IsSmartSearchFallback is true.
        /// Null when a rule matched normally.
        /// </summary>
        /// 
        public string? FallbackSearchUrl { get; set; }
    }

    public class HealthResponse
    {
        public string Status { get; set; } = "healthy";

        public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("o");

        public long UptimeSeconds { get; set; }

        public int RuleCount { get; set; }

        public HealthChecks Checks { get; set; } = new();
    }

    public class HealthChecks
    {
        public HealthCheckDetail TableStorage { get; set; } = new();

        public HealthCheckDetail Cache { get; set; } = new();
    }

    public class HealthCheckDetail
    {
        public string Status { get; set; } = "ok";

        public long ResponseTimeMs { get; set; }

        public string? Error { get; set; }
    }
}
