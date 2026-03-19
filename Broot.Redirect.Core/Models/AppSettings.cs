using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    /// <summary>
    /// Runtime-editable application settings stored in Azure Table Storage.
    /// These values can be changed by admins via the settings API.
    /// Static configuration (AdminPassword, scoring weights, session timeout, etc.)
    /// remains in BrootRedirectOptions / appsettings.json.
    /// </summary>
    /// 
    public sealed class AppSettings
    {
        public string DefaultNewDomain { get; set; } = "https://new.example.com";

        /// <summary>
        /// What to do when no rule matches: "RedirectToDefault" | "SmartSearch" | "Return404"
        /// </summary>
        /// 
        public string NoMatchBehavior { get; set; } = "RedirectToDefault";

        /// <summary>
        /// Global auto-redirect toggle. When false, all matched URLs show the info page
        /// regardless of per-rule AutoRedirect settings.
        /// </summary>
        /// 
        public bool AutoRedirect { get; set; } = true;

        public string HeaderTitle { get; set; } = "SmartRedirect Suite";

        public string InfoPageTitle { get; set; } = "URL veraltet - Aktualisierung erforderlich";

        public string InfoPageMessage { get; set; } = "Du verwendest einen alten Link. Dieser Link ist nicht mehr aktuell und wird bald nicht mehr funktionieren. Bitte verwende die neue URL und aktualisiere deine Verknuepfungen.";

        /// <summary>
        /// "inline" shows the URL comparison immediately.
        /// "active" hides it behind a button click.
        /// </summary>
        /// 
        public string PopupMode { get; set; } = "inline";

        public string NewUrlLabel { get; set; } = "Neue URL";

        public string OldUrlLabel { get; set; } = "Alte aufgerufene URL";

        public string CopyButtonText { get; set; } = "URL kopieren";

        public string OpenButtonText { get; set; } = "In neuem Tab oeffnen";

        public string SpecialHintsTitle { get; set; } = "Bitte beachte folgendes fuer diese URL:";

        public string SpecialHintsDescription { get; set; } = "Die neue URL wurde automatisch generiert. Es kann sein, dass sie nicht wie erwartet funktioniert. Falls die URL ungueltig ist, nutze bitte die Suchfunktion in der neuen Applikation, um den gewuenschten Inhalt zu finden.";

        public string FooterCopyright { get; set; } = "";

        public string SmartSearchUrl { get; set; } = string.Empty;

        public string SmartSearchRegex { get; set; } = string.Empty;

        public bool SmartSearchSkipEncoding { get; set; } = false;

        public string MatchHighExplanation { get; set; } = "Die neue URL wurde gefunden und korrekt zugeordnet.";

        public string MatchMediumExplanation { get; set; } = "Die URL wurde erkannt, weicht aber leicht ab (z.B. zusaetzliche Link-Parameter).";

        public string MatchLowExplanation { get; set; } = "Es wurde nur ein Teil der URL erkannt und ersetzt (Partial Match).";

        public string MatchNoneExplanation { get; set; } = "Die URL konnte nicht spezifisch zugeordnet werden. Bitte nutze die Suche der neuen App.";

        // -- Behavioral toggles --

        /// <summary>
        /// When true, rule matching uses case-sensitive path comparison.
        /// Overrides the static BrootRedirectOptions.CaseSensitivePath at runtime.
        /// </summary>
        public bool CaseSensitiveLinkDetection { get; set; } = false;

        /// <summary>
        /// When true, imported matchers and target URLs are percent-encoded during import.
        /// </summary>
        public bool EncodeImportedUrls { get; set; } = true;

        /// <summary>
        /// When true, the Referrer header is stored in tracking entries.
        /// Some customer deployments may want this disabled for privacy.
        /// </summary>
        public bool EnableReferrerTracking { get; set; } = true;

        /// <summary>
        /// When true, the info page shows an OK/NOK feedback prompt after the user copies or opens the URL.
        /// </summary>
        public bool EnableFeedbackSurvey { get; set; } = false;

        /// <summary>
        /// When true and the user clicks NOK, a text input appears to suggest the correct URL.
        /// Only effective when EnableFeedbackSurvey is true.
        /// </summary>
        public bool EnableFeedbackComment { get; set; } = false;

        /// <summary>
        /// When true, the match quality gauge (green/yellow/red) is visible on the info page.
        /// </summary>
        public bool ShowLinkQualityGauge { get; set; } = true;
    }
}