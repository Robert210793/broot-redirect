import { Component, inject, OnInit, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { RedirectService } from '../../shared/services/redirect.service';
import { SettingsService } from '../../shared/services/settings.service';
import { AuthService } from '../../shared/services/auth.service';
import { TrackingService } from '../../shared/services/tracking.service';
import { ResolveResponse, MatchQualityLevel } from '../../shared/models/redirect-rule';
import { AppSettings } from '../../shared/models/app-settings';
import { QualityGaugeComponent } from '../../shared/components/quality-gauge/quality-gauge.component';
import { PasswordModalComponent } from '../../shared/components/password-modal/password-modal.component';

@Component({
    selector: 'app-info-page',
    imports: [QualityGaugeComponent, PasswordModalComponent, FormsModule],
    templateUrl: './info-page.component.html',
    styleUrl: './info-page.component.css',
})
export class InfoPageComponent implements OnInit {

    private readonly router = inject(Router);
    private readonly redirectService = inject(RedirectService);
    private readonly settingsService = inject(SettingsService);
    private readonly authService = inject(AuthService);
    private readonly trackingService = inject(TrackingService);
    private readonly http = inject(HttpClient);

    // -- State --

    isLoading = signal(true);
    settings = signal<AppSettings | null>(null);
    resolveResult = signal<ResolveResponse | null>(null);
    isRootPath = signal(false);
    originalUrl = '';
    currentYear = new Date().getFullYear();

    // Prefer the build-injected meta tag (no extra request); fall back to /api/health
    // at runtime when the placeholder wasn't replaced (e.g. dev / ng serve).
    appVersion = signal<string>((() => {
        const meta = document.querySelector('meta[name="app-version"]');
        const version = meta?.getAttribute('content') ?? '';
        return version && version !== '__APP_VERSION__' ? version : '';
    })());

    // -- UI state --

    copied = signal(false);
    showPasswordModal = signal(false);
    isCheckingAuth = signal(false);
    showPopupDialog = signal(false);

    // -- Tracking state --

    private trackingId = signal<string | null>(null);
    private hasAskedFeedback = signal(false);
    showFeedbackDialog = signal(false);
    showCommentStep = signal(false);
    feedbackSuccess = signal(false);
    commentUrl = signal('');

    // -- Derived --

    hasMatch = computed(() => {
        const result = this.resolveResult();

        return result !== null && result.resolvedUrl !== null && !result.isSmartSearchFallback;
    });

    isSmartSearchFallback = computed(() => {
        const result = this.resolveResult();

        return result !== null && result.isSmartSearchFallback === true;
    });

    resolvedUrl = computed(() => {
        const result = this.resolveResult();

        if (!result) {
            return '';
        }

        if (result.isSmartSearchFallback && result.fallbackSearchUrl) {
            return result.fallbackSearchUrl;
        }

        return result.resolvedUrl || '';
    });

    quality = computed(() => {
        const result = this.resolveResult();

        return result?.quality ?? 0;
    });

    level = computed((): MatchQualityLevel => {
        const result = this.resolveResult();

        return result?.level ?? 'red';
    });

    matchExplanation = computed(() => {
        const result = this.resolveResult();
        const currentSettings = this.settings();

        if (!result || !currentSettings) {
            return '';
        }

        if (result.isSmartSearchFallback) {
            return currentSettings.matchNoneExplanation;
        }

        if (result.quality >= 90) {
            return currentSettings.matchHighExplanation;
        }

        if (result.quality >= 60) {
            return currentSettings.matchMediumExplanation;
        }

        return currentSettings.matchLowExplanation;
    });

    infoText = computed(() => {
        const result = this.resolveResult();
        const currentSettings = this.settings();

        if (!result || !currentSettings) {
            return '';
        }

        // Per-rule infoText takes precedence
        if (result.rule?.infoText) {
            return result.rule.infoText;
        }

        return currentSettings.specialHintsDescription;
    });

    isPopupMode = computed(() => {
        const currentSettings = this.settings();

        return currentSettings?.popupMode === 'active';
    });

    isInlineMode = computed(() => {
        const currentSettings = this.settings();

        return currentSettings?.popupMode === 'inline';
    });

    // -- Lifecycle --

    // /api/health returns the backend's assembly version. It may answer 503 while
    // warming up / unhealthy, but the body still carries the version, so read both paths.
    private loadVersionFallback(): void {
        const apply = (version?: string | null) => {
            if (version && version !== 'unknown') {
                this.appVersion.set(version);
            }
        };

        this.http.get<{ version?: string }>('/api/health').subscribe({
            next: (response) => apply(response?.version),
            error: (error) => apply(error?.error?.version),
        });
    }

    ngOnInit(): void {
        if (!this.appVersion()) {
            this.loadVersionFallback();
        }

        const path = this.extractPath();

        // Build the original URL from the extracted path (base href stripped)
        // so it reflects the actual redirect path, not the SPA path
        const url = new URL(window.location.href);

        this.originalUrl = url.origin + path;

        this.settingsService.getSettings().subscribe({
            next: (loadedSettings) => {
                this.settings.set(loadedSettings);

                // Show popup dialog if active mode
                if (loadedSettings.popupMode === 'active') {
                    this.showPopupDialog.set(true);
                }

                this.resolveRedirect(path);
            },
            error: () => {
                // Proceed without settings, use defaults
                this.resolveRedirect(path);
            }
        });
    }

    // -- Actions --

    onCopy(): void {
        const url = this.resolvedUrl();

        if (!url) {
            return;
        }

        navigator.clipboard.writeText(url).then(() => {
            this.copied.set(true);

            setTimeout(() => {
                this.copied.set(false);
            }, 2500);

            // Show feedback survey after first copy/open interaction
            this.promptFeedback();
        });
    }

    onOpenNewTab(): void {
        const url = this.resolvedUrl();

        if (url) {
            window.open(url, '_blank');

            // Show feedback survey after first copy/open interaction
            this.promptFeedback();
        }
    }

    onDismissPopup(): void {
        this.showPopupDialog.set(false);
    }

    onGearClick(): void {
        if (this.isCheckingAuth()) {
            return;
        }

        this.isCheckingAuth.set(true);

        this.authService.checkStatus().subscribe({
            next: (status) => {
                this.isCheckingAuth.set(false);

                if (status.isAuthenticated) {
                    this.router.navigate(['/rules']);
                } else {
                    this.showPasswordModal.set(true);
                }
            },
            error: () => {
                this.isCheckingAuth.set(false);
                this.showPasswordModal.set(true);
            }
        });
    }

    onAuthenticated(): void {
        this.showPasswordModal.set(false);

        this.router.navigate(['/rules']);
    }

    onPasswordModalClosed(): void {
        this.showPasswordModal.set(false);
    }

    // -- Feedback actions --

    onFeedback(feedback: 'OK' | 'NOK'): void {
        if (feedback === 'NOK') {
            // Show comment step so user can suggest the correct URL
            this.showCommentStep.set(true);

            return;
        }

        this.submitFeedback('OK');
    }

    onCommentSubmit(): void {
        const proposedUrl = this.commentUrl().trim() || undefined;

        this.submitFeedback('NOK', proposedUrl);
    }

    onCommentSkip(): void {
        this.submitFeedback('NOK');
    }

    onDismissFeedback(): void {
        this.showFeedbackDialog.set(false);
        this.showCommentStep.set(false);
        this.commentUrl.set('');
    }

    // -- Private --

    private extractPath(): string {
        const url = new URL(window.location.href);

        // The path as seen by the user (everything after the host)
        let path = url.pathname;

        // Strip the base href prefix (/admin/) so we only resolve actual redirect paths
        const baseElement = document.querySelector('base');
        const baseHref = baseElement?.getAttribute('href') || '/';

        if (path.startsWith(baseHref)) {
            path = '/' + path.substring(baseHref.length);
        }

        // Include query string if present
        if (url.search) {
            path += url.search;
        }

        return path;
    }

    private resolveRedirect(path: string): void {
        if (!path || path === '/') {
            this.isRootPath.set(true);

            // At root, synthesize a resolve result using defaultNewDomain (matching original Node.js app behavior)
            const currentSettings = this.settings();

            if (currentSettings?.defaultNewDomain) {
                this.resolveResult.set({
                    rule: null,
                    resolvedUrl: currentSettings.defaultNewDomain,
                    matchQuality: 100,
                    quality: 100,
                    level: 'green',
                    isSmartSearchFallback: false,
                    fallbackSearchUrl: null
                });
            }

            this.isLoading.set(false);

            return;
        }

        this.redirectService.resolve(path).subscribe({
            next: (response) => {
                this.resolveResult.set(response);
                this.isLoading.set(false);

                // Track the visit after successfully resolving
                this.trackVisit(path, response);
            },
            error: () => {
                this.resolveResult.set(null);
                this.isLoading.set(false);
            },
        });
    }

    private trackVisit(path: string, response: ResolveResponse): void {
        const resolvedUrl = response.isSmartSearchFallback && response.fallbackSearchUrl
            ? response.fallbackSearchUrl
            : (response.resolvedUrl || '');

        // Determine redirect strategy
        let redirectStrategy: string | undefined;

        if (response.rule) {
            redirectStrategy = 'rule';
        } else if (response.isSmartSearchFallback) {
            redirectStrategy = 'smart-search';
        } else {
            redirectStrategy = 'domain-fallback';
        }

        this.trackingService.track({
            oldUrl: this.originalUrl.substring(0, 8000),
            newUrl: resolvedUrl.substring(0, 8000),
            path: path.substring(0, 8000),
            userAgent: (navigator.userAgent || '').substring(0, 2000),
            referrer: (document.referrer || '').substring(0, 2000) || undefined,
            ruleId: response.rule?.id || undefined,
            matchQuality: response.quality,
            redirectStrategy: redirectStrategy
        }).subscribe({
            next: (trackResponse) => {
                this.trackingId.set(trackResponse.id);
            },
            error: () => {
                // Tracking failure is non-critical -- don't block the user
            }
        });
    }

    private promptFeedback(): void {
        // Respect the admin "Feedback-Umfrage" toggle (AppSettings.EnableFeedbackSurvey).
        if (!this.settings()?.enableFeedbackSurvey) {
            return;
        }

        if (this.hasAskedFeedback() || !this.trackingId()) {
            return;
        }

        this.hasAskedFeedback.set(true);
        this.showFeedbackDialog.set(true);
    }

    private submitFeedback(feedback: 'OK' | 'NOK', userProposedUrl?: string): void {
        const currentTrackingId = this.trackingId();

        if (!currentTrackingId) {
            this.onDismissFeedback();

            return;
        }

        this.trackingService.feedback({
            trackingId: currentTrackingId,
            feedback: feedback,
            userProposedUrl: userProposedUrl
        }).subscribe({
            next: () => {
                this.feedbackSuccess.set(true);

                setTimeout(() => {
                    this.showFeedbackDialog.set(false);
                    this.showCommentStep.set(false);
                    this.feedbackSuccess.set(false);
                    this.commentUrl.set('');
                }, 2000);
            },
            error: () => {
                this.onDismissFeedback();
            }
        });
    }
}