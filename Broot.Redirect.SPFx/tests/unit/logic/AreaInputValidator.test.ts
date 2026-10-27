import { isInputValid, prepareAreaInput } from '../../../src/webparts/brtqmRedirectRules/logic/AreaInputValidator';
import { computeSourceKey } from '../../../src/webparts/brtqmRedirectRules/logic/SourceKeyNormalizer';
import { SourceUrlRow } from '../../../src/webparts/brtqmRedirectRules/types';

function row(rowId: string, sourceUrl: string, overrides: Partial<SourceUrlRow> = {}): SourceUrlRow {
  return { rowId, sourceUrl, removed: false, ...overrides };
}

describe('AreaInputValidator', () => {
  it('accepts a complete new area and computes keys', async () => {
    const prepared = await prepareAreaInput({
      isNewArea: true,
      areaName: 'Finanzen',
      targetUrl: '/sites/qm/finanzen',
      rows: [row('r1', ' https://old/ims/a.aspx?x=1 '), row('r2', 'https://old/ims/b.aspx')]
    });

    expect(isInputValid(prepared.validation)).toBe(true);
    expect(prepared.rows[0].sourceUrl).toBe('https://old/ims/a.aspx?x=1');
    expect(prepared.rows[0].sourceKey).toBe(await computeSourceKey('https://old/ims/a.aspx?x=1'));
  });

  it('rejects a new area without name, target URL and source URLs', async () => {
    const prepared = await prepareAreaInput({ isNewArea: true, areaName: ' ', targetUrl: '', rows: [] });

    expect(prepared.validation).toEqual({
      areaNameMissing: true,
      targetUrlIssue: 'required',
      noSourceUrls: true,
      rowIssues: {}
    });
    expect(isInputValid(prepared.validation)).toBe(false);
  });

  it('rejects an invalid target URL', async () => {
    const prepared = await prepareAreaInput({
      isNewArea: true, areaName: 'A', targetUrl: 'sites/qm', rows: [row('r1', 'https://old/a')]
    });

    expect(prepared.validation.targetUrlIssue).toBe('invalidTargetUrl');
  });

  it('marks empty and invalid source URLs per row', async () => {
    const prepared = await prepareAreaInput({
      isNewArea: true,
      areaName: 'A',
      targetUrl: 'https://new/a',
      rows: [row('r1', ''), row('r2', '/ims/a.aspx'), row('r3', 'https://old/ok')]
    });

    expect(prepared.validation.rowIssues).toEqual({
      r1: { kind: 'required' },
      r2: { kind: 'invalidSourceUrl' }
    });
  });

  it('marks all rows of the same save that share a normalized key', async () => {
    const prepared = await prepareAreaInput({
      isNewArea: true,
      areaName: 'A',
      targetUrl: 'https://new/a',
      rows: [row('r1', 'https://host1/IMS/a.aspx?x=1&y=2'), row('r2', 'http://host2/ims/a.aspx?y=2&x=1'), row('r3', 'https://old/b')]
    });

    expect(prepared.validation.rowIssues).toEqual({
      r1: { kind: 'duplicateInSave' },
      r2: { kind: 'duplicateInSave' }
    });
  });

  it('ignores removed rows in validation and duplicate check', async () => {
    const prepared = await prepareAreaInput({
      isNewArea: false,
      areaName: 'A',
      targetUrl: 'https://new/a',
      rows: [row('r1', 'https://old/a', { itemId: 1, removed: true }), row('r2', 'https://old/a')]
    });

    expect(isInputValid(prepared.validation)).toBe(true);
  });

  it('allows removing every source URL of an existing area', async () => {
    const prepared = await prepareAreaInput({
      isNewArea: false,
      areaName: '',
      targetUrl: '',
      rows: [row('r1', 'https://old/a', { itemId: 1, removed: true })]
    });

    expect(isInputValid(prepared.validation)).toBe(true);
  });
});
