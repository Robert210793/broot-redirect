import { areaCompareValue, groupAreas, isSameArea } from '../../../src/webparts/brtqmRedirectRules/logic/AreaName';

describe('AreaName', () => {
  it('compares names without case and surrounding whitespace', () => {
    expect(areaCompareValue('  Finanzen ')).toBe('finanzen');
    expect(isSameArea('Finanzen', 'finanzen ')).toBe(true);
    expect(isSameArea('Finanzen', 'Finanz en')).toBe(false);
  });

  it('lists each area once and uses the spelling of the entry with the smallest id', () => {
    const areas = groupAreas([
      { id: 7, area: 'finanzen' },
      { id: 3, area: 'Finanzen' },
      { id: 9, area: 'FINANZEN ' },
      { id: 4, area: 'Audit' }
    ]);

    expect(areas).toEqual([
      { key: 'audit', name: 'Audit' },
      { key: 'finanzen', name: 'Finanzen' }
    ]);
  });

  it('skips entries without area', () => {
    expect(groupAreas([{ id: 1, area: '' }, { id: 2, area: '   ' }])).toEqual([]);
  });
});
