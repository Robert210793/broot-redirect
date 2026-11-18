import { commentDisplayText, commentTimestamp } from '../../../src/webparts/brtqmRedirectRules/logic/CommentText';

describe('CommentText', () => {
  it('replaces mention placeholders with the mentioned names', () => {
    expect(commentDisplayText('Bitte prüfen @mention{0} und @mention{1}', [{ name: 'Anna Muster' }, { name: 'Ben Beispiel' }]))
      .toBe('Bitte prüfen @Anna Muster und @Ben Beispiel');
  });

  it('keeps placeholders without a known mention and plain text unchanged', () => {
    expect(commentDisplayText('@mention{3} <b>fett</b>', undefined)).toBe('@mention{3} <b>fett</b>');
  });

  it('formats the creation time like the export', () => {
    const created = '2026-11-18T09:15:00Z';
    const date = new Date(created);
    const two = (value: number): string => String(value).padStart(2, '0');

    expect(commentTimestamp({ id: '1', text: '', authorName: '', created }))
      .toBe(`${two(date.getDate())}.${two(date.getMonth() + 1)}.${date.getFullYear()} ${two(date.getHours())}:${two(date.getMinutes())}`);
  });
});
