import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
    TrackRequest,
    TrackResponse,
    FeedbackRequest,
    StatsResponse,
    PaginatedTrackingResponse
} from '../models/tracking';

@Injectable({
    providedIn: 'root'
})
export class TrackingService {

    constructor(private httpClient: HttpClient) {}

    /**
     * Records a tracking entry when the info page displays a resolved URL.
     * Public endpoint -- no auth required.
     */
    track(request: TrackRequest): Observable<TrackResponse> {
        return this.httpClient.post<TrackResponse>('/api/track', request);
    }

    /**
     * Updates an existing tracking entry with user feedback (OK/NOK).
     * Public endpoint -- no auth required.
     */
    feedback(request: FeedbackRequest): Observable<{ success: boolean }> {
        return this.httpClient.post<{ success: boolean }>('/api/feedback', request);
    }

    /**
     * Retrieves aggregated statistics. Admin-protected.
     */
    getStats(): Observable<StatsResponse> {
        return this.httpClient.get<StatsResponse>('/api/stats');
    }

    /**
     * Retrieves paginated raw tracking entries. Admin-protected.
     */
    getEntries(page: number, limit: number, search?: string): Observable<PaginatedTrackingResponse> {
        let params = new HttpParams()
            .set('page', page.toString())
            .set('limit', limit.toString());

        if (search) {
            params = params.set('search', search);
        }

        return this.httpClient.get<PaginatedTrackingResponse>('/api/stats/entries', { params });
    }
}