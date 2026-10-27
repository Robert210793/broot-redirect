import {
  AreaSnapshot,
  PreparedRow,
  RedirectRuleEntry,
  RedirectRuleFields,
  RowIssue,
  SaveOperation,
  SaveOperationResult,
  SourceUrlRow
} from '../types';

export interface AppliedSaveResults {
  entries: RedirectRuleEntry[];
  rows: SourceUrlRow[];
  rowIssues: Record<string, RowIssue>;
}

function changedFields(entry: RedirectRuleEntry, desired: RedirectRuleFields): Partial<RedirectRuleFields> {
  const fields: Partial<RedirectRuleFields> = {};

  if (entry.area !== desired.brtqmArea) {
    fields.brtqmArea = desired.brtqmArea;
  }
  if (entry.sourceUrl !== desired.brtqmSourceUrl) {
    fields.brtqmSourceUrl = desired.brtqmSourceUrl;
  }
  if (entry.targetUrl !== desired.brtqmTargetUrl) {
    fields.brtqmTargetUrl = desired.brtqmTargetUrl;
  }
  if (entry.sourceKey !== desired.brtqmSourceKey) {
    fields.brtqmSourceKey = desired.brtqmSourceKey;
  }

  return fields;
}

export function planAreaSave(
  expected: RedirectRuleEntry[],
  areaName: string,
  targetUrl: string,
  rows: PreparedRow[]
): SaveOperation[] {
  const entriesById: Record<number, RedirectRuleEntry> = {};
  expected.forEach((entry) => { entriesById[entry.id] = entry; });

  const operations: SaveOperation[] = [];

  rows.forEach((row) => {
    const entry = row.itemId !== undefined ? entriesById[row.itemId] : undefined;

    if (row.removed) {
      if (entry) {
        operations.push({ kind: 'recycle', rowId: row.rowId, itemId: entry.id });
      }
      return;
    }

    const desired: RedirectRuleFields = {
      brtqmArea: areaName,
      brtqmSourceUrl: row.sourceUrl,
      brtqmTargetUrl: targetUrl,
      brtqmSourceKey: row.sourceKey
    };

    if (!entry) {
      operations.push({ kind: 'create', rowId: row.rowId, fields: desired });
      return;
    }

    const fields = changedFields(entry, desired);

    if (Object.keys(fields).length > 0) {
      operations.push({ kind: 'update', rowId: row.rowId, itemId: entry.id, etag: entry.etag, fields });
    }
  });

  return operations;
}

export function keysRequiringDuplicateCheck(expected: RedirectRuleEntry[], operations: SaveOperation[]): Record<string, string> {
  const expectedKeys: Record<number, string> = {};
  expected.forEach((entry) => { expectedKeys[entry.id] = entry.sourceKey; });

  const rowIdsByKey: Record<string, string> = {};

  operations.forEach((operation) => {
    if (operation.kind === 'create') {
      rowIdsByKey[operation.fields.brtqmSourceKey] = operation.rowId;
    } else if (operation.kind === 'update' && operation.fields.brtqmSourceKey !== undefined
      && operation.fields.brtqmSourceKey !== expectedKeys[operation.itemId]) {
      rowIdsByKey[operation.fields.brtqmSourceKey] = operation.rowId;
    }
  });

  return rowIdsByKey;
}

export function buildAreaSnapshot(entries: RedirectRuleEntry[], fallbackArea: string, fallbackTargetUrl: string): AreaSnapshot {
  const sorted = entries.slice().sort((left, right) => left.id - right.id);
  const first = sorted[0];

  return {
    area: first ? first.area : fallbackArea,
    targetUrl: first ? first.targetUrl : fallbackTargetUrl,
    entries: sorted
  };
}

export function applySaveResults(
  expected: RedirectRuleEntry[],
  rows: SourceUrlRow[],
  results: SaveOperationResult[]
): AppliedSaveResults {
  const entriesById: Record<number, RedirectRuleEntry> = {};
  expected.forEach((entry) => { entriesById[entry.id] = entry; });

  const itemIdByRowId: Record<string, number> = {};
  const recycledRowIds: Record<string, boolean> = {};
  const rowIssues: Record<string, RowIssue> = {};

  results.forEach((result) => {
    const operation = result.operation;

    if (!result.succeeded) {
      rowIssues[operation.rowId] = { kind: 'notSaved', reason: result.failure || 'error' };
      return;
    }

    if (operation.kind === 'recycle') {
      delete entriesById[operation.itemId];
      recycledRowIds[operation.rowId] = true;
      return;
    }

    if (result.entry) {
      entriesById[result.entry.id] = result.entry;
      itemIdByRowId[operation.rowId] = result.entry.id;
    }
  });

  return {
    entries: Object.keys(entriesById).map((id) => entriesById[Number(id)]),
    rows: rows
      .filter((row) => !recycledRowIds[row.rowId])
      .map((row) => (itemIdByRowId[row.rowId] !== undefined ? { ...row, itemId: itemIdByRowId[row.rowId] } : row)),
    rowIssues
  };
}
