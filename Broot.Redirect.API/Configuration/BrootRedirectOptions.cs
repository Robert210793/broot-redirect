namespace Broot.Redirect.API.Configuration
{
    /// <summary>
    /// Static configuration that does not change at runtime.
    /// Bound from appsettings.json section "SmartRedirect".
    ///
    /// Runtime-editable settings (DefaultNewDomain, NoMatchBehavior, InfoPageTitle, etc.)
    /// have been moved to AppSettings (stored in Azure Table Storage, served via /api/settings).
    /// </summary>
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
    }
}
