import { tokenizeJson } from './code-block';

describe('tokenizeJson', () => {
  const kinds = (line: string) => tokenizeJson(line).map((t) => [t.kind, t.text]);

  it('tells keys from string values', () => {
    expect(kinds('  "name": "Acme",')).toEqual([
      ['plain', '  '],
      ['key', '"name"'],
      ['punctuation', ':'],
      ['plain', ' '],
      ['string', '"Acme"'],
      ['punctuation', ','],
    ]);
  });

  it('recognises numbers, booleans and null', () => {
    expect(kinds('[1.5e3, -2, true, null]').filter(([kind]) => kind !== 'plain' && kind !== 'punctuation')).toEqual([
      ['number', '1.5e3'],
      ['number', '-2'],
      ['keyword', 'true'],
      ['keyword', 'null'],
    ]);
  });

  it('keeps escaped quotes inside one string', () => {
    expect(kinds('"say \\"hi\\""')).toEqual([['string', '"say \\"hi\\""']]);
  });

  it('loses no characters, even on text that is not JSON', () => {
    const line = '  oops: {"a": [1, x]}';
    expect(
      tokenizeJson(line)
        .map((t) => t.text)
        .join(''),
    ).toBe(line);
  });
});
