import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, catchError, of } from 'rxjs';
import { AuthStatusResponse, BlockedIpInfo, LoginResponse } from '../models/redirect-rule';

@Injectable({
    providedIn: 'root'
})
export class AuthService {

    private authenticated = signal(false);
    private loginTimestamp = signal<number | null>(null);

    public isAuthenticated = computed(() => this.authenticated());

    constructor(private httpClient: HttpClient) {}

    login(password: string): Observable<LoginResponse> {
        return this.httpClient.post<LoginResponse>('/api/auth/login', { password }).pipe(
            tap((response) => {
                if (response.success) {
                    this.authenticated.set(true);
                    this.loginTimestamp.set(Date.now());
                }
            })
        );
    }

    checkStatus(): Observable<AuthStatusResponse> {
        return this.httpClient.get<AuthStatusResponse>('/api/auth/status').pipe(
            tap((response) => {
                this.authenticated.set(response.isAuthenticated);
                this.loginTimestamp.set(response.loginTime);
            }),
            catchError(() => {
                this.authenticated.set(false);
                this.loginTimestamp.set(null);

                return of({ isAuthenticated: false, loginTime: null });
            })
        );
    }

    logout(): Observable<unknown> {
        return this.httpClient.post('/api/auth/logout', {}).pipe(
            tap(() => {
                this.authenticated.set(false);
                this.loginTimestamp.set(null);
            })
        );
    }

    // -- Blocked IP management (Phase 5.2) --

    getBlockedIps(): Observable<BlockedIpInfo[]> {
        return this.httpClient.get<BlockedIpInfo[]>('/api/auth/blocked-ips');
    }

    blockIp(ip: string): Observable<{ success: boolean; ip: string }> {
        return this.httpClient.post<{ success: boolean; ip: string }>('/api/auth/blocked-ips', { ip });
    }

    unblockIp(ip: string): Observable<{ success: boolean; ip: string }> {
        return this.httpClient.delete<{ success: boolean; ip: string }>(`/api/auth/blocked-ips/${encodeURIComponent(ip)}`);
    }

    clearBlockedIps(): Observable<{ success: boolean }> {
        return this.httpClient.delete<{ success: boolean }>('/api/auth/blocked-ips');
    }
}