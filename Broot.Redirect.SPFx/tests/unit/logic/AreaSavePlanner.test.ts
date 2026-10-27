import {
  applySaveResults,
  buildAreaSnapshot,
  keysRequiringDuplicateCheck,
  planAreaSave
} from '../../../src/webparts/brtqmRedirectRules/logic/AreaSavePlanner';
import { PreparedRow, SaveOperation } from '../../../src/webparts/brtqmRedirectRules/types';
import { entry } from '../testData';

const target = 'https://tenant.sharepoint.com/sites/qm/finanzen';

function prepared(rowId: string, id: number | undefined, overrides: Partial<PreparedRow> = {}): PreparedRow {
  return {
    rowId,
    itemId: id,
    sourceUrl: id !== undefined ? `https://old.example/ims/${id}.aspx` : 'https://old.example/new.aspx',
    sourceKey: id !== undefined ? `v1:key-${id}` : 'v1:key-new',
    removed: false,
    ...overrides
  };
}

describe('AreaSavePlanner', () => {
  it('creates one entry per source URL of a new area with area and target URL', () => {
    const operations = planAreaSave([], 'Finanzen', target, [
      prepared('a', undefined, { sourceUrl: 'https://old/a?x=1', sourceKey: 'v1:a' }),
      prepared('b', undefined, { sourceUrl: 'https://old/b', sourceKey: 'v1:b' })
    ]);

    expect(operations).toEqual([
      { kind: 'create', rowId: 'a', fields: { brtqmArea: 'Finanzen', brtqmSourceUrl: 'https://old/a?x=1', brtqmTargetUrl: target, brtqmSourceKey: 'v1:a' } },
      { kind: 'create', rowId: 'b', fields: { brtqmArea: 'Finanzen', brtqmSourceUrl: 'https://old/b', brtqmTargetUrl: target, brtqmSourceKey: 'v1:b' } }
    ]);
  });

  it('plans nothing for an unchanged area', () => {
    expect(planAreaSave([entry(1), entry(2)], 'Finanzen', target, [prepared('a', 1), prepared('b', 2)])).toEqual([]);
  });

  it('adds, edits and recycles single source URLs', () => {
    const operations = planAreaSave([entry(1), entry(2), entry(3, { etag: '"5"' })], 'Finanzen', target, [
      prepared('a', 1),
      prepared('b', 2, { removed: true }),
      prepared('c', 3, { sourceUrl: 'https://old/changed', sourceKey: 'v1:changed' }),
      prepared('d', undefined)
    ]);

    expect(operations).toEqual([
      { kind: 'recycle', rowId: 'b', itemId: 2 },
      { kind: 'update', rowId: 'c', itemId: 3, etag: '"5"', fields: { brtqmSourceUrl: 'https://old/changed', brtqmSourceKey: 'v1:changed' } },
      { kind: 'create', rowId: 'd', fields: { brtqmArea: 'Finanzen', brtqmSourceUrl: 'https://old.example/new.aspx', brtqmTargetUrl: target, brtqmSourceKey: 'v1:key-new' } }
    ]);
  });

  it('updates every entry of the area when renaming and changing the target URL', () => {
    const operations = planAreaSave([entry(1), entry(2)], 'Finanzen & Controlling', 'https://new/target', [
      prepared('a', 1),
      prepared('b', 2)
    ]);

    expect(operations).toEqual([
      { kind: 'update', rowId: 'a', itemId: 1, etag: '"1"', fields: { brtqmArea: 'Finanzen & Controlling', brtqmTargetUrl: 'https://new/target' } },
      { kind: 'update', rowId: 'b', itemId: 2, etag: '"1"', fields: { brtqmArea: 'Finanzen & Controlling', brtqmTargetUrl: 'https://new/target' } }
    ]);
  });

  it('ignores a removed row that was never saved', () => {
    expect(planAreaSave([], 'Finanzen', target, [prepared('a', undefined, { removed: true })])).toEqual([]);
  });

  it('checks keys of new entries and of entries with a changed key only', () => {
    const operations: SaveOperation[] = [
      { kind: 'create', rowId: 'n', fields: { brtqmArea: 'A', brtqmSourceUrl: 'u', brtqmTargetUrl: 't', brtqmSourceKey: 'v1:new' } },
      { kind: 'update', rowId: 'c', itemId: 1, etag: '"1"', fields: { brtqmSourceKey: 'v1:changed' } },
      { kind: 'update', rowId: 'r', itemId: 2, etag: '"1"', fields: { brtqmArea: 'B' } }
    ];

    expect(keysRequiringDuplicateCheck([entry(1), entry(2)], operations)).toEqual({ 'v1:new': 'n', 'v1:changed': 'c' });
  });

  it('builds the snapshot from the entry with the smallest id', () => {
    const snapshot = buildAreaSnapshot(
      [entry(5, { area: 'finanzen', targetUrl: 'https://b' }), entry(2, { area: 'Finanzen', targetUrl: 'https://a' })],
      'x',
      'y'
    );

    expect(snapshot.area).toBe('Finanzen');
    expect(snapshot.targetUrl).toBe('https://a');
    expect(snapshot.entries.map((e) => e.id)).toEqual([2, 5]);
  });

  it('carries saved changes into the expected state and keeps failed rows', () => {
    const rows = [
      prepared('keep', 1),
      prepared('gone', 2, { removed: true }),
      prepared('new', undefined),
      prepared('failed', 3, { sourceUrl: 'https://old/x', sourceKey: 'v1:x' })
    ];
    const operations = planAreaSave([entry(1), entry(2), entry(3)], 'Finanzen', target, rows);

    const applied = applySaveResults([entry(1), entry(2), entry(3)], rows, [
      { operation: operations[0], succeeded: true },
      { operation: operations[1], succeeded: true, entry: entry(9, { sourceUrl: 'https://old.example/new.aspx', etag: '"1"' }) },
      { operation: operations[2], succeeded: false, failure: 'conflict' }
    ]);

    expect(applied.entries.map((e) => e.id).sort()).toEqual([1, 3, 9]);
    expect(applied.rows.map((r) => [r.rowId, r.itemId])).toEqual([['keep', 1], ['new', 9], ['failed', 3]]);
    expect(applied.rowIssues).toEqual({ failed: { kind: 'notSaved', reason: 'conflict' } });
  });

  it('plans only the missing changes when saving again after a partial failure', () => {
    const rows = [prepared('a', 1), prepared('b', 2)];
    const first = planAreaSave([entry(1), entry(2)], 'Neu', target, rows);
    const applied = applySaveResults([entry(1), entry(2)], rows, [
      { operation: first[0], succeeded: true, entry: entry(1, { area: 'Neu', etag: '"2"' }) },
      { operation: first[1], succeeded: false, failure: 'error' }
    ]);

    const retry = planAreaSave(applied.entries, 'Neu', target, applied.rows.map((r) => r as PreparedRow));

    expect(retry).toEqual([{ kind: 'update', rowId: 'b', itemId: 2, etag: '"1"', fields: { brtqmArea: 'Neu' } }]);
  });
});
