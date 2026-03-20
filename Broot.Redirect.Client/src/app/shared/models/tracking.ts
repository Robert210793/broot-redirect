export interface TrackRequest {
    oldUrl: string;
    newUrl: string;
    path: string;
    userAgent?: string;
    referrer?: string;
    ruleId?: string;
    matchQuality: number;
    feedback?: string;
    redirectStrategy?: string;
}

export interface TrackResponse {
    id: string;
}

export interface FeedbackRequest {
    trackingId: string;
    feedback: 'OK' | 'NOK';
    userProposedUrl?: string;
}

export interface StatsResponse {
    totalVisits: number;
    matchedVisits: number;
    unmatchedVisits: number;
    matchRate: number;
    feedbackOk: number;
    feedbackNok: number;
    feedbackAutoRedirect: number;
    feedbackNone: number;
    topRules: TopRuleStat[];
}

export interface TopRuleStat {
    ruleId: string;
    count: number;
}

export interface TrackingEntry {
    id: string;
    oldUrl: string;
    newUrl: string;
    path: string;
    timestamp: string;
    userAgent?: string;
    referrer?: string;
    ruleId?: string;
    matchQuality: number;
    feedback?: string;
    userProposedUrl?: string;
    redirectStrategy?: string;
}

export interface PaginatedTrackingResponse {
    entries: TrackingEntry[];
    total: number;
    totalPages: number;
    currentPage: number;
}

// -- Satisfaction Trend (Phase 5.4) --

export interface TrendDataPoint {
    date: string;
    ok: number;
    nok: number;
    autoRedirect: number;
    none: number;
    total: number;
}

export interface TrendResponse {
    days: number;
    aggregation: string;
    dataPoints: TrendDataPoint[];
}

// -- Advanced Stats Filtering (Phase 5.5) --

export interface StatsFilterParams {
    qualityMin?: number;
    qualityMax?: number;
    feedbackType?: string;
    ruleId?: string;
}