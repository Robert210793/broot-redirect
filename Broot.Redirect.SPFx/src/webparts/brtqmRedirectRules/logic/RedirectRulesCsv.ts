import { ExportEntry } from '../types';

export const CSV_DELIMITER: string = ';';
export const CSV_HEADERS: string[] = ['Bereich', 'Quell-URL', 'Ziel-URL', 'Geändert am', 'Geändert von', 'UniqueId'];

const BYTE_ORDER_MARK: string = '﻿';
const LINE_BREAK: string = '\r\n';
const FORMULA_PREFIX: RegExp = /^[=+\-@\t\r]/;
const NEEDS_QUOTES: RegExp = /[";\r\n]/;

function pad(value: number): string {
  return value < 10 ? `0${value}` : String(value);
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
  const rows = [CSV_HEADERS].concat(sortForExport(entries).map((entry) => [
    entry.area.trim(),
    entry.sourceUrl,
    entry.targetUrl,
    formatDateTime(entry.modified),
    entry.modifiedBy,
    entry.uniqueId
  ]));

  return BYTE_ORDER_MARK + rows.map((row) => row.map(csvField).join(CSV_DELIMITER)).join(LINE_BREAK) + LINE_BREAK;
}

export function exportFileName(now: Date): string {
  return `Redirect-Regeln_${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}.csv`;
}
