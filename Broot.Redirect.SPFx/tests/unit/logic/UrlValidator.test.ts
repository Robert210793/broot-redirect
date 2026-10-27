import {
  isValidSourceUrl,
  resolveTargetUrl,
  validateTargetUrl
} from '../../../src/webparts/brtqmRedirectRules/logic/UrlValidator';

describe('UrlValidator', () => {
  describe('isValidSourceUrl', () => {
    it.each([
      'https://intranet.example.ch/ims/a.aspx',
      'http://oldserver/ims/A.aspx?RootFolder=%2Fims&View=%7B1%7D',
      'HTTP://OLDSERVER:8080/x',
      'https://host/docs/a b.docx'
    ])('accepts %s', (value: string) => {
      expect(isValidSourceUrl(value)).toBe(true);
    });

    it.each([
      '',
      '/ims/a.aspx',
      'ims/a.aspx',
      'ftp://host/a',
      'https://',
      'https:///path',
      'javascript:alert(1)',
      'https://ho st/a'
    ])('rejects %s', (value: string) => {
      expect(isValidSourceUrl(value)).toBe(false);
    });
  });

  describe('validateTargetUrl', () => {
    it.each([
      'https://tenant.sharepoint.com/sites/qm/doc.pdf',
      '/sites/qm/SitePages/Bereich.aspx?x=1#teil',
      'http://host'
    ])('accepts %s', (value: string) => {
      expect(validateTargetUrl(value)).toBeUndefined();
    });

    it('requires a value', () => {
      expect(validateTargetUrl('  ')).toBe('required');
    });

    it.each([
      'sites/qm',
      '//evil.example/x',
      '/\\evil.example/x',
      'mailto:a@b.ch',
      'ftp://host/a'
    ])('rejects %s', (value: string) => {
      expect(validateTargetUrl(value)).toBe('invalidTargetUrl');
    });
  });

  describe('resolveTargetUrl', () => {
    it('prefixes a server-relative path with the site origin and keeps query and fragment', () => {
      expect(resolveTargetUrl('/sites/qm/a.aspx?x=1&y=%20#teil', 'https://tenant.sharepoint.com'))
        .toBe('https://tenant.sharepoint.com/sites/qm/a.aspx?x=1&y=%20#teil');
    });

    it('keeps an absolute URL unchanged', () => {
      expect(resolveTargetUrl(' https://other.example/a?b=1 ', 'https://tenant.sharepoint.com'))
        .toBe('https://other.example/a?b=1');
    });
  });
});
