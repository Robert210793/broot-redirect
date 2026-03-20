import { Component, inject, OnInit, OnDestroy, signal, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';
import { RulesService, ExportFormat, StreamProgress } from '../../shared/services/rules.service';
import { ToastService } from '../../shared/services/toast.service';
import { RedirectRule, RulesQueryParams } from '../../shared/models/redirect-rule';
import { RuleModalComponent } from './rule-modal.component';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { ResizableColumnsDirective } from '../../shared/directives/resizable-columns.directive';

@Component({
    selector: 'app-rules-list',
    imports: [FormsModule, RuleModalComponent, ConfirmDialogComponent, ResizableColumnsDirective],
    templateUrl: './rules-list.component.html',
    styleUrl: './rules-list.component.css'
})
export class RulesListComponent implements OnInit, OnDestroy {

    private readonly rulesService = inject(RulesService);
    private readonly toastService = inject(ToastService);

    private readonly destroy$ = new Subject<void>();
    private readonly searchSubject = new Subject<string>();

    searchText = '';

    queryParams: RulesQueryParams = {
        page: 1,
        limit: 50,
        search: '',
        sortBy: 'createdAt',
        sortOrder: 'desc'
    };

    rules = signal<RedirectRule[]>([]);
    total = signal(0);
    totalPages = signal(0);
    isLoading = signal(false);
    isExporting = signal(false);
    isDeleting = signal(false);
    deleteProgress = signal<StreamProgress | null>(null);
    selectedIds = signal(new Set<string>());
    showExportMenu = signal(false);

    // -- Rule modal state --
    showRuleModal = signal(false);
    editingRule = signal<RedirectRule | null>(null);

    // -- Confirm dialog state --
    showConfirmDialog = signal(false);
    confirmTitle = signal('');
    confirmMessage = signal('');
    confirmAction = signal<() => void>(() => {});

    currentPage = signal(1);

    allSelected = computed(() => {
        const currentRules = this.rules();
        const selected = this.selectedIds();

        return currentRules.length > 0 && currentRules.every((rule) => selected.has(rule.id));
    });

    visiblePages = computed(() => {
        const current = this.currentPage();
        const totalPagesValue = this.totalPages();
        const pages: number[] = [];
        const range = 2;

        let start = Math.max(1, current - range);
        let end = Math.min(totalPagesValue, current + range);

        if (end - start < range * 2) {
            if (start === 1) {
                end = Math.min(totalPagesValue, start + range * 2);
            } else {
                start = Math.max(1, end - range * 2);
            }
        }

        for (let i = start; i <= end; i++) {
            pages.push(i);
        }

        return pages;
    });

    ngOnInit(): void {
        this.searchSubject.pipe(
            debounceTime(300),
            distinctUntilChanged(),
            takeUntil(this.destroy$)
        ).subscribe((searchValue) => {
            this.queryParams.search = searchValue;
            this.queryParams.page = 1;
            this.currentPage.set(1);
            this.loadRules();
        });

        this.loadRules();
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    onSearchChange(value: string): void {
        this.searchText = value;
        this.searchSubject.next(value);
    }

    onSort(column: string): void {
        if (this.queryParams.sortBy === column) {
            this.queryParams.sortOrder = this.queryParams.sortOrder === 'asc' ? 'desc' : 'asc';
        } else {
            this.queryParams.sortBy = column;
            this.queryParams.sortOrder = 'asc';
        }

        this.queryParams.page = 1;
        this.currentPage.set(1);
        this.loadRules();
    }

    onPageChange(page: number): void {
        this.queryParams.page = page;
        this.currentPage.set(page);
        this.loadRules();
    }

    // -- Modal actions --

    onNewRule(): void {
        this.editingRule.set(null);
        this.showRuleModal.set(true);
    }

    onRowClick(rule: RedirectRule, event: Event): void {
        this.editingRule.set(rule);
        this.showRuleModal.set(true);
    }

    onRuleModalSaved(rule: RedirectRule): void {
        this.showRuleModal.set(false);
        this.editingRule.set(null);
        this.loadRules();
    }

    onRuleModalClosed(): void {
        this.showRuleModal.set(false);
        this.editingRule.set(null);
    }

    // -- Selection --

    onToggleAll(event: Event): void {
        const checked = (event.target as HTMLInputElement).checked;

        if (checked) {
            const allIds = new Set(this.rules().map((rule) => rule.id));
            this.selectedIds.set(allIds);
        } else {
            this.selectedIds.set(new Set());
        }
    }

    onToggleSelection(id: string): void {
        this.selectedIds.update((current) => {
            const next = new Set(current);

            if (next.has(id)) {
                next.delete(id);
            } else {
                next.add(id);
            }

            return next;
        });
    }

    // -- Delete actions (now via confirm dialog) --

    onBulkDelete(): void {
        const count = this.selectedIds().size;

        this.confirmTitle.set('Ausgewählte Regeln löschen');
        this.confirmMessage.set(`${count} ausgewählte Regel(n) löschen? Dies kann nicht rückgängig gemacht werden.`);

        this.confirmAction.set(() => {
            const ids = Array.from(this.selectedIds());

            this.isDeleting.set(true);
            this.deleteProgress.set({ type: 'progress', processed: 0, total: count });

            this.rulesService.bulkDelete(ids).subscribe({
                next: (result) => {
                    this.isDeleting.set(false);
                    this.deleteProgress.set(null);
                    this.toastService.show(`${result.deleted} Regel(n) gelöscht.`, 'success');
                    this.selectedIds.set(new Set());
                    this.loadRules();
                },
                error: () => {
                    this.isDeleting.set(false);
                    this.deleteProgress.set(null);
                    this.toastService.show('Regeln konnten nicht gelöscht werden.', 'error');
                }
            });
        });

        this.showConfirmDialog.set(true);
    }

    onDeleteSingle(rule: RedirectRule): void {
        this.confirmTitle.set('Regel löschen');
        this.confirmMessage.set(`Regel "${rule.matcher}" löschen? Dies kann nicht rückgängig gemacht werden.`);

        this.confirmAction.set(() => {
            this.rulesService.deleteRule(rule.id).subscribe({
                next: () => {
                    this.toastService.show('Regel gelöscht.', 'success');
                    this.loadRules();
                },
                error: () => {
                    this.toastService.show('Regel konnte nicht gelöscht werden.', 'error');
                }
            });
        });

        this.showConfirmDialog.set(true);
    }

    onDeleteAll(): void {
        const ruleCount = this.total();

        this.confirmTitle.set('Alle Regeln löschen');
        this.confirmMessage.set(`Alle ${ruleCount} Regel(n) löschen? Dies kann nicht rückgängig gemacht werden.`);

        this.confirmAction.set(() => {
            this.isDeleting.set(true);
            this.deleteProgress.set({ type: 'progress', processed: 0, total: ruleCount });

            this.rulesService.deleteAllWithProgress().subscribe({
                next: (progress) => {
                    this.deleteProgress.set(progress);
                },
                complete: () => {
                    const finalProgress = this.deleteProgress();

                    this.isDeleting.set(false);
                    this.deleteProgress.set(null);
                    this.toastService.show(`${finalProgress?.processed ?? ruleCount} Regel(n) gelöscht.`, 'success');
                    this.selectedIds.set(new Set());
                    this.queryParams.page = 1;
                    this.currentPage.set(1);
                    this.loadRules();
                },
                error: () => {
                    this.isDeleting.set(false);
                    this.deleteProgress.set(null);
                    this.toastService.show('Alle Regeln konnten nicht gelöscht werden.', 'error');
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

    // -- Export --

    onToggleExportMenu(): void {
        this.showExportMenu.update((current) => !current);
    }

    onExport(format: ExportFormat): void {
        this.showExportMenu.set(false);
        this.isExporting.set(true);

        const extensionMap: Record<ExportFormat, string> = {
            json: '.json',
            csv: '.csv',
            xlsx: '.xlsx'
        };

        const extension = extensionMap[format];

        this.rulesService.exportRules(format).subscribe({
            next: (blob) => {
                const url = URL.createObjectURL(blob);
                const anchor = document.createElement('a');
                anchor.href = url;
                anchor.download = `redirect-rules-export${extension}`;
                anchor.click();
                URL.revokeObjectURL(url);
                this.isExporting.set(false);
                this.toastService.show('Export heruntergeladen.', 'success');
            },
            error: () => {
                this.isExporting.set(false);
                this.toastService.show('Export fehlgeschlagen.', 'error');
            }
        });
    }

    formatDate(isoDate: string): string {
        try {
            const date = new Date(isoDate);

            return date.toLocaleDateString('de-CH', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit'
            });
        } catch {
            return isoDate;
        }
    }

    private loadRules(): void {
        this.isLoading.set(true);

        this.rulesService.getRules(this.queryParams).subscribe({
            next: (response) => {
                this.rules.set(response.rules);
                this.total.set(response.total);
                this.totalPages.set(response.totalPages);
                this.isLoading.set(false);
            },
            error: () => {
                this.isLoading.set(false);
                this.toastService.show('Regeln konnten nicht geladen werden.', 'error');
            }
        });
    }
}