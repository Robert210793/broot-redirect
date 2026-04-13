using Azure;
using Azure.Data.Tables;
using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Infrastructure.Persistence
{
    /// <summary>
    /// Azure Table Storage entity for AppSettings.
    /// Single-row table with PartitionKey = "Settings", RowKey = "default".
    /// All properties are scalar strings/bools -- no JSON serialization needed.
    /// </summary>
    /// 
    public class AppSettingsEntity : ITableEntity
    {
        public const string DefaultPartitionKey = "Settings";
        public const string DefaultRowKey = "default";

        public string PartitionKey { get; set; } = DefaultPartitionKey;

        public string RowKey { get; set; } = DefaultRowKey;

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }

        public string DefaultNewDomain { get; set; } = string.Empty;

        public string NoMatchBehavior { get; set; } = "RedirectToDefault";

        public string HeaderTitle { get; set; } = string.Empty;

        public string InfoPageTitle { get; set; } = string.Empty;

        public string InfoPageMessage { get; set; } = string.Empty;

        public string PopupMode { get; set; } = "inline";

        public string NewUrlLabel { get; set; } = string.Empty;

        public string OldUrlLabel { get; set; } = string.Empty;

        public string CopyButtonText { get; set; } = string.Empty;

        public string OpenButtonText { get; set; } = string.Empty;

        public string SpecialHintsTitle { get; set; } = string.Empty;

        public string SpecialHintsDescription { get; set; } = string.Empty;

        public string SmartSearchUrl { get; set; } = string.Empty;

        public string SmartSearchRegex { get; set; } = string.Empty;

        public bool SmartSearchSkipEncoding { get; set; }

        public string MatchHighExplanation { get; set; } = string.Empty;

        public string MatchMediumExplanation { get; set; } = string.Empty;

        public string MatchLowExplanation { get; set; } = string.Empty;

        public string MatchNoneExplanation { get; set; } = string.Empty;

        public bool CaseSensitiveLinkDetection { get; set; }

        public bool EnableReferrerTracking { get; set; } = true;

        public bool EnableFeedbackSurvey { get; set; }

        public bool EnableFeedbackComment { get; set; }

        public bool ShowLinkQualityGauge { get; set; } = true;

        /// <summary>
        /// Maps a domain AppSettings to a Table Storage entity.
        /// </summary>
        /// 
        public static AppSettingsEntity FromDomainModel(AppSettings settings)
        {
            return new AppSettingsEntity
            {
                PartitionKey = DefaultPartitionKey,
                RowKey = DefaultRowKey,
                DefaultNewDomain = settings.DefaultNewDomain,
                NoMatchBehavior = settings.NoMatchBehavior,
                HeaderTitle = settings.HeaderTitle,
                InfoPageTitle = settings.InfoPageTitle,
                InfoPageMessage = settings.InfoPageMessage,
                PopupMode = settings.PopupMode,
                NewUrlLabel = settings.NewUrlLabel,
                OldUrlLabel = settings.OldUrlLabel,
                CopyButtonText = settings.CopyButtonText,
                OpenButtonText = settings.OpenButtonText,
                SpecialHintsTitle = settings.SpecialHintsTitle,
                SpecialHintsDescription = settings.SpecialHintsDescription,
                SmartSearchUrl = settings.SmartSearchUrl,
                SmartSearchRegex = settings.SmartSearchRegex,
                SmartSearchSkipEncoding = settings.SmartSearchSkipEncoding,
                MatchHighExplanation = settings.MatchHighExplanation,
                MatchMediumExplanation = settings.MatchMediumExplanation,
                MatchLowExplanation = settings.MatchLowExplanation,
                MatchNoneExplanation = settings.MatchNoneExplanation,
                CaseSensitiveLinkDetection = settings.CaseSensitiveLinkDetection,
                EnableReferrerTracking = settings.EnableReferrerTracking,
                EnableFeedbackSurvey = settings.EnableFeedbackSurvey,
                EnableFeedbackComment = settings.EnableFeedbackComment,
                ShowLinkQualityGauge = settings.ShowLinkQualityGauge
            };
        }

        /// <summary>
        /// Maps a Table Storage entity back to a domain AppSettings.
        /// </summary>
        /// 
        public AppSettings ToDomainModel()
        {
            return new AppSettings
            {
                DefaultNewDomain = DefaultNewDomain,
                NoMatchBehavior = NoMatchBehavior,
                HeaderTitle = HeaderTitle,
                InfoPageTitle = InfoPageTitle,
                InfoPageMessage = InfoPageMessage,
                PopupMode = PopupMode,
                NewUrlLabel = NewUrlLabel,
                OldUrlLabel = OldUrlLabel,
                CopyButtonText = CopyButtonText,
                OpenButtonText = OpenButtonText,
                SpecialHintsTitle = SpecialHintsTitle,
                SpecialHintsDescription = SpecialHintsDescription,
                SmartSearchUrl = SmartSearchUrl,
                SmartSearchRegex = SmartSearchRegex,
                SmartSearchSkipEncoding = SmartSearchSkipEncoding,
                MatchHighExplanation = MatchHighExplanation,
                MatchMediumExplanation = MatchMediumExplanation,
                MatchLowExplanation = MatchLowExplanation,
                MatchNoneExplanation = MatchNoneExplanation,
                CaseSensitiveLinkDetection = CaseSensitiveLinkDetection,
                EnableReferrerTracking = EnableReferrerTracking,
                EnableFeedbackSurvey = EnableFeedbackSurvey,
                EnableFeedbackComment = EnableFeedbackComment,
                ShowLinkQualityGauge = ShowLinkQualityGauge
            };
        }
    }
}