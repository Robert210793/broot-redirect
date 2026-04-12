export interface AppSettings {
    // -- Redirect behavior --
    defaultNewDomain: string;
    noMatchBehavior: string;
    autoRedirect: boolean;

    // -- Info page: header --
    headerTitle: string;

    // -- Info page: main content --
    infoPageTitle: string;
    infoPageMessage: string;

    // -- Info page: popup mode --
    popupMode: string;

    // -- Info page: URL comparison labels --
    newUrlLabel: string;
    oldUrlLabel: string;
    copyButtonText: string;
    openButtonText: string;

    // -- Info page: special hints --
    specialHintsTitle: string;
    specialHintsDescription: string;

    // -- Info page: footer --
    footerCopyright: string;

    // -- Smart search fallback --
    smartSearchUrl: string;
    smartSearchRegex: string;
    smartSearchSkipEncoding: boolean;

    // -- Match quality explanations --
    matchHighExplanation: string;
    matchMediumExplanation: string;
    matchLowExplanation: string;
    matchNoneExplanation: string;

    // -- Behavioral toggles --
    caseSensitiveLinkDetection: boolean;
    enableReferrerTracking: boolean;
    enableFeedbackSurvey: boolean;
    enableFeedbackComment: boolean;
    showLinkQualityGauge: boolean;
}