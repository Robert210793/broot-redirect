namespace Broot.Redirect.API.Configuration
{
    public class BrootRedirectOptions
    {
        public const string SectionName = "SmartRedirect";

        public string AdminPassword { get; set; } = "Password1";

        public int SessionTimeoutDays { get; set; } = 7;

        public bool CaseSensitivePath { get; set; } = false;

        public bool CaseSensitiveQuery { get; set; } = false;

        public string TrailingSlashPolicy { get; set; } = "ignore";

        public int WeightPathSegment { get; set; } = 10;

        public int WeightQueryPair { get; set; } = 5;

        public int PenaltyWildcard { get; set; } = 1;

        public int BonusExactMatch { get; set; } = 50;

        public int RegexMatchTimeoutSeconds { get; set; } = 1;

        public int TrackingRetentionDays { get; set; } = 30;

        // -- Rate limiting --

        public int RateLimitGlobalMax { get; set; } = 300;

        public int RateLimitTrackingMax { get; set; } = 300;

        public int RateLimitAdminMax { get; set; } = 60;

        public int RateLimitWindowSeconds { get; set; } = 60;

        // -- Brute force protection --

        public int LoginMaxAttempts { get; set; } = 5;

        public int LoginBlockDurationMinutes { get; set; } = 1440;
    }
}