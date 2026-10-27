import {
  BATCH_SIZE,
  listUrlForSite,
  odataString,
  PAGE_SIZE,
  RedirectRulesService
} from '../../../src/webparts/brtqmRedirectRules/services/RedirectRulesService';
import { SaveOperation } from '../../../src/webparts/brtqmRedirectRules/types';
import { FakeSharePoint, httpError } from '../mocks/FakeSharePoint';

function setup(): { fake: FakeSharePoint; service: RedirectRulesService } {
  const fake = new FakeSharePoint();
  return { fake, service: new RedirectRulesService(fake.asSpfi(), '/sites/qm/Lists/brtqmRedirectRules') };
}

function create(rowId: string, sourceKey: string): SaveOperation {
  return {
    kind: 'create',
    rowId,
    fields: { brtqmArea: 'Finanzen', brtqmSourceUrl: `https://old/${rowId}`, brtqmTargetUrl: 'https://new/f', brtqmSourceKey: sourceKey }
  };
}

describe('RedirectRulesService', () => {
  it('addresses the list below the site collection', () => {
    expect(listUrlForSite('/sites/qm/')).toBe('/sites/qm/Lists/brtqmRedirectRules');
    expect(listUrlForSite('/')).toBe('/Lists/brtqmRedirectRules');
  });

  it('escapes OData string literals', () => {
    expect(odataString("Mc'Donald")).toBe("'Mc''Donald'");
  });

  it('reports a missing list', async () => {
    const { fake, service } = setup();
    fake.listMissing = true;

    expect(await service.listExists()).toBe(false);
  });

  it('reads all areas page by page beyond 5000 entries', async () => {
    const { fake, service } = setup();
    fake.seed(Array.from({ length: PAGE_SIZE + 3 }, (_, index) => ({ brtqmArea: index % 2 === 0 ? 'Finanzen' : 'audit' })));
    fake.seed([{ brtqmArea: 'Personal ' }]);

    const areas = await service.getAreas();

    expect(areas).toEqual([
      { key: 'audit', name: 'audit' },
      { key: 'finanzen', name: 'Finanzen' },
      { key: 'personal', name: 'Personal ' }
    ]);
    expect(fake.queries.filter((query) => query.indexOf('Id gt') === 0)).toHaveLength(2);
  });

  it('reads the entries of an area through the indexed area field', async () => {
    const { fake, service } = setup();
    fake.seed([
      { brtqmArea: 'Finanzen', brtqmSourceUrl: 'https://old/a', brtqmTargetUrl: 'https://new/f', brtqmSourceKey: 'v1:a' },
      { brtqmArea: 'Audit' },
      { brtqmArea: 'finanzen', brtqmSourceUrl: 'https://old/b' }
    ]);

    const entries = await service.getAreaEntries('Finanzen');

    expect(entries.map((entry) => entry.id)).toEqual([1, 3]);
    expect(entries[0]).toEqual({
      id: 1,
      uniqueId: 'guid-1',
      etag: '"1"',
      area: 'Finanzen',
      sourceUrl: 'https://old/a',
      targetUrl: 'https://new/f',
      sourceKey: 'v1:a'
    });
  });

  it('falls back to id windows when the area query is throttled', async () => {
    const { fake, service } = setup();
    fake.seed(Array.from({ length: 12000 }, (_, index) => ({ brtqmArea: index % 3 === 0 ? 'Finanzen' : 'Audit' })));
    fake.areaQueryThrottleLimit = 1000;

    const entries = await service.getAreaEntries('Finanzen');

    expect(entries).toHaveLength(4000);
    expect(fake.queries.some((query) => /^Id gt 10000 and Id le 15000 and brtqmArea eq 'Finanzen'$/.test(query))).toBe(true);
  });

  it('rethrows other read errors', async () => {
    const { fake, service } = setup();
    fake.listMissing = true;

    await expect(service.getAreaEntries('Finanzen')).rejects.toMatchObject({ status: 404 });
  });

  it('finds an area by its trimmed name', async () => {
    const { fake, service } = setup();
    fake.seed([{ brtqmArea: 'Finanzen' }]);

    expect(await service.findAreaEntry('  finanzen ')).toEqual({ id: 1, area: 'Finanzen' });
    expect(await service.findAreaEntry('Audit')).toBeUndefined();
  });

  it('looks up source keys in batches of at most 100 requests', async () => {
    const { fake, service } = setup();
    fake.seed([{ brtqmArea: 'Audit', brtqmSourceKey: 'v1:k-5' }]);
    const keys = Array.from({ length: 150 }, (_, index) => `v1:k-${index}`);

    const matches = await service.findEntriesBySourceKeys(keys);

    expect(matches['v1:k-5']).toEqual([{ id: 1, area: 'Audit' }]);
    expect(matches['v1:k-6']).toEqual([]);
    expect(fake.batchSizes).toEqual([100, 50]);
  });

  it('checks the permissions required by the planned operations', async () => {
    const { fake, service } = setup();
    fake.permissions = [2];

    expect(await service.canExecute([create('a', 'v1:a')])).toBe(true);
    expect(await service.canExecute([create('a', 'v1:a'), { kind: 'recycle', rowId: 'b', itemId: 1 }])).toBe(false);
  });

  it('executes operations in batches of at most 100 and returns the saved entries', async () => {
    const { fake, service } = setup();
    const operations = Array.from({ length: BATCH_SIZE + 1 }, (_, index) => create(`r${index}`, `v1:${index}`));

    const results = await service.execute(operations);

    expect(results.every((result) => result.succeeded)).toBe(true);
    expect(results[0].entry).toMatchObject({ id: 1, uniqueId: 'guid-1', etag: '"1"', sourceUrl: 'https://old/r0', sourceKey: 'v1:0' });
    expect(fake.items).toHaveLength(BATCH_SIZE + 1);
    expect(fake.batchSizes.filter((size) => size > 0).every((size) => size <= BATCH_SIZE)).toBe(true);
  });

  it('reports a partial failure per operation', async () => {
    const { fake, service } = setup();
    fake.seed([{ brtqmArea: 'Finanzen' }, { brtqmArea: 'Finanzen' }]);
    fake.failWrite = (request) => (request.itemId === 2 ? httpError(500) : undefined);

    const results = await service.execute([
      { kind: 'update', rowId: 'a', itemId: 1, etag: '"1"', fields: { brtqmArea: 'Neu' } },
      { kind: 'update', rowId: 'b', itemId: 2, etag: '"1"', fields: { brtqmArea: 'Neu' } }
    ]);

    expect(results.map((result) => [result.succeeded, result.failure])).toEqual([[true, undefined], [false, 'error']]);
    expect(results[0].entry).toMatchObject({ id: 1, area: 'Neu', etag: '"2"' });
    expect(fake.items[1].brtqmArea).toBe('Finanzen');
  });

  it('maps 412 from If-Match to a conflict', async () => {
    const { fake, service } = setup();
    fake.seed([{ brtqmArea: 'Finanzen' }]);
    fake.modify(1, { brtqmTargetUrl: 'https://other' });

    const [result] = await service.execute([
      { kind: 'update', rowId: 'a', itemId: 1, etag: '"1"', fields: { brtqmArea: 'Neu' } }
    ]);

    expect(result).toMatchObject({ succeeded: false, failure: 'conflict' });
    expect(fake.items[0].brtqmArea).toBe('Finanzen');
  });

  it('maps 403 to forbidden', async () => {
    const { fake, service } = setup();
    fake.failWrite = () => httpError(403);

    const [result] = await service.execute([create('a', 'v1:a')]);

    expect(result).toMatchObject({ succeeded: false, failure: 'forbidden' });
  });

  it('moves removed entries to the recycle bin', async () => {
    const { fake, service } = setup();
    fake.seed([{ brtqmArea: 'Finanzen' }]);

    const [result] = await service.execute([{ kind: 'recycle', rowId: 'a', itemId: 1 }]);

    expect(result.succeeded).toBe(true);
    expect(fake.writes).toEqual([{ kind: 'recycle', itemId: 1 }]);
    expect(fake.items).toHaveLength(0);
  });
});
