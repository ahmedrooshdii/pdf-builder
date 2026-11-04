import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ProblemDetails } from '../../../../core/http/problem-details';
import { polyfillDialog } from '../../../../shared/testing/polyfill-dialog';
import { IssueKeyRequest, Scope } from '../data-access/api-key.models';
import { CreateKeyDialog } from './create-key-dialog';

const SCOPES: readonly Scope[] = [
  { name: 'pdf:generate', description: 'Submit jobs and read them.' },
  { name: 'pdf:templates:read', description: 'List and read templates.' },
  { name: 'pdf:templates:write', description: 'Create, duplicate, edit, publish, delete and preview templates.' },
];

@Component({
  imports: [CreateKeyDialog],
  template: `
    <app-create-key-dialog
      [(open)]="open"
      clientId="ecommerce"
      [scopes]="scopes()"
      [busy]="busy()"
      [problem]="problem()"
      (submitted)="sent.set($event)"
    />
  `,
})
class Host {
  readonly open = signal(true);
  readonly scopes = signal<readonly Scope[] | null>(SCOPES);
  readonly busy = signal(false);
  readonly problem = signal<ProblemDetails | null>(null);
  readonly sent = signal<IssueKeyRequest | null>(null);
}

function setup() {
  polyfillDialog();
  const fixture = TestBed.createComponent(Host);
  TestBed.tick();
  const el = fixture.nativeElement as HTMLElement;
  const find = <T extends Element>(selector: string): T => {
    const found = el.querySelector<T>(selector);
    if (!found) throw new Error(`Nothing matches '${selector}'.`);
    return found;
  };
  const type = (selector: string, value: string) => {
    const input = find<HTMLInputElement>(selector);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    TestBed.tick();
  };
  const submit = async () => {
    find<HTMLButtonElement>('button[type=submit]').click();
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  };
  const text = (selector: string) => find(selector).textContent?.trim() ?? '';
  return { el, find, type, submit, text, host: fixture.componentInstance };
}

/** A `datetime-local` value `days` from now, in the browser's zone. */
function local(days: number): string {
  const at = new Date(Date.now() + days * 86_400_000);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}T${pad(at.getHours())}:${pad(at.getMinutes())}`;
}

describe('CreateKeyDialog', () => {
  it('names the client it creates for', () => {
    expect(setup().text('#create-key-title')).toBe('New key for ecommerce');
  });

  it('offers one checkbox per scope, each with its description', () => {
    const { el } = setup();
    const boxes = el.querySelectorAll('[role=checkbox]');
    expect(boxes).toHaveLength(3);
    expect(el.textContent).toContain('Submit jobs and read them.');
    expect(boxes[0].getAttribute('aria-describedby')).toBe('scope-0-description');
  });

  it('refuses an empty name and no scope, on their fields', async () => {
    const { submit, text, host } = setup();
    await submit();

    expect(host.sent()).toBeNull();
    expect(text('#key-name-errors')).toBe('Enter a name.');
    expect(text('#key-scopes-errors')).toBe('Choose at least one scope.');
  });

  it('refuses a name over 100 characters', async () => {
    const { type, submit, text, host } = setup();
    type('#key-name', 'x'.repeat(101));
    await submit();

    expect(host.sent()).toBeNull();
    expect(text('#key-name-errors')).toBe('Use at most 100 characters.');
  });

  it('refuses an expiry in the past', async () => {
    const { type, submit, text, host, el } = setup();
    type('#key-name', 'production');
    el.querySelector<HTMLButtonElement>('[role=checkbox]')?.click();
    type('#key-expires', local(-1));
    await submit();

    expect(host.sent()).toBeNull();
    expect(text('#key-expires-errors')).toBe('Choose a time in the future.');
  });

  it('shows a chosen expiry back with its UTC offset', () => {
    const { type, text } = setup();
    type('#key-expires', local(30));
    expect(text('#key-expires-hint')).toMatch(/^Stops working on \d{1,2} \w{3} \d{4}, \d{2}:\d{2} \(UTC[+-]\d{2}:\d{2}\)\.$/);
  });

  it('sends name, scopes and expiry', async () => {
    const { type, submit, host, el } = setup();
    type('#key-name', '  production  ');
    el.querySelector<HTMLButtonElement>('[role=checkbox]')?.click();
    TestBed.tick();
    await submit();

    expect(host.sent()).toEqual({ name: 'production', scopes: ['pdf:generate'], expiresAt: null });
  });

  it('puts field errors from a 400 on their fields', () => {
    const { host, text } = setup();
    host.problem.set({ status: 400, title: 'Invalid', errors: { Name: ['The name is too long.'] } });
    TestBed.tick();

    expect(text('#key-name-server')).toBe('The name is too long.');
  });

  it('cannot be submitted twice while the request runs', () => {
    const { host, find } = setup();
    host.busy.set(true);
    TestBed.tick();
    expect(find<HTMLButtonElement>('button[type=submit]').disabled).toBe(true);
  });

  it('shows that the scopes are loading', () => {
    const { host, el } = setup();
    host.scopes.set(null);
    TestBed.tick();
    expect(el.querySelector('[aria-label="Loading scopes"]')).not.toBeNull();
  });
});
