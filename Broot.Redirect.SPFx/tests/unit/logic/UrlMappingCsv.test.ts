import { swissDateTime } from '../../../src/webparts/brtqmRedirectRules/logic/SwissTime';
import {
  buildUrlMappingCsv,
  isCurrentUrlMappingFile,
  isUrlMappingFileName,
  uniqueFileName,
  UrlMappingSource,
  urlMappingArchiveFileName
} from '../../../src/webparts/brtqmRedirectRules/logic/UrlMappingCsv';

function source(id: number, sourceUrl: string, status: string = 'aktiv'): UrlMappingSource {
  return { id, sourceUrl, status };
}

describe('UrlMappingCsv', () => {
  it('writes the header and one source URL per line with CRLF, without BOM and without a final line break', () => {
    const csv = buildUrlMappingCsv([source(1, 'http://old/a/'), source(2, 'http://old/b?x=1&y=2')]);
    const bytes = Buffer.from(csv.content, 'utf8');

    expect(csv.content).toBe('source-url\r\nhttp://old/a/\r\nhttp://old/b?x=1&y=2');
    expect(bytes[0]).toBe(0x73);
    expect(csv.content.endsWith('\r\n')).toBe(false);
    expect(csv.sourceUrlCount).toBe(2);
    expect(csv.skippedCount).toBe(0);
  });

  it('writes only the header for an empty list', () => {
    expect(buildUrlMappingCsv([]).content).toBe('source-url');
  });

  it('matches the customer template byte for byte', () => {
    const template = 'source-url\r\nhttp://services.ad.lihogr.ch/ims/Einkauf_Logistik_Apotheke/\r\nhttp://services.ad.lihogr.ch/ims/KVP_Innovation/11%20Integriertes%20Patienten%20R%C3%BCckmeldungsmanagement%20(IPRM)/';

    expect(buildUrlMappingCsv([
      source(1, 'http://services.ad.lihogr.ch/ims/Einkauf_Logistik_Apotheke/'),
      source(2, 'http://services.ad.lihogr.ch/ims/KVP_Innovation/11%20Integriertes%20Patienten%20R%C3%BCckmeldungsmanagement%20(IPRM)/')
    ]).content).toBe(template);
  });

  it('leaves out entries that count as prepared, including empty, unknown and padded statuses', () => {
    const csv = buildUrlMappingCsv([
      source(1, 'http://old/vorbereitet', 'vorbereitet'),
      source(2, 'http://old/iis', 'IIS-Umleitung offen'),
      source(3, 'http://old/testing', 'Testing'),
      source(4, 'http://old/fehlerhaft', 'Fehlerhaft'),
      source(5, 'http://old/aktiv', 'aktiv'),
      source(6, 'http://old/leer', ''),
      source(7, 'http://old/unbekannt', 'inaktiv'),
      source(8, 'http://old/leerraum', ' aktiv ')
    ]);

    expect(csv.content.split('\r\n').slice(1)).toEqual(['http://old/iis', 'http://old/testing', 'http://old/fehlerhaft', 'http://old/aktiv']);
  });

  it('orders the lines by list item id', () => {
    expect(buildUrlMappingCsv([source(9, 'http://old/9'), source(2, 'http://old/2'), source(5, 'http://old/5')]).content)
      .toBe('source-url\r\nhttp://old/2\r\nhttp://old/5\r\nhttp://old/9');
  });

  it('keeps values unchanged except for surrounding whitespace, without quoting, formula protection or deduplication', () => {
    const csv = buildUrlMappingCsv([
      source(1, '  http://old/a;b "c" '),
      source(2, '=http://old/formel'),
      source(3, 'http://old/a;b "c"')
    ]);

    expect(csv.content).toBe('source-url\r\nhttp://old/a;b "c"\r\n=http://old/formel\r\nhttp://old/a;b "c"');
  });

  it('skips and counts empty and multi-line source URLs', () => {
    const csv = buildUrlMappingCsv([source(1, ''), source(2, '   '), source(3, 'http://old/a\r\nhttp://old/b'), source(4, 'http://old/c\n'), source(5, 'http://old/ok')]);

    expect(csv.content).toBe('source-url\r\nhttp://old/c\r\nhttp://old/ok');
    expect(csv.skippedCount).toBe(3);
    expect(csv.sourceUrlCount).toBe(2);
  });

  it.each([
    ['url-mapping.csv', true],
    ['URL-Mapping.CSV', true],
    ['url-mapping (1).csv', true],
    ['url-mapping_2026-01-01.csv', true],
    ['Beispieldatei url-mapping.csv', false],
    ['url-mapping.csv.bak', false],
    ['Redirect-Regeln_2026-01-01_0600.csv', false]
  ])('recognises %j as url-mapping file: %s', (name, expected) => {
    expect(isUrlMappingFileName(name)).toBe(expected);
  });

  it('recognises the current file regardless of case', () => {
    expect(isCurrentUrlMappingFile('URL-Mapping.csv')).toBe(true);
    expect(isCurrentUrlMappingFile('url-mapping (1).csv')).toBe(false);
  });

  it.each([
    ['2026-01-15T05:00:00Z', 'url-mapping_2026-01-15.csv'],
    ['2026-07-15T04:00:00Z', 'url-mapping_2026-07-15.csv'],
    ['2026-03-29T00:30:00Z', 'url-mapping_2026-03-29.csv'],
    ['2026-03-29T01:30:00Z', 'url-mapping_2026-03-29.csv'],
    ['2026-10-25T00:30:00Z', 'url-mapping_2026-10-25.csv'],
    ['2026-10-25T01:30:00Z', 'url-mapping_2026-10-25.csv'],
    ['2026-12-31T23:30:00Z', 'url-mapping_2027-01-01.csv']
  ])('names the archive file of %s in Swiss time as %s', (modified, expected) => {
    expect(urlMappingArchiveFileName(modified)).toBe(expected);
  });

  it('formats Swiss date and time independent of the host time zone', () => {
    expect(swissDateTime(new Date(Date.UTC(2026, 10, 18, 23, 5)))).toEqual({ date: '2026-11-19', time: '0005' });
  });

  it('adds a numbered suffix before the extension from the second attempt on', () => {
    expect(uniqueFileName('url-mapping_2026-01-15.csv', 1)).toBe('url-mapping_2026-01-15.csv');
    expect(uniqueFileName('url-mapping_2026-01-15.csv', 2)).toBe('url-mapping_2026-01-15_2.csv');
    expect(uniqueFileName('Redirect-Regeln_2026-01-15_0600.csv', 99)).toBe('Redirect-Regeln_2026-01-15_0600_99.csv');
  });
});
