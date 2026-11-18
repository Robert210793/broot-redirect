import { RedirectRuleStatus, SourceUrlRow } from '../types';

export const REDIRECT_RULE_STATUSES: RedirectRuleStatus[] = ['vorbereitet', 'IIS-Umleitung offen', 'Testing', 'Fehlerhaft', 'aktiv'];
export const DEFAULT_REDIRECT_RULE_STATUS: RedirectRuleStatus = 'vorbereitet';

export function isKnownStatus(raw: string | undefined): raw is RedirectRuleStatus {
  return REDIRECT_RULE_STATUSES.indexOf(raw as RedirectRuleStatus) >= 0;
}

export function effectiveStatus(raw: string | undefined): RedirectRuleStatus {
  return isKnownStatus(raw) ? raw : DEFAULT_REDIRECT_RULE_STATUS;
}

export function isExportable(raw: string | undefined): boolean {
  return effectiveStatus(raw) !== DEFAULT_REDIRECT_RULE_STATUS;
}

export function applyStatusToAll(rows: SourceUrlRow[], status: RedirectRuleStatus): SourceUrlRow[] {
  return rows.map((row) => (row.removed ? row : { ...row, status }));
}

export function commonStatus(rows: SourceUrlRow[], storedStatus: (row: SourceUrlRow) => string | undefined): RedirectRuleStatus | undefined {
  const statuses = rows
    .filter((row) => !row.removed)
    .map((row) => effectiveStatus(row.status !== undefined ? row.status : storedStatus(row)));

  return statuses.length > 0 && statuses.every((status) => status === statuses[0]) ? statuses[0] : undefined;
}
