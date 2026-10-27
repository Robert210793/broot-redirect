import { detectConcurrentChanges, hasConcurrentChanges } from '../../../src/webparts/brtqmRedirectRules/logic/AreaConcurrencyGuard';
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
});
