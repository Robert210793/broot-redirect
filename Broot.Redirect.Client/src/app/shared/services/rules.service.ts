import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, switchMap, timer, takeWhile, tap, map } from 'rxjs';
import {
    RedirectRule,
    PaginatedRulesResponse,
    CreateRuleRequest,
    UpdateRuleRequest,
    BulkDeleteRequest,
    BulkDeleteResponse,
    ImportResult,
    ImportPreviewResponse,
    RulesQueryParams
} from '../models/redirect-rule';

export type ExportFormat = 'json' | 'csv' | 'xlsx';

export interface StreamProgress {
    type: 'progress' | 'complete';
    processed: number;
    total: number;
    imported?: number;
    updated?: number;
    errors?: string[];
}

interface JobStartResponse {
    jobId: string;
    total: number;
}

interface JobStatusResponse {
    processed: number;
    total: number;
    isComplete: boolean;
    imported?: number;
    updated?: number;
    errors?: string[];
    error?: string;
}

@Injectable({
    providedIn: 'root'
})
export class RulesService {

    private readonly baseUrl = '/api/rules';

    constructor(private httpClient: HttpClient) {}

    getRules(queryParams: RulesQueryParams): Observable<PaginatedRulesResponse> {
        let params = new HttpParams()
            .set('page', queryParams.page.toString())
            .set('limit', queryParams.limit.toString())
            .set('sortBy', queryParams.sortBy)
            .set('sortOrder', queryParams.sortOrder);

        if (queryParams.search) {
            params = params.set('search', queryParams.search);
        }

        return this.httpClient.get<PaginatedRulesResponse>(this.baseUrl, { params });
    }

    getRule(id: string): Observable<RedirectRule> {
        return this.httpClient.get<RedirectRule>(`${this.baseUrl}/${id}`);
    }

    createRule(rule: CreateRuleRequest): Observable<RedirectRule> {
        return this.httpClient.post<RedirectRule>(this.baseUrl, rule);
    }

    updateRule(id: string, rule: UpdateRuleRequest): Observable<RedirectRule> {
        return this.httpClient.put<RedirectRule>(`${this.baseUrl}/${id}`, rule);
    }

    deleteRule(id: string): Observable<void> {
        return this.httpClient.delete<void>(`${this.baseUrl}/${id}`);
    }

    bulkDelete(ids: string[]): Observable<BulkDeleteResponse> {
        const body: BulkDeleteRequest = { ids };

        return this.httpClient.delete<BulkDeleteResponse>(`${this.baseUrl}/bulk`, { body });
    }

    /**
     * Deletes all rules with polling progress.
     */
    deleteAllWithProgress(): Observable<StreamProgress> {
        return this.httpClient.delete<JobStartResponse>(`${this.baseUrl}/all`).pipe(
            switchMap(({ jobId, total }) => this.pollJob(jobId, total))
        );
    }

    /**
     * Import rules from JSON body with polling progress.
     */
    importRulesJsonWithProgress(rules: unknown[]): Observable<StreamProgress> {
        return this.httpClient.post<JobStartResponse>(`${this.baseUrl}/import`, rules).pipe(
            switchMap(({ jobId, total }) => this.pollJob(jobId, total))
        );
    }

    /**
     * Import rules from a file with polling progress.
     */
    importFileWithProgress(file: File): Observable<StreamProgress> {
        const formData = new FormData();

        formData.append('file', file, file.name);

        return this.httpClient.post<JobStartResponse>(`${this.baseUrl}/import`, formData).pipe(
            switchMap(({ jobId, total }) => this.pollJob(jobId, total))
        );
    }

    /**
     * Preview a file import (CSV, XLSX, JSON file).
     * Sends the file to the server for parsing and comparison against existing rules.
     */
    previewFileImport(file: File): Observable<ImportPreviewResponse> {
        const formData = new FormData();

        formData.append('file', file, file.name);

        return this.httpClient.post<ImportPreviewResponse>(`${this.baseUrl}/import/preview`, formData);
    }

    /**
     * Preview a JSON import (pasted text).
     * Sends the parsed JSON array to the server for comparison against existing rules.
     */
    previewJsonImport(rules: unknown[]): Observable<ImportPreviewResponse> {
        return this.httpClient.post<ImportPreviewResponse>(`${this.baseUrl}/import/preview`, rules);
    }

    /**
     * Export rules in the specified format.
     * Returns a Blob for download.
     */
    exportRules(format: ExportFormat = 'json'): Observable<Blob> {
        const params = new HttpParams().set('format', format);

        return this.httpClient.get(`${this.baseUrl}/export`, {
            params,
            responseType: 'blob'
        });
    }

    // -- Private polling helper --

    private pollJob(jobId: string, total: number): Observable<StreamProgress> {
        return new Observable<StreamProgress>((subscriber) => {
            subscriber.next({ type: 'progress', processed: 0, total });

            const poll = timer(0, 500).pipe(
                switchMap(() => this.httpClient.get<JobStatusResponse>(`${this.baseUrl}/jobs/${jobId}`)),
                map((data) => ({
                    type: (data.isComplete ? 'complete' : 'progress') as 'progress' | 'complete',
                    processed: data.processed,
                    total: data.total,
                    imported: data.imported,
                    updated: data.updated,
                    errors: data.errors
                })),
                tap((progress) => subscriber.next(progress)),
                takeWhile((progress) => progress.type !== 'complete')
            );

            const subscription = poll.subscribe({
                complete: () => subscriber.complete(),
                error: (error) => subscriber.error(error)
            });

            return () => subscription.unsubscribe();
        });
    }
}