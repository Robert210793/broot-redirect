export interface RedirectRule {
    id: string;
    matcher: string;
    targetUrl: string | null;
    redirectType: RedirectType;
    source: RuleSource;
    infoText: string | null;
    autoRedirect: boolean;
    discardQueryParams: boolean;
    forwardQueryParams: boolean;
    keptQueryParams: KeptQueryParam[];
    staticQueryParams: StaticQueryParam[];
    searchAndReplace: SearchAndReplaceEntry[];
    createdAt: string;
}

export type RedirectType = 'wildcard' | 'partial' | 'domain' | 'regex';

export type RuleSource = 'manual' | 'import' | 'unknown';

export type MatchQualityLevel = 'red' | 'yellow' | 'green';

export interface KeptQueryParam {
    keyPattern: string;
    valuePattern: string | null;
    targetKey: string | null;
    skipEncoding: boolean;
}

export interface StaticQueryParam {
    key: string;
    value: string;
    skipEncoding: boolean;
}

export interface SearchAndReplaceEntry {
    search: string;
    replace: string;
    caseSensitive: boolean;
}

export interface PaginatedRulesResponse {
    rules: RedirectRule[];
    total: number;
    totalPages: number;
    currentPage: number;
}

export interface CreateRuleRequest {
    matcher: string;
    targetUrl?: string | null;
    redirectType: RedirectType;
    infoText?: string | null;
    autoRedirect: boolean;
    discardQueryParams: boolean;
    forwardQueryParams: boolean;
    keptQueryParams: KeptQueryParam[];
    staticQueryParams: StaticQueryParam[];
    searchAndReplace: SearchAndReplaceEntry[];
}

export interface UpdateRuleRequest {
    matcher?: string;
    targetUrl?: string | null;
    redirectType?: RedirectType;
    infoText?: string | null;
    autoRedirect?: boolean;
    discardQueryParams?: boolean;
    forwardQueryParams?: boolean;
    keptQueryParams?: KeptQueryParam[];
    staticQueryParams?: StaticQueryParam[];
    searchAndReplace?: SearchAndReplaceEntry[];
}

export interface BulkDeleteRequest {
    ids: string[];
}

export interface BulkDeleteResponse {
    deleted: number;
    notFound: number;
}

export interface ImportResult {
    imported: number;
    updated: number;
    errors: string[];
}

export interface ImportPreviewEntry {
    matcher: string;
    targetUrl: string | null;
    redirectType: string | null;
    infoText: string | null;
    status: 'new' | 'update' | 'invalid';
    reason: string | null;
    existingRuleId: string | null;
}

export interface ImportPreviewResponse {
    total: number;
    limit: number;
    isLimited: boolean;
    preview: ImportPreviewEntry[];
    counts: {
        new: number;
        update: number;
        invalid: number;
    };
}

export interface ResolveResponse {
    rule: RedirectRule | null;
    resolvedUrl: string | null;
    matchQuality: number;

    // Phase 1: quality percentage (0-100) and traffic-light level
    quality: number;
    level: MatchQualityLevel;

    // Phase 3: smart search fallback fields
    isSmartSearchFallback: boolean;
    fallbackSearchUrl: string | null;
}

export interface AuthStatusResponse {
    isAuthenticated: boolean;
    loginTime: number | null;
}

export interface LoginResponse {
    success: boolean;
}

export interface RulesQueryParams {
    page: number;
    limit: number;
    search: string;
    sortBy: string;
    sortOrder: 'asc' | 'desc';
}

// -- Blocked IP management (Phase 5.2) --

export interface BlockedIpInfo {
    ip: string;
    attempts: number;
    blockedUntil: string;
}

// -- URL Validation/Trace (Phase 5.1) --

export interface UrlTraceStep {
    type: string;
    description: string;
    before: string | null;
    after: string | null;
}

export interface ValidateUrlResult {
    url: string;
    matched: boolean;
    ruleId: string | null;
    matcher: string | null;
    redirectType: string | null;
    score: number;
    quality: number;
    level: MatchQualityLevel;
    resolvedUrl: string | null;
    error: string | null;
    trace: UrlTraceStep[] | null;
}

export interface ValidateUrlsResponse {
    total: number;
    matched: number;
    unmatched: number;
    results: ValidateUrlResult[];
}