import { adoptEtagAfterOwnComment, detectConcurrentChanges, hasConcurrentChanges } from '../../../src/webparts/brtqmRedirectRules/logic/AreaConcurrencyGuard';
import { entry } from '../testData';

describe('AreaConcurrencyGuard', () => {
  it('reports no change for an identical state', () => {
    const changes = detectConcurrentChanges([entry(1), entry(2)], [entry(2), entry(1)]);

    expect(hasConcurrentChanges(changes)).toBe(false);
  });

  it('detects added, removed and modified entries', () => {
    const changes = detectConcurrentChanges(
      [entry(1), entry(2), entry(3)],
      [entry(1), entry(2, { etag: '"2"' }), entry(4)]
    );

    expect(changes).toEqual({ added: [4], removed: [3], modified: [2] });
    expect(hasConcurrentChanges(changes)).toBe(true);
  });

  it('adopts the new etag after an own comment when the fields are unchanged', () => {
    const adopted = adoptEtagAfterOwnComment([entry(1), entry(2)], entry(2, { etag: '"3"' }));

    expect(adopted.map((item) => item.etag)).toEqual(['"1"', '"3"']);
  });

  it.each([
    ['area', { area: 'Audit' }],
    ['source URL', { sourceUrl: 'https://old/other' }],
    ['target URL', { targetUrl: 'https://new/other' }],
    ['source key', { sourceKey: 'v1:other' }],
    ['status', { status: 'aktiv' }]
  ])('keeps the expected etag after a comment when the %s changed meanwhile', (_, change) => {
    const adopted = adoptEtagAfterOwnComment([entry(1)], entry(1, { etag: '"3"', ...change }));

    expect(adopted[0].etag).toBe('"1"');
  });
});
