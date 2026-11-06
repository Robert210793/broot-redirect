import { RedirectRule, RuleSource } from './redirect-rule';

export interface AppSettings {
    // -- Redirect behavior --
    defaultNewDomain: string;
    noMatchBehavior: string;

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

    // -- Auto-redirect per rule source --
    autoRedirectManual: AutoRedirectMode;
    autoRedirectImport: AutoRedirectMode;
    autoRedirectQmPro: AutoRedirectMode;
    autoRedirectAreaList: AutoRedirectMode;
}

export type AutoRedirectMode = 'perRule' | 'always' | 'never';

export function autoRedirectModeFor(settings: AppSettings | null, source: RuleSource): AutoRedirectMode {
    if (!settings) {
        return 'perRule';
    }

    switch (source) {
        case 'manual':
            return settings.autoRedirectManual ?? 'perRule';
        case 'import':
            return settings.autoRedirectImport ?? 'perRule';
        case 'qmPro':
            return settings.autoRedirectQmPro ?? 'perRule';
        case 'areaList':
            return settings.autoRedirectAreaList ?? 'perRule';
        default:
            return 'perRule';
    }
}

export function resolveAutoRedirect(settings: AppSettings | null, rule: RedirectRule): boolean {
    switch (autoRedirectModeFor(settings, rule.source)) {
        case 'always':
            return true;
        case 'never':
            return false;
        default:
            return rule.autoRedirect;
    }
}

export function autoRedirectModeLabel(mode: AutoRedirectMode): string {
    switch (mode) {
        case 'always':
            return 'Immer weiterleiten (302)';
        case 'never':
            return 'Nie weiterleiten (Infoseite)';
        default:
            return 'Pro Regel entscheiden';
    }
}