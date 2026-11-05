import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NOW } from '../../../../shared/util/clock';
import { ApiKey } from '../data-access/api-key.models';
import { KeysTable } from './keys-table';

const now = Date.parse('2026-11-04T12:00:00Z');

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

@Component({
  imports: [KeysTable],
  template: `<app-keys-table [keys]="keys()" clientId="ecommerce" (revoke)="revoked.set($event)" (create)="created.set(true)" />`,
})
class Host {
  readonly keys = signal<readonly ApiKey[]>([]);
  readonly revoked = signal<ApiKey | null>(null);
  readonly created = signal(false);
}

function setup(keys: readonly ApiKey[]) {
  TestBed.configureTestingModule({ providers: [{ provide: NOW, useValue: () => now }] });
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.keys.set(keys);
  TestBed.tick();
  const el = fixture.nativeElement as HTMLElement;
  const row = (keyId: string) => el.querySelector(`tr[data-key-id="${keyId}"]`) as HTMLElement;
  return { el, row, host: fixture.componentInstance };
}

describe('KeysTable', () => {
  it('shows every column, and only the public part of the key', () => {
    const { el, row } = setup([key('k7f2a9c1', { name: 'production', lastUsedAt: new Date(now - 3 * 60_000).toISOString() })]);

    const headers = Array.from(el.querySelectorAll('th')).map((th) => th.textContent?.trim());
    expect(headers).toEqual(['Name', 'Key', 'Scopes', 'Last used', 'Created', 'Expires', 'State', 'Actions']);
    expect(row('k7f2a9c1').textContent).toContain('production');
    expect(row('k7f2a9c1').textContent).toContain('pdf_k7f2a9c1_…');
    expect(row('k7f2a9c1').textContent).toContain('3 minutes ago');
    expect(row('k7f2a9c1').textContent).toContain('Active');
  });

  it('says never for a key never used, and for no expiry', () => {
    const { row } = setup([key('a')]);
    const cells = row('a').querySelectorAll('td');
    expect(cells[3].textContent?.trim()).toBe('never');
    expect(cells[5].textContent?.trim()).toBe('never');
  });

  it('gives the exact UTC time to keyboard and screen reader users too', () => {
    const { row } = setup([key('a', { lastUsedAt: '2026-11-04T11:57:00Z' })]);
    const time = row('a').querySelectorAll('td')[3].querySelector('time') as HTMLElement;

    expect(time.getAttribute('tabindex')).toBe('0');
    expect(time.textContent).toContain('2026-11-04 11:57 UTC');
  });

  it('offers revoking only for active keys', () => {
    const { row } = setup([key('a'), key('b', { status: 'Revoked' }), key('c', { status: 'Expired' })]);

    expect(row('a').textContent).toContain('Revoke key');
    expect(row('b').textContent).not.toContain('Revoke key');
    expect(row('c').textContent).not.toContain('Revoke key');
    expect(row('b').textContent).toContain('Revoked');
    expect(row('c').textContent).toContain('Expired');
  });

  it('asks the page to revoke the chosen key', () => {
    const { row, host } = setup([key('a')]);
    const revoke = Array.from(row('a').querySelectorAll('button')).find((b) => b.textContent?.includes('Revoke key'));
    revoke?.click();
    expect(host.revoked()?.keyId).toBe('a');
  });

  it('has an empty state that offers the first key', () => {
    const { el, host } = setup([]);
    expect(el.textContent).toContain('No keys yet for ecommerce.');

    Array.from(el.querySelectorAll('button'))
      .find((b) => b.textContent?.includes('Create a key'))
      ?.click();
    expect(host.created()).toBe(true);
  });
});
