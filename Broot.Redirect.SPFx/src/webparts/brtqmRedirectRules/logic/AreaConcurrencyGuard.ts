import { RedirectRuleEntry } from '../types';

export interface ConcurrentChanges {
  added: number[];
  removed: number[];
  modified: number[];
}

export function detectConcurrentChanges(expected: RedirectRuleEntry[], current: RedirectRuleEntry[]): ConcurrentChanges {
  const expectedById: Record<number, RedirectRuleEntry> = {};
  const currentById: Record<number, RedirectRuleEntry> = {};

  expected.forEach((entry) => { expectedById[entry.id] = entry; });
  current.forEach((entry) => { currentById[entry.id] = entry; });

  return {
    added: current.filter((entry) => !expectedById[entry.id]).map((entry) => entry.id),
    removed: expected.filter((entry) => !currentById[entry.id]).map((entry) => entry.id),
    modified: current
      .filter((entry) => expectedById[entry.id] && expectedById[entry.id].etag !== entry.etag)
      .map((entry) => entry.id)
  };
}

export function hasConcurrentChanges(changes: ConcurrentChanges): boolean {
  return changes.added.length > 0 || changes.removed.length > 0 || changes.modified.length > 0;
}
