import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { RulesService, StreamProgress } from '../../shared/services/rules.service';
import { ToastService } from '../../shared/services/toast.service';
import { ImportResult } from '../../shared/models/redirect-rule';

interface PreviewRow {
    matcher: string;
    targetUrl: string;
    redirectType: string;
    status: 'new' | 'update' | 'unknown';
}

type ImportMode = 'file' | 'paste';

@Component({
    selector: 'app-import',
    imports: [FormsModule],
    templateUrl: './import.component.html',
    styleUrl: './import.component.css'
})
export class ImportComponent {

    protected readonly router = inject(Router);

    private readonly rulesService = inject(RulesService);
    private readonly toastService = inject(ToastService);

    importMode: ImportMode = 'file';
    jsonText = '';
    selectedFile = signal<File | null>(null);
    selectedFileName = signal('');
    parseError = signal<string | null>(null);
    previewRows = signal<PreviewRow[] | null>(null);
    isImporting = signal(false);
    importProgress = signal<StreamProgress | null>(null);
    importResult = signal<ImportResult | null>(null);

    private parsedJsonRules: unknown[] = [];

    get isJsonFile(): boolean {
        const file = this.selectedFile();

        return file !== null && file.name.toLowerCase().endsWith('.json');
    }

    get isCsvOrXlsxFile(): boolean {
        const file = this.selectedFile();

        if (!file) {
            return false;
        }

        const name = file.name.toLowerCase();

        return name.endsWith('.csv') || name.endsWith('.xlsx') || name.endsWith('.xls');
    }

    get canPreview(): boolean {
        if (this.importMode === 'paste') {
            return this.jsonText.trim().length > 0;
        }

        return this.selectedFile() !== null;
    }

    onFileSelected(event: Event): void {
        const input = event.target as HTMLInputElement;
        const file = input.files?.[0];

        if (!file) {
            return;
        }

        this.selectedFile.set(file);
        this.selectedFileName.set(file.name);
        this.parseError.set(null);
        this.previewRows.set(null);
        this.importResult.set(null);
        this.importProgress.set(null);

        // If JSON file, read contents for preview
        if (file.name.toLowerCase().endsWith('.json')) {
            const reader = new FileReader();

            reader.onload = () => {
                this.jsonText = reader.result as string;
            };

            reader.readAsText(file);
        }
    }

    onPreview(): void {
        this.parseError.set(null);

        if (this.importMode === 'paste' || this.isJsonFile) {
            this.previewJson();
        } else if (this.isCsvOrXlsxFile) {
            // CSV/XLSX files go directly to import (no client-side preview)
            this.confirmFileImport();
        }
    }

    onReset(): void {
        this.previewRows.set(null);
        this.importResult.set(null);
        this.importProgress.set(null);
        this.parsedJsonRules = [];
        this.parseError.set(null);
    }

    onFullReset(): void {
        this.onReset();
        this.selectedFile.set(null);
        this.selectedFileName.set('');
        this.jsonText = '';
    }

    onConfirmImport(): void {
        this.isImporting.set(true);
        this.importProgress.set({ type: 'progress', processed: 0, total: this.parsedJsonRules.length });

        this.rulesService.importRulesJsonWithProgress(this.parsedJsonRules).subscribe({
            next: (progress) => {
                this.importProgress.set(progress);
            },
            complete: () => {
                this.handleImportComplete();
            },
            error: (error) => {
                this.isImporting.set(false);
                this.importProgress.set(null);

                const message = error?.message || 'Import failed.';

                this.toastService.show(message, 'error');
            }
        });
    }

    // -- Private --

    private previewJson(): void {
        try {
            const parsed = JSON.parse(this.jsonText);

            if (!Array.isArray(parsed)) {
                this.parseError.set('JSON must be an array of rule objects.');

                return;
            }

            if (parsed.length === 0) {
                this.parseError.set('JSON array is empty.');

                return;
            }

            this.parsedJsonRules = parsed;

            const rows: PreviewRow[] = parsed.map((item: Record<string, unknown>) => ({
                matcher: (item['matcher'] as string) || '(missing)',
                targetUrl: (item['targetUrl'] as string) || '',
                redirectType: (item['redirectType'] as string) || 'partial',
                status: item['id'] ? 'update' as const : 'new' as const
            }));

            this.previewRows.set(rows);
        } catch {
            this.parseError.set('Invalid JSON. Please check the format and try again.');
        }
    }

    private confirmFileImport(): void {
        const file = this.selectedFile();

        if (!file) {
            return;
        }

        this.isImporting.set(true);
        this.importProgress.set({ type: 'progress', processed: 0, total: 0 });

        this.rulesService.importFileWithProgress(file).subscribe({
            next: (progress) => {
                this.importProgress.set(progress);
            },
            complete: () => {
                this.handleImportComplete();
            },
            error: (error) => {
                this.isImporting.set(false);
                this.importProgress.set(null);

                const message = error?.message || 'Failed to import file.';

                this.toastService.show(message, 'error');
                this.parseError.set(message);
            }
        });
    }

    private handleImportComplete(): void {
        const finalProgress = this.importProgress();

        this.isImporting.set(false);
        this.importProgress.set(null);

        if (finalProgress && finalProgress.type === 'complete') {
            const result: ImportResult = {
                imported: finalProgress.imported ?? 0,
                updated: finalProgress.updated ?? 0,
                errors: finalProgress.errors ?? []
            };

            this.importResult.set(result);
            this.previewRows.set(null);

            this.toastService.show(
                `Imported ${result.imported} new, updated ${result.updated}.`,
                result.errors.length > 0 ? 'error' : 'success'
            );
        }
    }
}