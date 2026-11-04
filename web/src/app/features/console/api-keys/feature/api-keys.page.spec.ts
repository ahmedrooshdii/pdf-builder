import { HttpErrorResponse } from '@angular/common/http';
import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { ClientContext } from '../../../../core/client-context/client-context';
import { Client } from '../../../../shared/data-access/client.models';
import { polyfillDialog } from '../../../../shared/testing/polyfill-dialog';
import { ApiKey, IssuedKey } from '../data-access/api-key.models';
import { ApiKeysApi } from '../data-access/api-keys.api';
import { ApiKeysPage } from './api-keys.page';

const ISSUED = 'pdf_n3w4k3y0_Q8vLx2Tn9WcZr4YpB6sK1dHf7MjG3aE0uVnR5tXo';

function key(keyId: string, overrides: Partial<ApiKey> = {}): ApiKey {
  return {
    keyId,
    display: `pdf_${keyId}_…`,
    name: `key-${keyId}`,
    scopes: ['pdf:generate'],
    status: 'Active',
    createdAt: '2026-10-12T09:00:00Z',
    expiresAt: null,
    revokedAt: null,
    lastUsedAt: null,
    ...overrides,
  };
}

const client = (id: string, status: Client['status'] = 'Active'): Client => ({
  id,
  name: id.toUpperCase(),
  status,
  createdAt: '2026-11-01T00:00:00Z',
  disabledAt: null,
});

function setup(options: { revoke?: () => Observable<null>; status?: Client['status'] } = {}) {
  polyfillDialog();
  const current = signal<string | null>('ecommerce');
  const keysByClient: Record<string, ApiKey[]> = { ecommerce: [key('aaaaaaaa'), key('bbbbbbbb')], erp: [key('cccccccc')] };
  const api = {
    list: vi.fn((clientId: string) => of(keysByClient[clientId] ?? [])),
    scopes: vi.fn(() => of([{ name: 'pdf:generate', description: 'Submit jobs and read them.' }])),
    issue: vi.fn((): Observable<IssuedKey> => of({ ...key('n3w4k3y0'), key: ISSUED })),
    revoke: vi.fn(options.revoke ?? (() => of(null))),
  };
  TestBed.configureTestingModule({
    providers: [
      { provide: ApiKeysApi, useValue: api },
      {
        provide: ClientContext,
        useValue: { current, currentClient: computed(() => (current() ? client(current() ?? '', options.status) : null)) },
      },
    ],
  });

  const fixture = TestBed.createComponent(ApiKeysPage);
  TestBed.tick();
  const el = fixture.nativeElement as HTMLElement;
  const button = (text: string, within: ParentNode = el) =>
    Array.from(within.querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;
  const settle = async () => {
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  };
  const rows = () => Array.from(el.querySelectorAll('tr[data-key-id]')).map((tr) => tr.getAttribute('data-key-id'));

  return { el, button, settle, rows, api, current, page: fixture.componentInstance };
}

describe('ApiKeysPage', () => {
  it('lists the chosen client\'s keys, and only that client\'s', async () => {
    const { rows, current, settle } = setup();
    await settle();
    expect(rows()).toEqual(['aaaaaaaa', 'bbbbbbbb']);

    current.set('erp');
    await settle();
    expect(rows()).toEqual(['cccccccc']);
  });

  it('warns that a disabled client\'s keys are refused', async () => {
    const { el, settle } = setup({ status: 'Disabled' });
    await settle();
    expect(el.textContent).toContain('ecommerce is disabled. Its keys are refused until it is enabled again.');
  });

  it('reveals a new key, guards leaving while it is shown, and forgets it once copied', async () => {
    const { el, button, settle, api, page } = setup();
    await settle();

    button('New key').click();
    await settle();
    (el.querySelector('#key-name') as HTMLInputElement).value = 'production';
    el.querySelector('#key-name')?.dispatchEvent(new Event('input'));
    el.querySelector<HTMLButtonElement>('[role=checkbox]')?.click();
    TestBed.tick();
    button('Create key').click();
    await settle();

    expect(api.issue).toHaveBeenCalledWith('ecommerce', { name: 'production', scopes: ['pdf:generate'], expiresAt: null });
    expect(el.querySelector('[data-testid=revealed-key]')?.textContent).toBe(ISSUED);
    expect(page.hasUnsavedChanges()).toBe(true);

    const before = api.list.mock.calls.length;
    button('I have copied it').click();
    await settle();

    expect(page.hasUnsavedChanges()).toBe(false);
    expect(el.textContent).not.toContain(ISSUED);
    expect(api.list.mock.calls.length).toBeGreaterThan(before);
  });

  it('revokes after confirming, then reloads the list', async () => {
    const { el, button, settle, api } = setup();
    await settle();

    el.querySelector<HTMLButtonElement>('tr[data-key-id=aaaaaaaa] [appdropdownmenuitem]')?.click();
    await settle();
    expect(el.querySelector('#revoke-title')?.textContent).toContain('key-aaaaaaaa');

    const before = api.list.mock.calls.length;
    button('Revoke key', el.querySelector('dialog') as HTMLElement).click();
    await settle();

    expect(api.revoke).toHaveBeenCalledWith('ecommerce', 'aaaaaaaa');
    expect(api.list.mock.calls.length).toBeGreaterThan(before);
    expect(el.querySelector('#revoke-title')).toBeNull();
  });

  it('shows the problem and reloads when revoking fails', async () => {
    const { el, button, settle, api } = setup({
      revoke: () =>
        throwError(() => new HttpErrorResponse({ status: 404, error: { title: 'Key not found', code: 'KEY_NOT_FOUND' } })),
    });
    await settle();

    el.querySelector<HTMLButtonElement>('tr[data-key-id=aaaaaaaa] [appdropdownmenuitem]')?.click();
    await settle();
    const before = api.list.mock.calls.length;
    button('Revoke key', el.querySelector('dialog') as HTMLElement).click();
    await settle();

    expect(el.textContent).toContain('Key not found');
    expect(el.textContent).toContain('KEY_NOT_FOUND');
    expect(api.list.mock.calls.length).toBeGreaterThan(before);
  });

  it('shows why the keys could not be loaded, with a way to try again', async () => {
    const { el, settle, api, current } = setup();
    api.list.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    current.set('erp');
    await settle();

    expect(el.textContent).toContain('The API could not be reached.');
    expect(el.textContent).toContain('Try again');
  });
});
