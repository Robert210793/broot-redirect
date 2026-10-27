import { detectConcurrentChanges, hasConcurrentChanges } from '../logic/AreaConcurrencyGuard';
import { isInputValid, prepareAreaInput } from '../logic/AreaInputValidator';
import { areaCompareValue, isSameArea } from '../logic/AreaName';
import { applySaveResults, buildAreaSnapshot, keysRequiringDuplicateCheck, planAreaSave } from '../logic/AreaSavePlanner';
import { resolveTargetUrl } from '../logic/UrlValidator';
import { AreaSnapshot, RedirectRuleEntry, RowIssue, SaveResult, SourceUrlRow } from '../types';
import { IRedirectRulesService } from './RedirectRulesService';

export interface AreaSaveRequest {
  expected?: AreaSnapshot;
  areaName: string;
  targetUrl: string;
  rows: SourceUrlRow[];
}

export class AreaSaveCoordinator {
  private readonly service: IRedirectRulesService;
  private readonly siteOrigin: string;

  constructor(service: IRedirectRulesService, siteOrigin: string) {
    this.service = service;
    this.siteOrigin = siteOrigin;
  }

  public async save(request: AreaSaveRequest): Promise<SaveResult> {
    const expected = request.expected;
    const expectedEntries = expected ? expected.entries : [];
    const prepared = await prepareAreaInput({
      isNewArea: !expected,
      areaName: request.areaName,
      targetUrl: request.targetUrl,
      rows: request.rows
    });

    if (!isInputValid(prepared.validation)) {
      return { kind: 'invalid', validation: prepared.validation };
    }

    const areaName = request.areaName.trim();
    const targetUrl = resolveTargetUrl(request.targetUrl, this.siteOrigin);
    const operations = planAreaSave(expectedEntries, areaName, targetUrl, prepared.rows);

    if (operations.length === 0) {
      return { kind: 'nothingToSave' };
    }

    if (!(await this.service.canExecute(operations))) {
      return { kind: 'forbidden' };
    }

    if (expected) {
      const current = await this.readCurrentEntries(expected);
      if (hasConcurrentChanges(detectConcurrentChanges(expectedEntries, current))) {
        return { kind: 'concurrentChange' };
      }
    }

    const ownIds: Record<number, boolean> = {};
    expectedEntries.forEach((entry) => { ownIds[entry.id] = true; });

    if (areaName.length > 0 && (!expected || !isSameArea(areaName, expected.area))) {
      const existing = await this.service.findAreaEntry(areaName);
      if (existing && !ownIds[existing.id]) {
        return { kind: 'areaNameTaken', existingArea: existing.area.trim() };
      }
    }

    const rowIdsByKey = keysRequiringDuplicateCheck(expectedEntries, operations);
    const keys = Object.keys(rowIdsByKey);

    if (keys.length > 0) {
      const matches = await this.service.findEntriesBySourceKeys(keys);
      const rowIssues: Record<string, RowIssue> = {};

      keys.forEach((key) => {
        const foreign = (matches[key] || []).filter((match) => !ownIds[match.id])[0];
        if (foreign) {
          rowIssues[rowIdsByKey[key]] = { kind: 'duplicateInList', area: foreign.area.trim() };
        }
      });

      if (Object.keys(rowIssues).length > 0) {
        return { kind: 'duplicates', rowIssues };
      }
    }

    const results = await this.service.execute(operations);
    const applied = applySaveResults(
      expectedEntries,
      prepared.rows.map((row) => ({ rowId: row.rowId, itemId: row.itemId, sourceUrl: row.sourceUrl, removed: row.removed })),
      results
    );

    return {
      kind: 'completed',
      snapshot: buildAreaSnapshot(applied.entries, areaName, targetUrl),
      rows: applied.rows,
      rowIssues: applied.rowIssues
    };
  }

  private async readCurrentEntries(expected: AreaSnapshot): Promise<RedirectRuleEntry[]> {
    const namesByKey: Record<string, string> = {};

    [expected.area].concat(expected.entries.map((entry) => entry.area)).forEach((area) => {
      const key = areaCompareValue(area);
      if (key.length > 0 && !namesByKey[key]) {
        namesByKey[key] = area;
      }
    });

    const entriesById: Record<number, RedirectRuleEntry> = {};
    const pages = await Promise.all(Object.keys(namesByKey).map((key) => this.service.getAreaEntries(namesByKey[key])));

    pages.forEach((page) => page.forEach((entry) => { entriesById[entry.id] = entry; }));

    return Object.keys(entriesById).map((id) => entriesById[Number(id)]);
  }
}
