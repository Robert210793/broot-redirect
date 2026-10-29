import {
  buildRedirectRulesCsv,
  csvField,
  exportFileName,
  formatDateTime,
  sortForExport
} from '../../../src/webparts/brtqmRedirectRules/logic/RedirectRulesCsv';
import { ExportEntry } from '../../../src/webparts/brtqmRedirectRules/types';

function exportEntry(id: number, overrides: Partial<ExportEntry> = {}): ExportEntry {
  return {
    id,
    uniqueId: `00000000-0000-0000-0000-${String(id).padStart(12, '0')}`,
    area: 'Finanzen',
    sourceUrl: `http://old.example/ims/${id}/`,
    targetUrl: 'https://tenant.sharepoint.com/sites/qm/SitePages/Prozess.aspx?ProzessId=1#Finanzen',
    modified: '2026-10-29T07:05:00Z',
    modifiedBy: 'Anna Muster',
    ...overrides
  };
}

function localDateTime(iso: string): string {
  const date = new Date(iso);
  const two = (value: number): string => String(value).padStart(2, '0');
  return `${two(date.getDate())}.${two(date.getMonth() + 1)}.${date.getFullYear()} ${two(date.getHours())}:${two(date.getMinutes())}`;
}

describe('RedirectRulesCsv', () => {
  it('starts with a byte order mark and the German header separated by semicolons', () => {
    const csv = buildRedirectRulesCsv([]);

    expect(csv.charAt(0)).toBe('﻿');
    expect(csv.slice(1)).toBe('Bereich;Quell-URL;Ziel-URL;Geändert am;Geändert von;UniqueId\r\n');
  });

  it('writes one row per entry with area, source, target, modified, editor and unique id', () => {
    const csv = buildRedirectRulesCsv([exportEntry(3)]);
    const lines = csv.slice(1).split('\r\n');

    expect(lines[1]).toBe([
      'Finanzen',
      'http://old.example/ims/3/',
      'https://tenant.sharepoint.com/sites/qm/SitePages/Prozess.aspx?ProzessId=1#Finanzen',
      localDateTime('2026-10-29T07:05:00Z'),
      'Anna Muster',
      '00000000-0000-0000-0000-000000000003'
    ].join(';'));
    expect(lines).toHaveLength(3);
    expect(lines[2]).toBe('');
  });

  it('keeps umlauts and query strings unchanged', () => {
    const csv = buildRedirectRulesCsv([exportEntry(1, {
      area: 'Prüfung & Qualität',
      sourceUrl: 'http://old/ims/Forms/AllItems.aspx?RootFolder=%2Fims%2FPr%C3%BCfung&View=%7B1%7D'
    })]);

    expect(csv).toContain('Prüfung & Qualität;http://old/ims/Forms/AllItems.aspx?RootFolder=%2Fims%2FPr%C3%BCfung&View=%7B1%7D;');
  });

  it('exports all areas sorted by area and then by item id', () => {
    const sorted = sortForExport([
      exportEntry(5, { area: 'IT' }),
      exportEntry(2, { area: 'Finanzen' }),
      exportEntry(9, { area: ' audit' }),
      exportEntry(1, { area: 'finanzen' }),
      exportEntry(4, { area: 'Ärzte' })
    ]);

    expect(sorted.map((entry) => entry.id)).toEqual([4, 9, 1, 2, 5]);
  });

  it('trims the area name in the export', () => {
    expect(buildRedirectRulesCsv([exportEntry(1, { area: '  Finanzen ' })])).toContain('\r\nFinanzen;');
  });

  it.each([
    ['a;b', '"a;b"'],
    ['Sagt "Hallo"', '"Sagt ""Hallo"""'],
    ['erste\nzweite', '"erste\nzweite"'],
    ['https://x/?a=1&b=2', 'https://x/?a=1&b=2']
  ])('quotes %j as %j', (value, expected) => {
    expect(csvField(value)).toBe(expected);
  });

  it.each(['=SUM(A1)', '+41 44', '-Bereich', '@cmd'])('neutralises the formula prefix of %j', (value) => {
    expect(csvField(value)).toBe(`'${value}`);
  });

  it.each(['', 'kein Datum'])('leaves an empty or invalid modified date %j empty', (value) => {
    expect(formatDateTime(value)).toBe('');
  });

  it('names the file with the local date', () => {
    expect(exportFileName(new Date(2026, 0, 5, 23, 59))).toBe('Redirect-Regeln_2026-01-05.csv');
  });
});
