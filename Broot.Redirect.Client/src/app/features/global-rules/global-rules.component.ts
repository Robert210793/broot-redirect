import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { GlobalRulesService } from '../../shared/services/global-rules.service';
import { ToastService } from '../../shared/services/toast.service';
import { GlobalRule, CreateGlobalRuleRequest, UpdateGlobalRuleRequest } from '../../shared/models/global-rule';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';

@Component({
    selector: 'app-global-rules',
    imports: [FormsModule, ConfirmDialogComponent],
    templateUrl: './global-rules.component.html',
    styleUrl: './global-rules.component.css'
})
export class GlobalRulesComponent implements OnInit {

    private readonly globalRulesService = inject(GlobalRulesService);
    private readonly toastService = inject(ToastService);

    rules = signal<GlobalRule[]>([]);
    isLoading = signal(false);

    // -- Edit/Create modal state --
    showModal = signal(false);
    isSaving = signal(false);
    editingRule = signal<GlobalRule | null>(null);

    // Form fields
    formSearch = '';
    formReplace = '';
    formCaseSensitive = false;
    formPriority = 0;
    formErrors = signal<Record<string, string>>({});

    // -- Confirm dialog state --
    showConfirmDialog = signal(false);
    confirmTitle = signal('');
    confirmMessage = signal('');
    confirmAction = signal<() => void>(() => {});

    get isEditMode(): boolean {
        return this.editingRule() !== null;
    }

    get modalTitle(): string {
        return this.isEditMode ? 'Globale Regel bearbeiten' : 'Neue globale Regel';
    }

    ngOnInit(): void {
        this.loadRules();
    }

    // -- Modal actions --

    onNewRule(): void {
        this.editingRule.set(null);
        this.resetForm();

        // Default priority: one higher than current max
        const currentRules = this.rules();

        if (currentRules.length > 0) {
            const maxPriority = Math.max(...currentRules.map((rule) => rule.priority));
            this.formPriority = maxPriority + 1;
        } else {
            this.formPriority = 0;
        }

        this.showModal.set(true);
    }

    onEditRule(rule: GlobalRule): void {
        this.editingRule.set(rule);
        this.formSearch = rule.search;
        this.formReplace = rule.replace;
        this.formCaseSensitive = rule.caseSensitive;
        this.formPriority = rule.priority;
        this.formErrors.set({});
        this.showModal.set(true);
    }

    onModalClose(): void {
        if (this.isSaving()) {
            return;
        }

        this.showModal.set(false);
        this.editingRule.set(null);
    }

    onModalBackdropClick(event: MouseEvent): void {
        if ((event.target as HTMLElement).classList.contains('global-rule-modal-backdrop')) {
            this.onModalClose();
        }
    }

    onModalKeydown(event: KeyboardEvent): void {
        if (event.key === 'Escape') {
            this.onModalClose();
        }
    }

    onModalSave(): void {
        if (!this.validateForm()) {
            return;
        }

        this.isSaving.set(true);

        if (this.isEditMode && this.editingRule()) {
            const request: UpdateGlobalRuleRequest = {
                search: this.formSearch,
                replace: this.formReplace,
                caseSensitive: this.formCaseSensitive,
                priority: this.formPriority
            };

            this.globalRulesService.update(this.editingRule()!.id, request).subscribe({
                next: () => {
                    this.isSaving.set(false);
                    this.showModal.set(false);
                    this.toastService.show('Globale Regel aktualisiert.', 'success');
                    this.loadRules();
                },
                error: (error) => {
                    this.isSaving.set(false);

                    const message = error?.error?.error || 'Globale Regel konnte nicht aktualisiert werden.';

                    this.toastService.show(message, 'error');
                }
            });
        } else {
            const request: CreateGlobalRuleRequest = {
                search: this.formSearch,
                replace: this.formReplace,
                caseSensitive: this.formCaseSensitive,
                priority: this.formPriority
            };

            this.globalRulesService.create(request).subscribe({
                next: () => {
                    this.isSaving.set(false);
                    this.showModal.set(false);
                    this.toastService.show('Globale Regel erstellt.', 'success');
                    this.loadRules();
                },
                error: (error) => {
                    this.isSaving.set(false);

                    const message = error?.error?.error || 'Globale Regel konnte nicht erstellt werden.';

                    this.toastService.show(message, 'error');
                }
            });
        }
    }

    // -- Delete --

    onDeleteRule(rule: GlobalRule): void {
        this.confirmTitle.set('Globale Regel löschen');
        this.confirmMessage.set(`Globale Regel "${rule.search}" -> "${rule.replace}" löschen? Dies kann nicht rückgängig gemacht werden.`);

        this.confirmAction.set(() => {
            this.globalRulesService.delete(rule.id).subscribe({
                next: () => {
                    this.toastService.show('Globale Regel gelöscht.', 'success');
                    this.loadRules();
                },
                error: () => {
                    this.toastService.show('Globale Regel konnte nicht gelöscht werden.', 'error');
                }
            });
        });

        this.showConfirmDialog.set(true);
    }

    onConfirmDialogConfirmed(): void {
        this.showConfirmDialog.set(false);

        const action = this.confirmAction();

        action();
    }

    onConfirmDialogCancelled(): void {
        this.showConfirmDialog.set(false);
    }

    // -- Private --

    private loadRules(): void {
        this.isLoading.set(true);

        this.globalRulesService.getAll().subscribe({
            next: (rules) => {
                this.rules.set(rules);
                this.isLoading.set(false);
            },
            error: () => {
                this.isLoading.set(false);
                this.toastService.show('Globale Regeln konnten nicht geladen werden.', 'error');
            }
        });
    }

    private resetForm(): void {
        this.formSearch = '';
        this.formReplace = '';
        this.formCaseSensitive = false;
        this.formPriority = 0;
        this.formErrors.set({});
    }

    private validateForm(): boolean {
        const errors: Record<string, string> = {};

        if (!this.formSearch.trim()) {
            errors['search'] = 'Suchmuster ist erforderlich.';
        }

        if (!this.formReplace.trim() && this.formReplace !== '') {
            // Replace can be empty string (to delete matches), but not whitespace-only
            // Actually, allow empty replace (it means "remove the match")
        }

        this.formErrors.set(errors);

        return Object.keys(errors).length === 0;
    }
}