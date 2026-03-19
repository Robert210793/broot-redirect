import { Component, inject, OnInit, OnDestroy, signal, computed } from '@angular/core';
import { NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';
import { TrackingService } from '../../shared/services/tracking.service';
import { StatsResponse, TrackingEntry } from '../../shared/models/tracking';
import { ResizableColumnsDirective } from '../../shared/directives/resizable-columns.directive';

@Component({
    selector: 'app-stats',
    imports: [FormsModule, NgClass, ResizableColumnsDirective],
    templateUrl: './stats.component.html',
    styleUrl: './stats.component.css'
})
export class StatsComponent implements OnInit, OnDestroy {

    private readonly trackingService = inject(TrackingService);

    private readonly destroy$ = new Subject<void>();
    private readonly searchSubject = new Subject<string>();

    searchText = '';
    timeRange: '24h' | '7d' | 'all' = 'all';
    showDeleteConfirm = signal(false);
    isDeleting = signal(false);

    // -- Aggregation state --

    stats = signal<StatsResponse | null>(null);
    isLoadingStats = signal(true);

    // -- Entries table state --

    entries = signal<TrackingEntry[]>([]);
    total = signal(0);
    totalPages = signal(0);
    currentPage = signal(1);
    isLoadingEntries = signal(true);
    limit = 50;

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
            this.currentPage.set(1);
            this.loadEntries(1, searchValue);
        });

        this.loadStats();
        this.loadEntries(1);
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    onSearchChange(value: string): void {
        this.searchText = value;
        this.searchSubject.next(value);
    }

    onPageChange(page: number): void {
        this.currentPage.set(page);
        this.loadEntries(page, this.searchText);
    }

    onRefresh(): void {
        this.loadStats();
        this.loadEntries(this.currentPage(), this.searchText);
    }

    onTimeRangeChange(range: '24h' | '7d' | 'all'): void {
        this.timeRange = range;
        this.loadStats();
    }

    onDeleteAll(): void {
        this.showDeleteConfirm.set(true);
    }

    onCancelDelete(): void {
        this.showDeleteConfirm.set(false);
    }

    onConfirmDelete(): void {
        this.isDeleting.set(true);

        this.trackingService.deleteAll().subscribe({
            next: () => {
                this.isDeleting.set(false);
                this.showDeleteConfirm.set(false);
                this.loadStats();
                this.loadEntries(1, '');
                this.searchText = '';
            },
            error: () => {
                this.isDeleting.set(false);
                this.showDeleteConfirm.set(false);
            }
        });
    }

    formatDate(isoDate: string): string {
        try {
            const date = new Date(isoDate);

            return date.toLocaleDateString('de-CH', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit',
                hour: '2-digit',
                minute: '2-digit'
            });
        } catch {
            return isoDate;
        }
    }

    formatPercent(value: number): string {
        return value.toFixed(1) + '%';
    }

    feedbackLabel(feedback: string | null | undefined): string {
        if (!feedback) {
            return '-';
        }

        if (feedback === 'OK') {
            return 'OK';
        }

        if (feedback === 'NOK') {
            return 'NOK';
        }

        if (feedback === 'auto-redirect') {
            return 'Auto';
        }

        return feedback;
    }

    feedbackClass(feedback: string | null | undefined): string {
        if (!feedback) {
            return '';
        }

        if (feedback === 'OK') {
            return 'feedback-badge-ok';
        }

        if (feedback === 'NOK') {
            return 'feedback-badge-nok';
        }

        if (feedback === 'auto-redirect') {
            return 'feedback-badge-auto';
        }

        return '';
    }

    strategyLabel(strategy: string | null | undefined): string {
        if (!strategy) {
            return '-';
        }

        if (strategy === 'rule') {
            return 'Regel';
        }

        if (strategy === 'smart-search') {
            return 'Suche';
        }

        if (strategy === 'domain-fallback') {
            return 'Fallback';
        }

        return strategy;
    }

    private loadStats(): void {
        this.isLoadingStats.set(true);

        this.trackingService.getStats(this.timeRange).subscribe({
            next: (response) => {
                this.stats.set(response);
                this.isLoadingStats.set(false);
            },
            error: () => {
                this.isLoadingStats.set(false);
            }
        });
    }

    private loadEntries(page: number, search?: string): void {
        this.isLoadingEntries.set(true);

        this.trackingService.getEntries(page, this.limit, search || undefined).subscribe({
            next: (response) => {
                this.entries.set(response.entries);
                this.total.set(response.total);
                this.totalPages.set(response.totalPages);
                this.isLoadingEntries.set(false);
            },
            error: () => {
                this.isLoadingEntries.set(false);
            }
        });
    }
}