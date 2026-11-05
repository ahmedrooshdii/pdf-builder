import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { polyfillDialog } from '../../../../shared/testing/polyfill-dialog';
import { ApiKey } from '../data-access/api-key.models';
import { RevokeKeyDialog } from './revoke-key-dialog';

const KEY: ApiKey = {
  keyId: 'k7f2a9c1',
  display: 'pdf_k7f2a9c1_…',
  name: 'production',
  scopes: ['pdf:generate'],
  status: 'Active',
  createdAt: '2026-10-12T09:00:00Z',
  expiresAt: null,
  revokedAt: null,
  lastUsedAt: null,
};

@Component({
  imports: [RevokeKeyDialog],
  template: `<app-revoke-key-dialog [(open)]="open" [key]="key" clientId="ecommerce" (confirmed)="confirmed.set(true)" />`,
})
class Host {
  readonly key = KEY;
  readonly open = signal(true);
  readonly confirmed = signal(false);
}

function setup() {
  polyfillDialog();
  const fixture = TestBed.createComponent(Host);
  TestBed.tick();
  const el = fixture.nativeElement as HTMLElement;
  const button = (text: string) =>
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;
  return { el, button, host: fixture.componentInstance };
}

describe('RevokeKeyDialog', () => {
  it('names the key and the client, and says it cannot be undone', () => {
    const { el } = setup();
    expect(el.querySelector('#revoke-title')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'Revoke production (pdf_k7f2a9c1_…) for ecommerce?',
    );
    expect(el.querySelector('#revoke-body')?.textContent?.trim()).toBe(
      'Calls with this key stop working within a minute. This cannot be undone.',
    );
  });

  it('starts focus on Cancel', () => {
    const { button } = setup();
    expect(button('Cancel').hasAttribute('autofocus')).toBe(true);
  });

  it('does nothing on Cancel', () => {
    const { button, host } = setup();
    button('Cancel').click();
    TestBed.tick();
    expect(host.confirmed()).toBe(false);
    expect(host.open()).toBe(false);
  });

  it('confirms on Revoke key', () => {
    const { button, host } = setup();
    button('Revoke key').click();
    expect(host.confirmed()).toBe(true);
  });
});
