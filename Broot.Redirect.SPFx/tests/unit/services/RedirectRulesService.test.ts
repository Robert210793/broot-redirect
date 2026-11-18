import {
  BATCH_SIZE,
  COMMENT_PAGE_SIZE,
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

  it('reads all entries of all areas page by page beyond 5000 entries for the export', async () => {
    const { fake, service } = setup();
    fake.seed(Array.from({ length: PAGE_SIZE + 2 }, (_, index) => ({
      brtqmArea: index % 2 === 0 ? 'Finanzen' : 'Audit',
      brtqmSourceUrl: `https://old/${index}`,
      brtqmTargetUrl: 'https://new/target'
    })));
    fake.seed([{ brtqmArea: 'IT', brtqmSourceUrl: 'https://old/it/', brtqmTargetUrl: 'https://new/it', brtqmStatus: 'Testing', Modified: '2026-10-28T12:30:00Z', Editor: { Title: 'Anna Muster' } }]);

    const entries = await service.getAllEntries();

    expect(entries).toHaveLength(PAGE_SIZE + 3);
    expect(entries[PAGE_SIZE + 2]).toEqual({
      id: PAGE_SIZE + 3,
      uniqueId: `guid-${PAGE_SIZE + 3}`,
      area: 'IT',
      sourceUrl: 'https://old/it/',
      targetUrl: 'https://new/it',
      status: 'Testing',
      modified: '2026-10-28T12:30:00Z',
      modifiedBy: 'Anna Muster'
    });
    expect(fake.queries.filter((query) => query.indexOf('Id gt') === 0)).toEqual(['Id gt 0', `Id gt ${PAGE_SIZE}`]);
    expect(fake.expands).toContain('Editor');
  });

  it('exports an empty list without entries', async () => {
    const { service } = setup();

    expect(await service.getAllEntries()).toEqual([]);
  });

  it('reads the entries of an area through the indexed area field', async () => {
    const { fake, service } = setup();
    fake.seed([
      { brtqmArea: 'Finanzen', brtqmSourceUrl: 'https://old/a', brtqmTargetUrl: 'https://new/f', brtqmSourceKey: 'v1:a', brtqmStatus: 'aktiv' },
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
      sourceKey: 'v1:a',
      status: 'aktiv'
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

  describe('status', () => {
    it('reads an empty status of an existing entry as empty', async () => {
      const { fake, service } = setup();
      fake.seed([{ brtqmArea: 'Finanzen' }]);

      const [entry] = await service.getAreaEntries('Finanzen');

      expect(entry.status).toBe('');
    });

    it('updates only the status of an entry and keeps its identifier', async () => {
      const { fake, service } = setup();
      fake.seed([{ brtqmArea: 'Finanzen', brtqmSourceUrl: 'https://old/a', brtqmStatus: 'Testing' }]);

      const [result] = await service.execute([
        { kind: 'update', rowId: 'a', itemId: 1, etag: '"1"', fields: { brtqmStatus: 'aktiv' } }
      ]);

      expect(fake.writes).toEqual([{ kind: 'update', itemId: 1, fields: { brtqmStatus: 'aktiv' } }]);
      expect(result.entry).toMatchObject({ id: 1, uniqueId: 'guid-1', sourceUrl: 'https://old/a', status: 'aktiv' });
    });

    it('lets the list default apply to a created entry without status', async () => {
      const { fake, service } = setup();

      const [result] = await service.execute([{
        kind: 'create',
        rowId: 'a',
        fields: { brtqmArea: 'Finanzen', brtqmSourceUrl: 'https://old/a', brtqmTargetUrl: 'https://new/f', brtqmSourceKey: 'v1:a' }
      }]);

      expect(result.entry && result.entry.status).toBe('vorbereitet');
      expect(fake.items[0].brtqmStatus).toBe('vorbereitet');
    });

    it('reads a single entry and reports a missing one as undefined', async () => {
      const { fake, service } = setup();
      fake.seed([{ brtqmArea: 'Finanzen', brtqmStatus: 'aktiv' }]);

      expect(await service.getEntry(1)).toMatchObject({ id: 1, status: 'aktiv', etag: '"1"' });
      expect(await service.getEntry(99)).toBeUndefined();
    });
  });

  describe('comments', () => {
    it('reads the comment counts of the given entries in batches and counts none as zero', async () => {
      const { fake, service } = setup();
      fake.seed(Array.from({ length: BATCH_SIZE + 1 }, () => ({ brtqmArea: 'Finanzen' })));
      fake.comments[1] = [
        { id: '1', text: 'a', createdDate: '', author: { name: 'A' } },
        { id: '2', text: 'b', createdDate: '', author: { name: 'B' } }
      ];

      const counts = await service.getCommentCounts(Array.from({ length: BATCH_SIZE + 1 }, (_, index) => index + 1));

      expect(counts[1]).toBe(2);
      expect(counts[2]).toBe(0);
      expect(Object.keys(counts)).toHaveLength(BATCH_SIZE + 1);
      expect(fake.batchSizes).toEqual([BATCH_SIZE, 1]);
    });

    it('leaves comment counts unknown when they cannot be read', async () => {
      const { fake, service } = setup();
      fake.seed([{ brtqmArea: 'Finanzen' }]);
      fake.commentCountFailure = true;

      expect(await service.getCommentCounts([1])).toEqual({});
    });

    it('loads all comments page by page with author, time and resolved mentions', async () => {
      const { fake, service } = setup();
      fake.seed([{ brtqmArea: 'Finanzen' }]);
      fake.comments[1] = Array.from({ length: COMMENT_PAGE_SIZE + 1 }, (_, index) => ({
        id: String(index + 1),
        text: index === 0 ? 'Bitte prüfen @mention{0}' : `Kommentar ${index + 1}`,
        createdDate: '2026-11-18T09:15:00Z',
        author: { name: 'Anna Muster' },
        mentions: index === 0 ? [{ name: 'Ben Beispiel' }] : null
      }));

      const result = await service.getComments(1);

      expect(result.kind).toBe('loaded');
      const comments = result.kind === 'loaded' ? result.comments : [];
      expect(comments).toHaveLength(COMMENT_PAGE_SIZE + 1);
      expect(comments[0]).toEqual({ id: '1', text: 'Bitte prüfen @Ben Beispiel', authorName: 'Anna Muster', created: '2026-11-18T09:15:00Z' });
      expect(fake.commentQueries.map((query) => query.skip)).toEqual([0, COMMENT_PAGE_SIZE]);
    });

    it('adds a comment as plain text', async () => {
      const { fake, service } = setup();
      fake.seed([{ brtqmArea: 'Finanzen' }]);

      expect(await service.addComment(1, 'IIS-Regel eingetragen')).toBe('added');
      expect(fake.comments[1].map((comment) => comment.text)).toEqual(['IIS-Regel eingetragen']);
    });

    it('reports disabled comments from the list setting without calling the comment API', async () => {
      const { fake, service } = setup();
      fake.seed([{ brtqmArea: 'Finanzen' }]);
      fake.commentingDisabled = true;

      expect(await service.getComments(1)).toEqual({ kind: 'disabled' });
      expect(await service.addComment(1, 'x')).toBe('disabled');
      expect(fake.commentQueries).toHaveLength(0);
      expect(fake.comments[1]).toBeUndefined();
    });

    it('recognises comments disabled for the tenant from the error of the comment API', async () => {
      const { fake, service } = setup();
      fake.seed([{ brtqmArea: 'Finanzen' }]);
      fake.commentingDisabled = undefined;
      fake.commentFailure = httpError(400, 'Commenting is disabled for this list.');

      expect(await service.getComments(1)).toEqual({ kind: 'disabled' });
      expect(await service.addComment(1, 'x')).toBe('disabled');
    });

    it('continues when the list setting cannot be read', async () => {
      const { fake, service } = setup();
      fake.seed([{ brtqmArea: 'Finanzen' }]);
      fake.listSettingsFailure = httpError(500);

      expect(await service.isCommentingDisabled()).toBeUndefined();
      expect(await service.getComments(1)).toEqual({ kind: 'loaded', comments: [] });
    });

    it.each([
      [403, 'forbidden'],
      [401, 'forbidden'],
      [500, 'failed'],
      [404, 'failed']
    ] as const)('maps status %d of the comment API to %s', async (status, expected) => {
      const { fake, service } = setup();
      fake.seed([{ brtqmArea: 'Finanzen' }]);
      fake.commentFailure = httpError(status);

      expect(await service.getComments(1)).toEqual({ kind: expected });
      expect(await service.addComment(1, 'x')).toBe(expected);
    });
  });
});
