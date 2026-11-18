import {
  applyStatusToAll,
  commonStatus,
  DEFAULT_REDIRECT_RULE_STATUS,
  effectiveStatus,
  isExportable,
  REDIRECT_RULE_STATUSES
} from '../../../src/webparts/brtqmRedirectRules/logic/RedirectRuleStatus';
import { SourceUrlRow } from '../../../src/webparts/brtqmRedirectRules/types';

function row(rowId: string, overrides: Partial<SourceUrlRow> = {}): SourceUrlRow {
  return { rowId, sourceUrl: `https://old/${rowId}`, removed: false, ...overrides };
}

describe('RedirectRuleStatus', () => {
  it('lists the five statuses in the order of the workflow with «vorbereitet» as default', () => {
    expect(REDIRECT_RULE_STATUSES).toEqual(['vorbereitet', 'IIS-Umleitung offen', 'Testing', 'Fehlerhaft', 'aktiv']);
    expect(DEFAULT_REDIRECT_RULE_STATUS).toBe('vorbereitet');
  });

  it.each(REDIRECT_RULE_STATUSES)('keeps the known status %j', (status) => {
    expect(effectiveStatus(status)).toBe(status);
  });

  it.each([undefined, '', 'Aktiv', 'unbekannt'])('treats %j as «vorbereitet»', (raw) => {
    expect(effectiveStatus(raw)).toBe('vorbereitet');
  });

  it('exports every status except «vorbereitet», empty and unknown values', () => {
    expect(REDIRECT_RULE_STATUSES.filter(isExportable)).toEqual(['IIS-Umleitung offen', 'Testing', 'Fehlerhaft', 'aktiv']);
    expect(isExportable('')).toBe(false);
    expect(isExportable(undefined)).toBe(false);
  });

  it('sets the status of all rows except removed ones, new rows included', () => {
    const rows = applyStatusToAll([
      row('a', { itemId: 1 }),
      row('b', { itemId: 2, removed: true, status: 'Testing' }),
      row('c', { status: 'vorbereitet' })
    ], 'aktiv');

    expect(rows.map((item) => item.status)).toEqual(['aktiv', 'Testing', 'aktiv']);
  });

  it('reports the common status of all rows, falling back to the stored status', () => {
    const stored: Record<number, string> = { 1: 'Testing', 2: '' };
    const storedStatus = (item: SourceUrlRow): string | undefined => (item.itemId !== undefined ? stored[item.itemId] : undefined);

    expect(commonStatus([row('a', { itemId: 1 }), row('b', { itemId: 2, status: 'Testing' })], storedStatus)).toBe('Testing');
    expect(commonStatus([row('a', { itemId: 1 }), row('b', { itemId: 2 })], storedStatus)).toBeUndefined();
    expect(commonStatus([row('a', { itemId: 2 }), row('b', { removed: true, status: 'aktiv' })], storedStatus)).toBe('vorbereitet');
    expect(commonStatus([], storedStatus)).toBeUndefined();
  });
});
