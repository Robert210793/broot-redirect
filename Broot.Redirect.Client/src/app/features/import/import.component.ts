import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgClass } from '@angular/common';
import { Router } from '@angular/router';
import { RulesService, StreamProgress } from '../../shared/services/rules.service';
import { ToastService } from '../../shared/services/toast.service';
import { ImportResult, ImportPreviewResponse, ImportPreviewEntry } from '../../shared/models/redirect-rule';

type ImportMode = 'file' | 'paste';

type StatusFilter = 'all' | 'new' | 'update' | 'invalid';

@Component({
    selector: 'app-import',
    imports: [FormsModule, NgClass],
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

    // Preview state
    previewData = signal<ImportPreviewResponse | null>(null);
    isLoadingPreview = signal(false);
    statusFilter: StatusFilter = 'all';

    // Import state
    isImporting = signal(false);
    importProgress = signal<StreamProgress | null>(null);
    importResult = signal<ImportResult | null>(null);

    private parsedJsonRules: unknown[] = [];

    get canPreview(): boolean {
        if (this.importMode === 'paste') {
            return this.jsonText.trim().length > 0;
        }

        return this.selectedFile() !== null;
    }

    get filteredPreview(): ImportPreviewEntry[] {
        const data = this.previewData();

        if (!data) {
            return [];
        }

        if (this.statusFilter === 'all') {
            return data.preview;
        }

        return data.preview.filter(entry => entry.status === this.statusFilter);
    }

    get canConfirmImport(): boolean {
        const data = this.previewData();

        if (!data) {
            return false;
        }

        return (data.counts.new + data.counts.update) > 0;
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
        this.previewData.set(null);
        this.importResult.set(null);
        this.importProgress.set(null);

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
        this.isLoadingPreview.set(true);
        this.statusFilter = 'all';

        if (this.importMode === 'paste') {
            this.previewFromJson();
        } else {
            this.previewFromFile();
        }
    }

    onReset(): void {
        this.previewData.set(null);
        this.importResult.set(null);
        this.importProgress.set(null);
        this.parsedJsonRules = [];
        this.parseError.set(null);
        this.statusFilter = 'all';
    }

    onFullReset(): void {
        this.onReset();
        this.selectedFile.set(null);
        this.selectedFileName.set('');
        this.jsonText = '';
    }

    onStatusFilter(filter: StatusFilter): void {
        this.statusFilter = filter;
    }

    onConfirmImport(): void {
        this.isImporting.set(true);

        const total = this.previewData()?.total ?? 0;

        this.importProgress.set({ type: 'progress', processed: 0, total });

        if (this.importMode === 'paste' || this.isJsonFile()) {
            const rules = this.parsedJsonRules.length > 0 ? this.parsedJsonRules : this.tryParseJson();

            if (!rules) {
                this.isImporting.set(false);

                return;
            }

            this.rulesService.importRulesJsonWithProgress(rules).subscribe({
                next: (progress) => this.importProgress.set(progress),
                complete: () => this.handleImportComplete(),
                error: (error) => this.handleImportError(error)
            });
        } else {
            const file = this.selectedFile();

            if (!file) {
                this.isImporting.set(false);

                return;
            }

            this.rulesService.importFileWithProgress(file).subscribe({
                next: (progress) => this.importProgress.set(progress),
                complete: () => this.handleImportComplete(),
                error: (error) => this.handleImportError(error)
            });
        }
    }

    // -- Private --

    private isJsonFile(): boolean {
        const file = this.selectedFile();

        return file !== null && file.name.toLowerCase().endsWith('.json');
    }

    private previewFromJson(): void {
        const rules = this.tryParseJson();

        if (!rules) {
            this.isLoadingPreview.set(false);

            return;
        }

        this.parsedJsonRules = rules;

        this.rulesService.previewJsonImport(rules).subscribe({
            next: (response) => {
                this.previewData.set(response);
                this.isLoadingPreview.set(false);
            },
            error: (error) => {
                this.isLoadingPreview.set(false);

                const message = error?.error?.error || 'Vorschau fehlgeschlagen.';

                this.parseError.set(message);
            }
        });
    }

    private previewFromFile(): void {
        const file = this.selectedFile();

        if (!file) {
            this.isLoadingPreview.set(false);

            return;
        }

        this.rulesService.previewFileImport(file).subscribe({
            next: (response) => {
                this.previewData.set(response);
                this.isLoadingPreview.set(false);
            },
            error: (error) => {
                this.isLoadingPreview.set(false);

                const message = error?.error?.error || 'Datei konnte nicht eingelesen werden.';

                this.parseError.set(message);
            }
        });
    }

    private tryParseJson(): unknown[] | null {
        try {
            const parsed = JSON.parse(this.jsonText);

            if (!Array.isArray(parsed)) {
                this.parseError.set('JSON muss ein Array von Regelobjekten sein.');

                return null;
            }

            if (parsed.length === 0) {
                this.parseError.set('JSON-Array ist leer.');

                return null;
            }

            return parsed;
        } catch {
            this.parseError.set('Ungueltiges JSON. Bitte ueberpruefen und erneut versuchen.');

            return null;
        }
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
            this.previewData.set(null);

            this.toastService.show(
                `${result.imported} neu importiert, ${result.updated} aktualisiert.`,
                result.errors.length > 0 ? 'error' : 'success'
            );
        }
    }

    private handleImportError(error: unknown): void {
        this.isImporting.set(false);
        this.importProgress.set(null);

        const message = (error as { message?: string })?.message || 'Import fehlgeschlagen.';

        this.toastService.show(message, 'error');
    }
}