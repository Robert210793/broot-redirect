import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgClass } from '@angular/common';
import { RulesService } from '../../shared/services/rules.service';
import { ToastService } from '../../shared/services/toast.service';
import { ValidateUrlResult, ValidateUrlsResponse, UrlTraceStep } from '../../shared/models/redirect-rule';

@Component({
    selector: 'app-validate',
    imports: [FormsModule, NgClass],
    templateUrl: './validate.component.html',
    styleUrl: './validate.component.css'
})
export class ValidateComponent {

    private readonly rulesService = inject(RulesService);
    private readonly toastService = inject(ToastService);

    urlInput = '';
    isValidating = signal(false);
    response = signal<ValidateUrlsResponse | null>(null);
    filterMode = signal<'all' | 'matched' | 'unmatched'>('all');

    filteredResults = signal<ValidateUrlResult[]>([]);

    expandedRows = signal<Set<string>>(new Set());

    onValidate(): void {
        const lines = this.urlInput
            .split('\n')
            .map((line) => line.trim())
            .filter((line) => line.length > 0);

        if (lines.length === 0) {
            this.toastService.show('Bitte mindestens eine URL eingeben', 'error');

            return;
        }

        if (lines.length > 500) {
            this.toastService.show('Maximal 500 URLs pro Anfrage', 'error');

            return;
        }

        this.isValidating.set(true);
        this.response.set(null);
        this.expandedRows.set(new Set());

        this.rulesService.validateUrls(lines).subscribe({
            next: (result) => {
                this.response.set(result);
                this.filterMode.set('all');
                this.applyFilter('all', result.results);
                this.isValidating.set(false);
            },
            error: (error) => {
                const message = error?.error?.error || 'Validation failed';
                this.toastService.show(message, 'error');
                this.isValidating.set(false);
            }
        });
    }

    onFilterChange(mode: 'all' | 'matched' | 'unmatched'): void {
        this.filterMode.set(mode);

        const currentResponse = this.response();

        if (currentResponse) {
            this.applyFilter(mode, currentResponse.results);
        }
    }

    onClear(): void {
        this.urlInput = '';
        this.response.set(null);
        this.filteredResults.set([]);
        this.filterMode.set('all');
        this.expandedRows.set(new Set());
    }

    onToggleRow(url: string): void {
        this.expandedRows.update((current) => {
            const next = new Set(current);

            if (next.has(url)) {
                next.delete(url);
            } else {
                next.add(url);
            }

            return next;
        });
    }

    isExpanded(url: string): boolean {
        return this.expandedRows().has(url);
    }

    hasTrace(result: ValidateUrlResult): boolean {
        return result.trace !== null && result.trace.length > 0;
    }

    traceStepIcon(step: UrlTraceStep): string {
        switch (step.type) {
            case 'input':
                return 'step-input';
            case 'rule-match':
                return 'step-match';
            case 'domain-replace':
                return 'step-transform';
            case 'search-replace':
                return 'step-transform';
            case 'global-rule':
                return 'step-global';
            case 'query-forward':
            case 'query-kept':
            case 'query-discard':
            case 'query-static':
                return 'step-query';
            case 'result':
                return 'step-result';
            default:
                return 'step-default';
        }
    }

    stepChanged(step: UrlTraceStep): boolean {
        return step.before !== null
            && step.after !== null
            && step.before !== step.after;
    }

    qualityClass(result: ValidateUrlResult): string {
        if (!result.matched) {
            return 'quality-none';
        }

        if (result.quality >= 90) {
            return 'quality-high';
        }

        if (result.quality >= 60) {
            return 'quality-medium';
        }

        return 'quality-low';
    }

    levelLabel(result: ValidateUrlResult): string {
        if (!result.matched) {
            return 'Kein Treffer';
        }

        return `${result.quality}%`;
    }

    private applyFilter(mode: 'all' | 'matched' | 'unmatched', results: ValidateUrlResult[]): void {
        if (mode === 'matched') {
            this.filteredResults.set(results.filter((result) => result.matched));
        }
        else if (mode === 'unmatched') {
            this.filteredResults.set(results.filter((result) => !result.matched));
        }
        else {
            this.filteredResults.set(results);
        }
    }
}