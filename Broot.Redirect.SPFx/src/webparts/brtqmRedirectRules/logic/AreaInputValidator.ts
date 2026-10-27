import { AreaInputValidation, PreparedRow, SourceUrlRow } from '../types';
import { computeSourceKey } from './SourceKeyNormalizer';
import { isValidSourceUrl, validateTargetUrl } from './UrlValidator';

export interface AreaInput {
  isNewArea: boolean;
  areaName: string;
  targetUrl: string;
  rows: SourceUrlRow[];
}

export interface PreparedAreaInput {
  validation: AreaInputValidation;
  rows: PreparedRow[];
}

export function isInputValid(validation: AreaInputValidation): boolean {
  return !validation.areaNameMissing
    && !validation.targetUrlIssue
    && !validation.noSourceUrls
    && Object.keys(validation.rowIssues).length === 0;
}

export async function prepareAreaInput(input: AreaInput): Promise<PreparedAreaInput> {
  const activeRows = input.rows.filter((row) => !row.removed);
  const requiresAreaData = input.isNewArea || activeRows.length > 0;
  const validation: AreaInputValidation = {
    areaNameMissing: requiresAreaData && input.areaName.trim().length === 0,
    targetUrlIssue: requiresAreaData ? validateTargetUrl(input.targetUrl) : undefined,
    noSourceUrls: input.isNewArea && activeRows.length === 0,
    rowIssues: {}
  };

  const rows: PreparedRow[] = await Promise.all(input.rows.map(async (row) => {
    const sourceUrl = row.sourceUrl.trim();

    if (row.removed) {
      return { ...row, sourceUrl, sourceKey: '' };
    }

    if (sourceUrl.length === 0) {
      validation.rowIssues[row.rowId] = { kind: 'required' };
      return { ...row, sourceUrl, sourceKey: '' };
    }

    if (!isValidSourceUrl(sourceUrl)) {
      validation.rowIssues[row.rowId] = { kind: 'invalidSourceUrl' };
      return { ...row, sourceUrl, sourceKey: '' };
    }

    return { ...row, sourceUrl, sourceKey: await computeSourceKey(sourceUrl) };
  }));

  const rowIdsByKey: Record<string, string[]> = {};

  rows
    .filter((row) => !row.removed && row.sourceKey.length > 0)
    .forEach((row) => {
      rowIdsByKey[row.sourceKey] = (rowIdsByKey[row.sourceKey] || []).concat(row.rowId);
    });

  Object.keys(rowIdsByKey)
    .filter((key) => rowIdsByKey[key].length > 1)
    .forEach((key) => rowIdsByKey[key].forEach((rowId) => {
      validation.rowIssues[rowId] = { kind: 'duplicateInSave' };
    }));

  return { validation, rows };
}
