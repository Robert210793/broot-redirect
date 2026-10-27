import { createHash } from 'crypto';
import { canonicalizeSourceUrl, computeSourceKey } from '../../../src/webparts/brtqmRedirectRules/logic/SourceKeyNormalizer';

const host = 'https://intranet.example.ch';

describe('SourceKeyNormalizer', () => {
  describe('equivalent spellings from FindEquivalentMatcherTests', () => {
    it.each([
      ['/docs/a.docx', '/docs/a.docx'],
      ['/Docs/A.docx', '/docs/a.docx'],
      ['/docs/a b.docx', '/docs/a%20b.docx'],
      ['/docs/a%20b.docx', '/DOCS/A B.docx'],
      ['/docs/a.docx/', '/docs/a.docx'],
      ['/docs/a.docx?x=1&y=2', '/docs/a.docx?y=2&x=1'],
      ['/docs/a.docx?RootFolder=%2Fims%2FA', '/docs/a.docx?rootfolder=/ims/a'],
      ['/docs/a.docx?', '/docs/a.docx']
    ])('%s equals %s', async (existing: string, input: string) => {
      expect(await computeSourceKey(host + existing)).toBe(await computeSourceKey(host + input));
    });
  });

  describe('different matchers from FindEquivalentMatcherTests', () => {
    it.each([
      ['/docs/a.docx?x=1', '/docs/a.docx?x=2'],
      ['/docs/a.docx?x=1', '/docs/a.docx'],
      ['/docs/a.docx', '/docs/b.docx'],
      ['/docs', '/docs/a.docx']
    ])('%s differs from %s', async (existing: string, input: string) => {
      expect(await computeSourceKey(host + existing)).not.toBe(await computeSourceKey(host + input));
    });
  });

  it('ignores scheme and host', async () => {
    expect(await computeSourceKey('http://old-host/ims/A.aspx?ID=1'))
      .toBe(await computeSourceKey('https://intranet.example.ch:8443/ims/a.aspx?id=1'));
  });

  it('ignores the fragment', async () => {
    expect(await computeSourceKey(`${host}/ims/a#section`)).toBe(await computeSourceKey(`${host}/ims/a`));
  });

  it('treats repeated query pairs as a set', async () => {
    expect(await computeSourceKey(`${host}/a?x=1&x=1`)).toBe(await computeSourceKey(`${host}/a?x=1`));
  });

  it('treats a key without value like an empty value', async () => {
    expect(await computeSourceKey(`${host}/a?flag`)).toBe(await computeSourceKey(`${host}/a?flag=`));
  });

  it('does not split on an encoded slash inside a segment', () => {
    expect(canonicalizeSourceUrl(`${host}/a%2Fb`)).not.toBe(canonicalizeSourceUrl(`${host}/a/b`));
  });

  it('keeps invalid percent sequences', () => {
    expect(canonicalizeSourceUrl(`${host}/a%zz%20b`)).toBe(JSON.stringify({ path: ['a%zz b'], query: [] }));
  });

  it('does not confuse separators inside decoded values', () => {
    expect(canonicalizeSourceUrl(`${host}/a?x=1%26y%3D2`)).not.toBe(canonicalizeSourceUrl(`${host}/a?x=1&y=2`));
  });

  it('produces a versioned SHA-256 key of 67 characters', async () => {
    const key = await computeSourceKey(`${host}/ims/a.aspx?RootFolder=%2Fims%2FA&FolderCTID=0x0120&View=%7BABC%7D`);

    expect(key).toMatch(/^v1:[0-9a-f]{64}$/);
    expect(key).toHaveLength(67);
  });

  it('is deterministic for a known canonical form', async () => {
    expect(await computeSourceKey(`${host}/a`))
      .toBe('v1:' + createHash('sha256').update('{"path":["a"],"query":[]}').digest('hex'));
  });
});
