import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ResolveResponse } from '../models/redirect-rule';

@Injectable({
    providedIn: 'root'
})
export class RedirectService {

    constructor(private httpClient: HttpClient) {}

    resolve(path: string): Observable<ResolveResponse> {
        const params = new HttpParams().set('path', path);

        return this.httpClient.get<ResolveResponse>('/api/redirect/resolve', { params });
    }
}