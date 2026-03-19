export interface RedirectRule {
    id: string;
    matcher: string;
    targetUrl: string | null;
    redirectType: RedirectType;
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