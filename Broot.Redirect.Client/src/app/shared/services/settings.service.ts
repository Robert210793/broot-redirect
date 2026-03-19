import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AppSettings } from '../models/app-settings';

@Injectable({
    providedIn: 'root'
})
export class SettingsService {

    constructor(private httpClient: HttpClient) {}

    getSettings(): Observable<AppSettings> {
        return this.httpClient.get<AppSettings>('/api/settings');
    }

    updateSettings(partial: Partial<AppSettings>): Observable<AppSettings> {
        return this.httpClient.put<AppSettings>('/api/settings', partial);
    }
}