import { AreaEntryReference, AreaOption } from '../types';

export function areaCompareValue(name: string): string {
  return name.trim().toLowerCase();
}

export function isSameArea(left: string, right: string): boolean {
  return areaCompareValue(left) === areaCompareValue(right);
}

export function groupAreas(entries: AreaEntryReference[]): AreaOption[] {
  const byKey: Record<string, AreaEntryReference> = {};

  entries.forEach((entry) => {
    const key = areaCompareValue(entry.area || '');

    if (key.length === 0) {
      return;
    }

    const current = byKey[key];

    if (!current || entry.id < current.id) {
      byKey[key] = entry;
    }
  });

  return Object.keys(byKey)
    .map((key) => ({ key, name: byKey[key].area }))
    .sort((left, right) => left.name.trim().localeCompare(right.name.trim(), 'de', { sensitivity: 'base' }));
}
