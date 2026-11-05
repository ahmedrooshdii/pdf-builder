import { safeReturnUrl } from './safe-return-url';

describe('safeReturnUrl', () => {
  it('keeps a path inside the app, query included', () => {
    expect(safeReturnUrl('/templates?client=a')).toBe('/templates?client=a');
  });

  it.each(['//evil.example', '/\\evil.example', 'https://evil.example', 'javascript:alert(1)', 'templates', ''])(
    'refuses %j and goes home instead',
    (value) => {
      expect(safeReturnUrl(value)).toBe('/');
    },
  );

  it('goes home when there is nothing to return to', () => {
    expect(safeReturnUrl(null)).toBe('/');
    expect(safeReturnUrl(undefined)).toBe('/');
  });
});
