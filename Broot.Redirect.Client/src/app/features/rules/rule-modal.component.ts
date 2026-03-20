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

                    const message = error?.error?.message || error?.error?.title || 'Regel konnte nicht aktualisiert werden.';

                    this.toastService.show(message, 'error');
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

                    const message = error?.error?.message || error?.error?.title || 'Regel konnte nicht erstellt werden.';

                    this.toastService.show(message, 'error');
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

        if (!this.matcher.trim()) {
            validationErrors['matcher'] = 'Matcher ist erforderlich.';
        } else if (this.matcher.length > 500) {
            validationErrors['matcher'] = 'Matcher darf maximal 500 Zeichen lang sein.';
        }

        if (this.redirectType === 'regex') {
            try {
                new RegExp(this.matcher);
            } catch {
                validationErrors['matcher'] = 'Ungueltige Regex-Syntax.';
            }
        }

        this.errors.set(validationErrors);

        return Object.keys(validationErrors).length === 0;
    }
}