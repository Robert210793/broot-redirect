import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { GlobalRule, CreateGlobalRuleRequest, UpdateGlobalRuleRequest } from '../models/global-rule';

@Injectable({
    providedIn: 'root'
})
export class GlobalRulesService {

    private readonly baseUrl = '/api/global-rules';

    constructor(private httpClient: HttpClient) {}

    getAll(): Observable<GlobalRule[]> {
        return this.httpClient.get<GlobalRule[]>(this.baseUrl);
    }

    getById(id: string): Observable<GlobalRule> {
        return this.httpClient.get<GlobalRule>(`${this.baseUrl}/${id}`);
    }

    create(request: CreateGlobalRuleRequest): Observable<GlobalRule> {
        return this.httpClient.post<GlobalRule>(this.baseUrl, request);
    }

    update(id: string, request: UpdateGlobalRuleRequest): Observable<GlobalRule> {
        return this.httpClient.put<GlobalRule>(`${this.baseUrl}/${id}`, request);
    }

    delete(id: string): Observable<void> {
        return this.httpClient.delete<void>(`${this.baseUrl}/${id}`);
    }
}