/**
 * Where to go after signing in. Only a path inside this app is accepted, so a link such as
 * `/sign-in?returnUrl=https://evil.example` cannot turn the sign-in page into an open redirect.
 * `//host` and `/\host` are rejected too: browsers read both as another origin.
 */
export function safeReturnUrl(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) return '/';
  return value;
}
