import { ExportEntry } from '../types';
import { isExportable } from './RedirectRuleStatus';
import { swissDateTime } from './SwissTime';

export type ExportVariant = 'full' | 'sourceUrls';

export const CSV_DELIMITER: string = ';';
export const CSV_HEADERS: string[] = ['Bereich', 'Quell-URL', 'Ziel-URL', 'Status', 'Geändert am', 'Geändert von', 'UniqueId'];
export const SOURCE_URLS_CSV_HEADERS: string[] = ['Quell-URL'];

const BYTE_ORDER_MARK: string = '﻿';
const LINE_BREAK: string = '\r\n';
const FORMULA_PREFIX: RegExp = /^[=+\-@\t\r]/;
const NEEDS_QUOTES: RegExp = /[";\r\n]/;

function pad(value: number): string {
  return value < 10 ? `0${value}` : String(value);
}

function toCsv(rows: string[][]): string {
  return BYTE_ORDER_MARK + rows.map((row) => row.map(csvField).join(CSV_DELIMITER)).join(LINE_BREAK) + LINE_BREAK;
}

export function formatDateTime(value: string): string {
  const date = new Date(value);

  if (!value || isNaN(date.getTime())) {
    return '';
  }

  return `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${date.getFullYear()} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function csvField(value: string): string {
  const safe = FORMULA_PREFIX.test(value) ? `'${value}` : value;

  return NEEDS_QUOTES.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe;
}

export function sortForExport(entries: ExportEntry[]): ExportEntry[] {
  return entries.slice().sort((left, right) =>
    left.area.trim().localeCompare(right.area.trim(), 'de', { sensitivity: 'base' }) || left.id - right.id);
}

export function buildRedirectRulesCsv(entries: ExportEntry[]): string {
  return toCsv([CSV_HEADERS].concat(sortForExport(entries).map((entry) => [
    entry.area.trim(),
    entry.sourceUrl,
    entry.targetUrl,
    entry.status,
    formatDateTime(entry.modified),
    entry.modifiedBy,
    entry.uniqueId
  ])));
}

export function buildSourceUrlsCsv(entries: ExportEntry[]): string {
  return toCsv([SOURCE_URLS_CSV_HEADERS].concat(sortForExport(entries).map((entry) => [entry.sourceUrl])));
}

export function exportableEntries(entries: ExportEntry[]): ExportEntry[] {
  return entries.filter((entry) => isExportable(entry.status));
}

export function buildExportCsv(entries: ExportEntry[], variant: ExportVariant): string {
  const exportable = exportableEntries(entries);

  return variant === 'sourceUrls' ? buildSourceUrlsCsv(exportable) : buildRedirectRulesCsv(exportable);
}

export function exportFileName(now: Date, variant: ExportVariant = 'full'): string {
  const suffix = variant === 'sourceUrls' ? '_Quell-URLs' : '';
  const stamp = swissDateTime(now);

  return `Redirect-Regeln${suffix}_${stamp.date}_${stamp.time}.csv`;
}
