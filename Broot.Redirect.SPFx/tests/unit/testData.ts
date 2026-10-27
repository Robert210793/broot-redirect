import { RedirectRuleEntry } from '../../src/webparts/brtqmRedirectRules/types';

export function entry(id: number, overrides: Partial<RedirectRuleEntry> = {}): RedirectRuleEntry {
  return {
    id,
    uniqueId: `00000000-0000-0000-0000-${String(id).padStart(12, '0')}`,
    etag: '"1"',
    area: 'Finanzen',
    sourceUrl: `https://old.example/ims/${id}.aspx`,
    targetUrl: 'https://tenant.sharepoint.com/sites/qm/finanzen',
    sourceKey: `v1:key-${id}`,
    ...overrides
  };
}
