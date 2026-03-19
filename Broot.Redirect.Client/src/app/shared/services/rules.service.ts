import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
    RedirectRule,
    PaginatedRulesResponse,
    CreateRuleRequest,
    UpdateRuleRequest,
    BulkDeleteRequest,
    BulkDeleteResponse,
    ImportResult,
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
        return this.startJobAndPoll(`${this.baseUrl}/all`, 'DELETE');
    }

    /**
     * Import rules from JSON body with polling progress.
     */
    importRulesJsonWithProgress(rules: unknown[]): Observable<StreamProgress> {
        return this.startJobAndPoll(`${this.baseUrl}/import`, 'POST', JSON.stringify(rules), 'application/json');
    }

    /**
     * Import rules from a file with polling progress.
     */
    importFileWithProgress(file: File): Observable<StreamProgress> {
        const formData = new FormData();

        formData.append('file', file, file.name);

        return new Observable<StreamProgress>((subscriber) => {
            fetch(`${this.baseUrl}/import`, { method: 'POST', body: formData })
                .then((response) => {
                    if (!response.ok) {
                        throw new Error(`HTTP ${response.status}`);
                    }

                    return response.json();
                })
                .then(({ jobId, total }) => {
                    subscriber.next({ type: 'progress', processed: 0, total });

                    this.pollJob(jobId, subscriber);
                })
                .catch((error) => subscriber.error(error));
        });
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

    // -- Private polling helpers --

    private startJobAndPoll(url: string, method: string, body?: string, contentType?: string): Observable<StreamProgress> {
        return new Observable<StreamProgress>((subscriber) => {
            const headers: Record<string, string> = {};

            if (contentType) {
                headers['Content-Type'] = contentType;
            }

            fetch(url, { method, headers, body })
                .then((response) => {
                    if (!response.ok) {
                        throw new Error(`HTTP ${response.status}`);
                    }

                    return response.json();
                })
                .then(({ jobId, total }) => {
                    subscriber.next({ type: 'progress', processed: 0, total });

                    this.pollJob(jobId, subscriber);
                })
                .catch((error) => subscriber.error(error));
        });
    }

    private pollJob(jobId: string, subscriber: { next: (value: StreamProgress) => void; complete: () => void; error: (error: unknown) => void }): void {
        const interval = setInterval(() => {
            fetch(`${this.baseUrl}/jobs/${jobId}`)
                .then((response) => {
                    if (!response.ok) {
                        throw new Error(`HTTP ${response.status}`);
                    }

                    return response.json();
                })
                .then((data) => {
                    subscriber.next({
                        type: data.isComplete ? 'complete' : 'progress',
                        processed: data.processed,
                        total: data.total,
                        imported: data.imported,
                        updated: data.updated,
                        errors: data.errors
                    });

                    if (data.isComplete) {
                        clearInterval(interval);

                        subscriber.complete();
                    }
                })
                .catch((error) => {
                    clearInterval(interval);

                    subscriber.error(error);
                });
        }, 500);
    }
}