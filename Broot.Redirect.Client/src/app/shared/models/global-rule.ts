export interface GlobalRule {
    id: string;
    search: string;
    replace: string;
    caseSensitive: boolean;
    priority: number;
    createdAt: string;
}

export interface CreateGlobalRuleRequest {
    search: string;
    replace: string;
    caseSensitive: boolean;
    priority: number;
}

export interface UpdateGlobalRuleRequest {
    search?: string;
    replace?: string;
    caseSensitive?: boolean;
    priority?: number;
}