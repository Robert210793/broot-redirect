namespace Broot.Redirect.API.Dtos
{
    public class UpdateSettingsRequest
    {
        public string? DefaultNewDomain { get; set; }

        public string? NoMatchBehavior { get; set; }

        public bool? AutoRedirect { get; set; }

        public string? HeaderTitle { get; set; }

        public string? InfoPageTitle { get; set; }

        public string? InfoPageMessage { get; set; }

        public string? PopupMode { get; set; }

        public string? NewUrlLabel { get; set; }

        public string? OldUrlLabel { get; set; }

        public string? CopyButtonText { get; set; }

        public string? OpenButtonText { get; set; }

        public string? SpecialHintsTitle { get; set; }

        public string? SpecialHintsDescription { get; set; }

        public string? FooterCopyright { get; set; }

        public string? SmartSearchUrl { get; set; }

        public string? SmartSearchRegex { get; set; }

        public bool? SmartSearchSkipEncoding { get; set; }

        public string? MatchHighExplanation { get; set; }

        public string? MatchMediumExplanation { get; set; }

        public string? MatchLowExplanation { get; set; }

        public string? MatchNoneExplanation { get; set; }
    }
}
