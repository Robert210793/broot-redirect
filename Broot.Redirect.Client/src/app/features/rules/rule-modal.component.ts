import { Component, input, output, signal, inject, effect } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RulesService } from '../../shared/services/rules.service';
import { ToastService } from '../../shared/services/toast.service';
import {
    RedirectRule,
    RedirectType,
    CreateRuleRequest,
    UpdateRuleRequest,
    KeptQueryParam,
    StaticQueryParam,
    SearchAndReplaceEntry
} from '../../shared/models/redirect-rule';

// Matches Node.js shared/schema.ts URL_MATCHER_PATTERN
const MATCHER_PATTERN = /^(\/|[a-zA-Z0-9])([a-zA-Z0-9\-._~:/?#[\]@!$&'()*+,;=%]*)$/;

const MATCHER_MAX_LENGTH = 500;
const TARGET_URL_MAX_LENGTH = 2000;
const INFO_TEXT_MAX_LENGTH = 5000;

@Component({
    selector: 'app-rule-modal',
    imports: [FormsModule],
    templateUrl: './rule-modal.component.html',
    styleUrl: './rule-modal.component.css'
})
export class RuleModalComponent {

    private readonly rulesService = inject(RulesService);
    private readonly toastService = inject(ToastService);

    // Inputs / Outputs
    isOpen = input.required<boolean>();
    rule = input<RedirectRule | null>(null);

    saved = output<RedirectRule>();
    closed = output<void>();

    // Core fields
    matcher = '';
    targetUrl = '';
    redirectType: RedirectType = 'partial';
    infoText = '';
    autoRedirect = false;
    discardQueryParams = false;
    forwardQueryParams = false;

    // Sub-arrays
    keptQueryParams: KeptQueryParam[] = [];
    staticQueryParams: StaticQueryParam[] = [];
    searchAndReplace: SearchAndReplaceEntry[] = [];

    isSaving = signal(false);
    errors = signal<Record<string, string>>({});

    constructor() {
        // Populate form when rule input changes
        effect(() => {
            const currentRule = this.rule();
            const open = this.isOpen();

            if (open) {
                if (currentRule) {
                    this.populateForm(currentRule);
                } else {
                    this.resetForm();
                }
            }
        });
    }

    get isEditMode(): boolean {
        return this.rule() !== null;
    }

    get modalTitle(): string {
        return this.isEditMode ? 'Regel bearbeiten' : 'Neue Regel';
    }

    // -- Actions --

    onSave(): void {
        if (!this.validate()) {
            return;
        }

        this.isSaving.set(true);

        if (this.isEditMode && this.rule()) {
            const updateRequest: UpdateRuleRequest = {
                matcher: this.matcher,
                targetUrl: this.targetUrl || null,
                redirectType: this.redirectType,
                infoText: this.infoText || null,
                autoRedirect: this.autoRedirect,
                discardQueryParams: this.discardQueryParams,
                forwardQueryParams: this.forwardQueryParams,
                keptQueryParams: this.keptQueryParams,
                staticQueryParams: this.staticQueryParams,
                searchAndReplace: this.searchAndReplace
            };

            this.rulesService.updateRule(this.rule()!.id, updateRequest).subscribe({
                next: (updatedRule) => {
                    this.isSaving.set(false);
                    this.toastService.show('Regel aktualisiert.', 'success');
                    this.saved.emit(updatedRule);
                },
                error: (error) => {
                    this.isSaving.set(false);
                    this.applyServerErrors(error);
                }
            });
        } else {
            const createRequest: CreateRuleRequest = {
                matcher: this.matcher,
                targetUrl: this.targetUrl || null,
                redirectType: this.redirectType,
                infoText: this.infoText || null,
                autoRedirect: this.autoRedirect,
                discardQueryParams: this.discardQueryParams,
                forwardQueryParams: this.forwardQueryParams,
                keptQueryParams: this.keptQueryParams,
                staticQueryParams: this.staticQueryParams,
                searchAndReplace: this.searchAndReplace
            };

            this.rulesService.createRule(createRequest).subscribe({
                next: (createdRule) => {
                    this.isSaving.set(false);
                    this.toastService.show('Regel erstellt.', 'success');
                    this.saved.emit(createdRule);
                },
                error: (error) => {
                    this.isSaving.set(false);
                    this.applyServerErrors(error);
                }
            });
        }
    }

    onClose(): void {
        if (this.isSaving()) {
            return;
        }

        this.closed.emit();
    }

    onBackdropClick(event: MouseEvent): void {
        if ((event.target as HTMLElement).classList.contains('rule-modal-backdrop')) {
            this.onClose();
        }
    }

    onKeydown(event: KeyboardEvent): void {
        if (event.key === 'Escape') {
            this.onClose();
        }
    }

    // -- Live field error clearing --

    onMatcherChange(): void {
        const current = this.errors();

        if (current['matcher']) {
            const { matcher, ...rest } = current;

            this.errors.set(rest);
        }
    }

    onTargetUrlChange(): void {
        const current = this.errors();

        if (current['targetUrl']) {
            const { targetUrl, ...rest } = current;

            this.errors.set(rest);
        }
    }

    onRedirectTypeChange(): void {
        const current = this.errors();
        const { targetUrl, ...rest } = current;

        this.errors.set(rest);
    }

    onInfoTextChange(): void {
        const current = this.errors();

        if (current['infoText']) {
            const { infoText, ...rest } = current;

            this.errors.set(rest);
        }
    }

    // -- Sub-array management --

    addKeptQueryParam(): void {
        this.keptQueryParams = [...this.keptQueryParams, {
            keyPattern: '',
            valuePattern: null,
            targetKey: null,
            skipEncoding: false
        }];
    }

    removeKeptQueryParam(index: number): void {
        this.keptQueryParams = this.keptQueryParams.filter((_, i) => i !== index);
    }

    addStaticQueryParam(): void {
        this.staticQueryParams = [...this.staticQueryParams, {
            key: '',
            value: '',
            skipEncoding: false
        }];
    }

    removeStaticQueryParam(index: number): void {
        this.staticQueryParams = this.staticQueryParams.filter((_, i) => i !== index);
    }

    addSearchAndReplace(): void {
        this.searchAndReplace = [...this.searchAndReplace, {
            search: '',
            replace: '',
            caseSensitive: false
        }];
    }

    removeSearchAndReplace(index: number): void {
        this.searchAndReplace = this.searchAndReplace.filter((_, i) => i !== index);
    }

    // -- Private --

    private populateForm(rule: RedirectRule): void {
        this.matcher = rule.matcher;
        this.targetUrl = rule.targetUrl || '';
        this.redirectType = rule.redirectType;
        this.infoText = rule.infoText || '';
        this.autoRedirect = rule.autoRedirect;
        this.discardQueryParams = rule.discardQueryParams;
        this.forwardQueryParams = rule.forwardQueryParams;
        this.keptQueryParams = rule.keptQueryParams ? [...rule.keptQueryParams] : [];
        this.staticQueryParams = rule.staticQueryParams ? [...rule.staticQueryParams] : [];
        this.searchAndReplace = rule.searchAndReplace ? [...rule.searchAndReplace] : [];
        this.errors.set({});
    }

    private resetForm(): void {
        this.matcher = '';
        this.targetUrl = '';
        this.redirectType = 'partial';
        this.infoText = '';
        this.autoRedirect = false;
        this.discardQueryParams = false;
        this.forwardQueryParams = false;
        this.keptQueryParams = [];
        this.staticQueryParams = [];
        this.searchAndReplace = [];
        this.errors.set({});
    }

    private validate(): boolean {
        const validationErrors: Record<string, string> = {};

        // -- Matcher validation --

        const trimmedMatcher = this.matcher.trim();

        if (!trimmedMatcher) {
            validationErrors['matcher'] = 'URL-Muster darf nicht leer sein.';
        } else if (trimmedMatcher.length > MATCHER_MAX_LENGTH) {
            validationErrors['matcher'] = `URL-Muster ist zu lang (maximal ${MATCHER_MAX_LENGTH} Zeichen).`;
        } else if (!MATCHER_PATTERN.test(trimmedMatcher)) {
            validationErrors['matcher'] = "URL-Muster muss mit '/' beginnen oder eine Domain sein (z.B. example.com).";
        }

        // -- Regex-specific matcher check --

        if (this.redirectType === 'regex' && trimmedMatcher) {
            try {
                new RegExp(trimmedMatcher);
            } catch {
                validationErrors['matcher'] = 'Ungültige Regex-Syntax.';
            }
        }

        // -- TargetUrl validation (redirect-type-specific, mirrors Node.js shared/validation.ts) --

        const targetUrlTrimmed = this.targetUrl.trim();

        if (targetUrlTrimmed) {
            if (targetUrlTrimmed.length > TARGET_URL_MAX_LENGTH) {
                validationErrors['targetUrl'] = `Ziel-URL ist zu lang (maximal ${TARGET_URL_MAX_LENGTH} Zeichen).`;
            } else {
                const startsWithHttp = targetUrlTrimmed.startsWith('http://') || targetUrlTrimmed.startsWith('https://');

                if (this.redirectType === 'wildcard') {
                    if (!startsWithHttp) {
                        validationErrors['targetUrl'] = "Bei Typ 'Wildcard' muss die Ziel-URL mit http:// oder https:// beginnen.";
                    }
                } else if (this.redirectType === 'partial') {
                    if (!targetUrlTrimmed.startsWith('/') && !startsWithHttp) {
                        validationErrors['targetUrl'] = "Bei Typ 'Teilweise' muss die Ziel-URL mit '/' beginnen oder eine vollständige URL sein.";
                    }
                } else if (this.redirectType === 'domain') {
                    if (!startsWithHttp) {
                        validationErrors['targetUrl'] = "Bei Typ 'Domain' muss die Ziel-URL mit http:// oder https:// beginnen.";
                    } else {
                        try {
                            const parsed = new URL(targetUrlTrimmed);

                            if (parsed.pathname !== '/' && parsed.pathname !== '') {
                                validationErrors['targetUrl'] = "Bei Typ 'Domain' darf die Ziel-URL keine Unterordner enthalten (nur https://domain.com).";
                            }
                        } catch {
                            validationErrors['targetUrl'] = 'Ungültige URL.';
                        }
                    }
                }
            }
        }

        // -- InfoText validation --

        if (this.infoText && this.infoText.length > INFO_TEXT_MAX_LENGTH) {
            validationErrors['infoText'] = `Info-Text ist zu lang (maximal ${INFO_TEXT_MAX_LENGTH} Zeichen).`;
        }

        // -- Sub-array validation --

        for (let index = 0; index < this.keptQueryParams.length; index++) {
            if (!this.keptQueryParams[index].keyPattern?.trim()) {
                validationErrors[`keptQueryParams_${index}_keyPattern`] = 'Schlüssel-Muster ist erforderlich.';
            }
        }

        for (let index = 0; index < this.staticQueryParams.length; index++) {
            if (!this.staticQueryParams[index].key?.trim()) {
                validationErrors[`staticQueryParams_${index}_key`] = 'Key ist erforderlich.';
            }
        }

        for (let index = 0; index < this.searchAndReplace.length; index++) {
            if (!this.searchAndReplace[index].search?.trim()) {
                validationErrors[`searchAndReplace_${index}_search`] = 'Suchbegriff ist erforderlich.';
            }
        }

        this.errors.set(validationErrors);

        return Object.keys(validationErrors).length === 0;
    }

    /// Maps server-side validation error responses into the errors signal
    /// so field-level messages display inline, or falls back to a toast.
    private applyServerErrors(error: any): void {
        const body = error?.error;

        // Backend returns { error: "...", details: [{ field, message }] }
        if (body?.details && Array.isArray(body.details)) {
            const mapped: Record<string, string> = {};

            for (const detail of body.details) {
                if (detail.field && detail.message) {
                    mapped[detail.field] = detail.message;
                }
            }

            if (Object.keys(mapped).length > 0) {
                this.errors.set(mapped);

                return;
            }
        }

        // Fallback: show a toast with whatever message the server sent
        const message = body?.error || body?.message || body?.title || 'Regel konnte nicht gespeichert werden.';

        this.toastService.show(message, 'error');
    }
}