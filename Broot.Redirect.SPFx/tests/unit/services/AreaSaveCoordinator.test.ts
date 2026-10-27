import { computeSourceKey } from '../../../src/webparts/brtqmRedirectRules/logic/SourceKeyNormalizer';
import { AreaSaveCoordinator, AreaSaveRequest } from '../../../src/webparts/brtqmRedirectRules/services/AreaSaveCoordinator';
import { RedirectRulesService } from '../../../src/webparts/brtqmRedirectRules/services/RedirectRulesService';
import { AreaSnapshot, SaveResult, SourceUrlRow } from '../../../src/webparts/brtqmRedirectRules/types';
import { FakeItem, FakeSharePoint, httpError } from '../mocks/FakeSharePoint';

const origin = 'https://tenant.sharepoint.com';
const target = `${origin}/sites/qm/finanzen`;

interface Context {
  fake: FakeSharePoint;
  service: RedirectRulesService;
  coordinator: AreaSaveCoordinator;
}

function setup(): Context {
  const fake = new FakeSharePoint();
  const service = new RedirectRulesService(fake.asSpfi(), '/sites/qm/Lists/brtqmRedirectRules');
  return { fake, service, coordinator: new AreaSaveCoordinator(service, origin) };
}

async function seedArea(fake: FakeSharePoint, area: string, sourceUrls: string[], targetUrl: string = target): Promise<FakeItem[]> {
  const values = await Promise.all(sourceUrls.map(async (sourceUrl) => ({
    brtqmArea: area,
    brtqmSourceUrl: sourceUrl,
    brtqmTargetUrl: targetUrl,
    brtqmSourceKey: await computeSourceKey(sourceUrl)
  })));
  return fake.seed(values);
}

async function load(context: Context, area: string): Promise<{ expected: AreaSnapshot; rows: SourceUrlRow[] }> {
  const entries = await context.service.getAreaEntries(area);
  const sorted = entries.slice().sort((left, right) => left.id - right.id);
  return {
    expected: { area: sorted[0].area, targetUrl: sorted[0].targetUrl, entries: sorted },
    rows: sorted.map((entry) => ({ rowId: `row-${entry.id}`, itemId: entry.id, sourceUrl: entry.sourceUrl, removed: false }))
  };
}

function newRow(rowId: string, sourceUrl: string): SourceUrlRow {
  return { rowId, sourceUrl, removed: false };
}

function completed(result: SaveResult): Extract<SaveResult, { kind: 'completed' }> {
  if (result.kind !== 'completed') {
    throw new Error(`Expected completed, got ${result.kind}`);
  }
  return result;
}

describe('AreaSaveCoordinator', () => {
  describe('new area', () => {
    it('creates one entry per source URL with area, unchanged source URL and absolute target URL', async () => {
      const context = setup();
      const sourceUrl = 'https://old.example/ims/A.aspx?RootFolder=%2Fims%2FA&FolderCTID=0x012&View=%7BABC%7D';

      const result = completed(await context.coordinator.save({
        areaName: '  Finanzen ',
        targetUrl: '/sites/qm/finanzen?x=1#teil',
        rows: [newRow('a', sourceUrl), newRow('b', 'https://old.example/ims/b.aspx')]
      }));

      expect(context.fake.items.map((item) => [item.brtqmArea, item.brtqmSourceUrl, item.brtqmTargetUrl])).toEqual([
        ['Finanzen', sourceUrl, `${origin}/sites/qm/finanzen?x=1#teil`],
        ['Finanzen', 'https://old.example/ims/b.aspx', `${origin}/sites/qm/finanzen?x=1#teil`]
      ]);
      expect(context.fake.items[0].brtqmSourceKey).toBe(await computeSourceKey(sourceUrl));
      expect(result.rowIssues).toEqual({});
      expect(result.rows.map((row) => row.itemId)).toEqual([1, 2]);
      expect(result.snapshot.entries).toHaveLength(2);
    });

    it('rejects missing name, target URL and source URLs without writing', async () => {
      const context = setup();

      const result = await context.coordinator.save({ areaName: '', targetUrl: '', rows: [] });

      expect(result).toMatchObject({ kind: 'invalid', validation: { areaNameMissing: true, targetUrlIssue: 'required', noSourceUrls: true } });
      expect(context.fake.writes).toHaveLength(0);
    });

    it('rejects a name that matches an existing area regardless of case and whitespace', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a']);

      const result = await context.coordinator.save({ areaName: ' FINANZEN ', targetUrl: target, rows: [newRow('a', 'https://old/z')] });

      expect(result).toEqual({ kind: 'areaNameTaken', existingArea: 'Finanzen' });
      expect(context.fake.writes).toHaveLength(0);
    });

    it('rejects a source URL whose key exists in another area and names that area', async () => {
      const context = setup();
      await seedArea(context.fake, 'Audit', ['http://server1/IMS/a.aspx?y=2&x=1']);

      const result = await context.coordinator.save({
        areaName: 'Finanzen',
        targetUrl: target,
        rows: [newRow('a', 'https://server2/ims/a.aspx?x=1&y=2'), newRow('b', 'https://old/b')]
      });

      expect(result).toEqual({ kind: 'duplicates', rowIssues: { a: { kind: 'duplicateInList', area: 'Audit' } } });
      expect(context.fake.writes).toHaveLength(0);
    });

    it('rejects two source URLs of the same save with the same key', async () => {
      const context = setup();

      const result = await context.coordinator.save({
        areaName: 'Finanzen',
        targetUrl: target,
        rows: [newRow('a', 'https://old/a/'), newRow('b', 'https://other/A')]
      });

      expect(result).toMatchObject({ kind: 'invalid', validation: { rowIssues: { a: { kind: 'duplicateInSave' }, b: { kind: 'duplicateInSave' } } } });
    });

    it('rejects saving without edit permission', async () => {
      const context = setup();
      context.fake.permissions = [];

      const result = await context.coordinator.save({ areaName: 'Finanzen', targetUrl: target, rows: [newRow('a', 'https://old/a')] });

      expect(result).toEqual({ kind: 'forbidden' });
      expect(context.fake.writes).toHaveLength(0);
    });
  });

  describe('existing area', () => {
    it('adds, edits and removes source URLs and keeps identifiers of edited entries', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a', 'https://old/b', 'https://old/c']);
      await seedArea(context.fake, 'Audit', ['https://old/audit'], 'https://new/audit');
      const loaded = await load(context, 'Finanzen');
      const auditBefore = JSON.stringify(context.fake.items.filter((item) => item.brtqmArea === 'Audit'));

      const result = completed(await context.coordinator.save({
        expected: loaded.expected,
        areaName: 'Finanzen',
        targetUrl: target,
        rows: [
          loaded.rows[0],
          { ...loaded.rows[1], sourceUrl: 'https://old/b-changed?x=1' },
          { ...loaded.rows[2], removed: true },
          newRow('new', 'https://old/d')
        ]
      }));

      const finanzen = context.fake.items.filter((item) => item.brtqmArea === 'Finanzen');
      expect(finanzen.map((item) => [item.Id, item.UniqueId, item.brtqmSourceUrl, item.brtqmTargetUrl])).toEqual([
        [1, 'guid-1', 'https://old/a', target],
        [2, 'guid-2', 'https://old/b-changed?x=1', target],
        [5, 'guid-5', 'https://old/d', target]
      ]);
      expect(context.fake.items[1].brtqmSourceKey).toBe(await computeSourceKey('https://old/b-changed?x=1'));
      expect(context.fake.writes.filter((write) => write.kind === 'recycle')).toEqual([{ kind: 'recycle', itemId: 3 }]);
      expect(JSON.stringify(context.fake.items.filter((item) => item.brtqmArea === 'Audit'))).toBe(auditBefore);
      expect(result.rows.map((row) => row.rowId)).toEqual(['row-1', 'row-2', 'new']);
    });

    it('renames the area and changes the target URL in all entries without changing identifiers', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a', 'https://old/b']);
      await seedArea(context.fake, 'Audit', ['https://old/audit'], 'https://new/audit');
      const loaded = await load(context, 'Finanzen');

      completed(await context.coordinator.save({
        expected: loaded.expected,
        areaName: 'Finanzen & Controlling',
        targetUrl: 'https://new/fc',
        rows: loaded.rows
      }));

      expect(context.fake.items.map((item) => [item.Id, item.UniqueId, item.brtqmArea, item.brtqmTargetUrl])).toEqual([
        [1, 'guid-1', 'Finanzen & Controlling', 'https://new/fc'],
        [2, 'guid-2', 'Finanzen & Controlling', 'https://new/fc'],
        [3, 'guid-3', 'Audit', 'https://new/audit']
      ]);
    });

    it('allows changing only the spelling of the own area name', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a']);
      const loaded = await load(context, 'Finanzen');

      const result = await context.coordinator.save({ expected: loaded.expected, areaName: 'FINANZEN', targetUrl: target, rows: loaded.rows });

      expect(result.kind).toBe('completed');
      expect(context.fake.items[0].brtqmArea).toBe('FINANZEN');
    });

    it('rejects renaming to another existing area and changes nothing', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a']);
      await seedArea(context.fake, 'Audit', ['https://old/audit']);
      const loaded = await load(context, 'Finanzen');

      const result = await context.coordinator.save({ expected: loaded.expected, areaName: 'audit', targetUrl: target, rows: loaded.rows });

      expect(result).toEqual({ kind: 'areaNameTaken', existingArea: 'Audit' });
      expect(context.fake.writes).toHaveLength(0);
    });

    it('rejects the save when someone else changed the area since loading and changes nothing', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a', 'https://old/b']);
      const loaded = await load(context, 'Finanzen');
      context.fake.modify(2, { brtqmSourceUrl: 'https://old/b2' });

      const result = await context.coordinator.save({
        expected: loaded.expected,
        areaName: 'Finanzen',
        targetUrl: 'https://new/other',
        rows: loaded.rows
      });

      expect(result).toEqual({ kind: 'concurrentChange' });
      expect(context.fake.writes).toHaveLength(0);
    });

    it('detects entries added to the area by someone else', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a']);
      const loaded = await load(context, 'Finanzen');
      await seedArea(context.fake, 'finanzen', ['https://old/foreign']);

      const result = await context.coordinator.save({ expected: loaded.expected, areaName: 'Finanzen', targetUrl: 'https://new/x', rows: loaded.rows });

      expect(result).toEqual({ kind: 'concurrentChange' });
    });

    it('does not treat own saved changes as concurrent changes on the next save', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a']);
      const loaded = await load(context, 'Finanzen');
      const first = completed(await context.coordinator.save({
        expected: loaded.expected, areaName: 'Finanzen', targetUrl: 'https://new/1', rows: loaded.rows.concat(newRow('n', 'https://old/n'))
      }));

      const second = await context.coordinator.save({
        expected: first.snapshot, areaName: 'Finanzen', targetUrl: 'https://new/2', rows: first.rows
      });

      expect(second.kind).toBe('completed');
      expect(context.fake.items.map((item) => item.brtqmTargetUrl)).toEqual(['https://new/2', 'https://new/2']);
    });

    it('allows keeping an unchanged source URL whose key belongs to the own area', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a', 'https://old/b']);
      const loaded = await load(context, 'Finanzen');

      const result = await context.coordinator.save({
        expected: loaded.expected,
        areaName: 'Finanzen',
        targetUrl: target,
        rows: [{ ...loaded.rows[0], sourceUrl: 'https://old/b' }, { ...loaded.rows[1], sourceUrl: 'https://old/a' }]
      });

      expect(result.kind).toBe('completed');
    });

    it('rejects an edited source URL whose key exists in another area', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a']);
      await seedArea(context.fake, 'Audit', ['https://old/audit']);
      const loaded = await load(context, 'Finanzen');

      const result = await context.coordinator.save({
        expected: loaded.expected,
        areaName: 'Finanzen',
        targetUrl: target,
        rows: [{ ...loaded.rows[0], sourceUrl: 'https://elsewhere/AUDIT/' }]
      });

      expect(result).toEqual({ kind: 'duplicates', rowIssues: { 'row-1': { kind: 'duplicateInList', area: 'Audit' } } });
    });

    it('removes the area when all source URLs are removed', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a']);
      const loaded = await load(context, 'Finanzen');

      const result = completed(await context.coordinator.save({
        expected: loaded.expected, areaName: 'Finanzen', targetUrl: target, rows: [{ ...loaded.rows[0], removed: true }]
      }));

      expect(context.fake.items).toHaveLength(0);
      expect(result.snapshot.entries).toHaveLength(0);
    });

    it('reports source URLs that were not saved and completes them on the next save', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a', 'https://old/b', 'https://old/c']);
      const loaded = await load(context, 'Finanzen');
      context.fake.failWrite = (request) => (request.itemId === 2 ? httpError(503) : undefined);
      const request: AreaSaveRequest = { expected: loaded.expected, areaName: 'Neu', targetUrl: 'https://new/n', rows: loaded.rows };

      const first = completed(await context.coordinator.save(request));

      expect(first.rowIssues).toEqual({ 'row-2': { kind: 'notSaved', reason: 'error' } });
      expect(context.fake.items.map((item) => item.brtqmArea)).toEqual(['Neu', 'Finanzen', 'Neu']);

      context.fake.failWrite = () => undefined;
      const writesBefore = context.fake.writes.length;
      const second = completed(await context.coordinator.save({ ...request, expected: first.snapshot, rows: first.rows }));

      expect(second.rowIssues).toEqual({});
      expect(context.fake.writes.slice(writesBefore)).toEqual([{ kind: 'update', itemId: 2, fields: { brtqmArea: 'Neu', brtqmTargetUrl: 'https://new/n' } }]);
      expect(context.fake.items.map((item) => [item.brtqmArea, item.brtqmTargetUrl])).toEqual([
        ['Neu', 'https://new/n'], ['Neu', 'https://new/n'], ['Neu', 'https://new/n']
      ]);
    });

    it('reports nothing to save when nothing changed', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a']);
      const loaded = await load(context, 'Finanzen');

      expect(await context.coordinator.save({ expected: loaded.expected, areaName: 'Finanzen', targetUrl: target, rows: loaded.rows }))
        .toEqual({ kind: 'nothingToSave' });
    });

    it('rejects removing source URLs without delete permission', async () => {
      const context = setup();
      await seedArea(context.fake, 'Finanzen', ['https://old/a', 'https://old/b']);
      const loaded = await load(context, 'Finanzen');
      context.fake.permissions = [2, 3];

      const result = await context.coordinator.save({
        expected: loaded.expected, areaName: 'Finanzen', targetUrl: target, rows: [loaded.rows[0], { ...loaded.rows[1], removed: true }]
      });

      expect(result).toEqual({ kind: 'forbidden' });
    });
  });
});
