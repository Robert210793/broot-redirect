import { Component, inject, OnInit, OnDestroy, signal, computed } from '@angular/core';
import { NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';
import { TrackingService } from '../../shared/services/tracking.service';
import { StatsResponse, TrackingEntry, TrendDataPoint, StatsFilterParams } from '../../shared/models/tracking';
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

    // -- Filter state (Phase 5.5) --

    showFilters = signal(false);
    filterQualityMin: number | null = null;
    filterQualityMax: number | null = null;
    filterFeedbackType = '';
    filterRuleId = '';

    // -- Export state (Phase 5.5) --

    isExporting = signal(false);

    // -- Aggregation state --

    stats = signal<StatsResponse | null>(null);
    isLoadingStats = signal(true);

    // -- Trend state (Phase 5.4) --

    trendData = signal<TrendDataPoint[]>([]);
    trendAggregation = signal<'day' | 'week' | 'month'>('day');
    trendDays = signal(30);
    isLoadingTrend = signal(true);

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

    trendMaxValue = computed(() => {
        const data = this.trendData();

        if (data.length === 0) {
            return 1;
        }

        return Math.max(...data.map((dataPoint) => dataPoint.total), 1);
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
        this.loadTrend();
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
        this.loadTrend();
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
                this.loadTrend();
                this.searchText = '';
            },
            error: () => {
                this.isDeleting.set(false);
                this.showDeleteConfirm.set(false);
            }
        });
    }

    // -- Filter methods (Phase 5.5) --

    onToggleFilters(): void {
        this.showFilters.update((current) => !current);
    }

    onApplyFilters(): void {
        this.currentPage.set(1);
        this.loadEntries(1, this.searchText);
    }

    onClearFilters(): void {
        this.filterQualityMin = null;
        this.filterQualityMax = null;
        this.filterFeedbackType = '';
        this.filterRuleId = '';
        this.currentPage.set(1);
        this.loadEntries(1, this.searchText);
    }

    hasActiveFilters(): boolean {
        return this.filterQualityMin != null
            || this.filterQualityMax != null
            || this.filterFeedbackType !== ''
            || this.filterRuleId !== '';
    }

    // -- Export methods (Phase 5.5) --

    onExport(format: 'csv' | 'json'): void {
        this.isExporting.set(true);

        this.trackingService.exportEntries(format, this.searchText || undefined, this.buildFilterParams()).subscribe({
            next: (blob) => {
                const url = URL.createObjectURL(blob);
                const anchor = document.createElement('a');

                anchor.href = url;
                anchor.download = `tracking-export.${format}`;
                anchor.click();

                URL.revokeObjectURL(url);
                this.isExporting.set(false);
            },
            error: () => {
                this.isExporting.set(false);
            }
        });
    }

    // -- Trend methods (Phase 5.4) --

    onTrendAggregationChange(aggregation: 'day' | 'week' | 'month'): void {
        this.trendAggregation.set(aggregation);
        this.loadTrend();
    }

    onTrendDaysChange(days: number): void {
        this.trendDays.set(days);
        this.loadTrend();
    }

    trendBarHeight(value: number): number {
        const maxValue = this.trendMaxValue();

        if (maxValue === 0) {
            return 0;
        }

        return (value / maxValue) * 100;
    }

    trendDateLabel(dateString: string): string {
        const date = new Date(dateString);

        if (this.trendAggregation() === 'month') {
            return date.toLocaleDateString('de-CH', { month: 'short', year: '2-digit' });
        }

        return date.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit' });
    }

    // -- Formatters --

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

    // -- Private --

    private buildFilterParams(): StatsFilterParams | undefined {
        const filters: StatsFilterParams = {};
        let hasFilter = false;

        if (this.filterQualityMin != null) {
            filters.qualityMin = this.filterQualityMin;
            hasFilter = true;
        }

        if (this.filterQualityMax != null) {
            filters.qualityMax = this.filterQualityMax;
            hasFilter = true;
        }

        if (this.filterFeedbackType) {
            filters.feedbackType = this.filterFeedbackType;
            hasFilter = true;
        }

        if (this.filterRuleId) {
            filters.ruleId = this.filterRuleId;
            hasFilter = true;
        }

        return hasFilter ? filters : undefined;
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

        this.trackingService.getEntries(page, this.limit, search || undefined, this.buildFilterParams()).subscribe({
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

    private loadTrend(): void {
        this.isLoadingTrend.set(true);

        this.trackingService.getTrend(this.trendDays(), this.trendAggregation()).subscribe({
            next: (response) => {
                this.trendData.set(response.dataPoints);
                this.isLoadingTrend.set(false);
            },
            error: () => {
                this.isLoadingTrend.set(false);
            }
        });
    }
}