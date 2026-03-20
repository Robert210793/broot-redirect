import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../shared/services/settings.service';
import { ToastService } from '../../shared/services/toast.service';
import { AppSettings } from '../../shared/models/app-settings';

@Component({
    selector: 'app-settings',
    imports: [FormsModule],
    templateUrl: './settings.component.html',
    styleUrl: './settings.component.css'
})
export class SettingsComponent implements OnInit {

    private readonly settingsService = inject(SettingsService);
    private readonly toastService = inject(ToastService);

    isLoading = signal(true);
    isSaving = signal(false);

    // -- Redirect behavior --
    defaultNewDomain = '';
    noMatchBehavior = 'RedirectToDefault';
    autoRedirect = false;

    // -- Info page: header --
    headerTitle = '';

    // -- Info page: main content --
    infoPageTitle = '';
    infoPageMessage = '';

    // -- Info page: popup mode --
    popupMode = 'inline';

    // -- Info page: URL comparison labels --
    newUrlLabel = '';
    oldUrlLabel = '';
    copyButtonText = '';
    openButtonText = '';

    // -- Info page: special hints --
    specialHintsTitle = '';
    specialHintsDescription = '';

    // -- Info page: footer --
    footerCopyright = '';

    // -- Smart search fallback --
    smartSearchUrl = '';
    smartSearchRegex = '';
    smartSearchSkipEncoding = false;

    // -- Match quality explanations --
    matchHighExplanation = '';
    matchMediumExplanation = '';
    matchLowExplanation = '';
    matchNoneExplanation = '';

    // -- Behavioral toggles --
    caseSensitiveLinkDetection = false;
    encodeImportedUrls = true;
    enableReferrerTracking = true;
    enableFeedbackSurvey = false;
    enableFeedbackComment = false;
    showLinkQualityGauge = true;

    ngOnInit(): void {
        this.loadSettings();
    }

    onSave(): void {
        this.isSaving.set(true);

        const payload: Partial<AppSettings> = {
            defaultNewDomain: this.defaultNewDomain,
            noMatchBehavior: this.noMatchBehavior,
            autoRedirect: this.autoRedirect,
            headerTitle: this.headerTitle,
            infoPageTitle: this.infoPageTitle,
            infoPageMessage: this.infoPageMessage,
            popupMode: this.popupMode,
            newUrlLabel: this.newUrlLabel,
            oldUrlLabel: this.oldUrlLabel,
            copyButtonText: this.copyButtonText,
            openButtonText: this.openButtonText,
            specialHintsTitle: this.specialHintsTitle,
            specialHintsDescription: this.specialHintsDescription,
            footerCopyright: this.footerCopyright,
            smartSearchUrl: this.smartSearchUrl,
            smartSearchRegex: this.smartSearchRegex,
            smartSearchSkipEncoding: this.smartSearchSkipEncoding,
            matchHighExplanation: this.matchHighExplanation,
            matchMediumExplanation: this.matchMediumExplanation,
            matchLowExplanation: this.matchLowExplanation,
            matchNoneExplanation: this.matchNoneExplanation,
            caseSensitiveLinkDetection: this.caseSensitiveLinkDetection,
            encodeImportedUrls: this.encodeImportedUrls,
            enableReferrerTracking: this.enableReferrerTracking,
            enableFeedbackSurvey: this.enableFeedbackSurvey,
            enableFeedbackComment: this.enableFeedbackComment,
            showLinkQualityGauge: this.showLinkQualityGauge
        };

        this.settingsService.updateSettings(payload).subscribe({
            next: (updated) => {
                this.isSaving.set(false);
                this.populateForm(updated);
                this.toastService.show('Einstellungen gespeichert.', 'success');
            },
            error: (error) => {
                this.isSaving.set(false);

                const message = error?.error?.error || error?.error?.title || 'Einstellungen konnten nicht gespeichert werden.';

                this.toastService.show(message, 'error');
            }
        });
    }

    onReset(): void {
        this.loadSettings();
    }

    // -- Private --

    private loadSettings(): void {
        this.isLoading.set(true);

        this.settingsService.getSettings().subscribe({
            next: (settings) => {
                this.populateForm(settings);
                this.isLoading.set(false);
            },
            error: () => {
                this.isLoading.set(false);
                this.toastService.show('Einstellungen konnten nicht geladen werden.', 'error');
            }
        });
    }

    private populateForm(settings: AppSettings): void {
        this.defaultNewDomain = settings.defaultNewDomain;
        this.noMatchBehavior = settings.noMatchBehavior;
        this.autoRedirect = settings.autoRedirect;
        this.headerTitle = settings.headerTitle;
        this.infoPageTitle = settings.infoPageTitle;
        this.infoPageMessage = settings.infoPageMessage;
        this.popupMode = settings.popupMode;
        this.newUrlLabel = settings.newUrlLabel;
        this.oldUrlLabel = settings.oldUrlLabel;
        this.copyButtonText = settings.copyButtonText;
        this.openButtonText = settings.openButtonText;
        this.specialHintsTitle = settings.specialHintsTitle;
        this.specialHintsDescription = settings.specialHintsDescription;
        this.footerCopyright = settings.footerCopyright;
        this.smartSearchUrl = settings.smartSearchUrl;
        this.smartSearchRegex = settings.smartSearchRegex;
        this.smartSearchSkipEncoding = settings.smartSearchSkipEncoding;
        this.matchHighExplanation = settings.matchHighExplanation;
        this.matchMediumExplanation = settings.matchMediumExplanation;
        this.matchLowExplanation = settings.matchLowExplanation;
        this.matchNoneExplanation = settings.matchNoneExplanation;
        this.caseSensitiveLinkDetection = settings.caseSensitiveLinkDetection;
        this.encodeImportedUrls = settings.encodeImportedUrls;
        this.enableReferrerTracking = settings.enableReferrerTracking;
        this.enableFeedbackSurvey = settings.enableFeedbackSurvey;
        this.enableFeedbackComment = settings.enableFeedbackComment;
        this.showLinkQualityGauge = settings.showLinkQualityGauge;
    }
}