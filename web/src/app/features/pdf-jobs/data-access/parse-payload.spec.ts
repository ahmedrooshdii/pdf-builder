import { parsePayload } from './parse-payload';

describe('parsePayload', () => {
  it('accepts a JSON object', () => {
    expect(parsePayload('{ "a": 1 }')).toEqual({ ok: true, value: { a: 1 } });
  });

  it.each([
    ['empty text', '   '],
    ['malformed JSON', '{ "a": '],
    ['an array', '[1, 2]'],
    ['a bare value', '42'],
    ['null', 'null'],
  ])('rejects %s', (_, text) => {
    expect(parsePayload(text).ok).toBe(false);
  });

  it('rejects a payload over 5 MB', () => {
    const big = `{ "x": "${'a'.repeat(5 * 1024 * 1024)}" }`;
    expect(parsePayload(big)).toEqual({ ok: false, error: 'Payload exceeds 5 MB.' });
  });
});
