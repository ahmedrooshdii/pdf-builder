import { Component, computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { Client } from '../../../shared/data-access/client.models';
import { SessionService } from '../../auth/session.service';
import { ClientContext } from '../../client-context/client-context';
import { Shell } from './shell';

@Component({ template: '<p id="page">a client page</p>' })
class ClientPage {}

@Component({ template: '<p id="page">clients</p>' })
class ClientsPage {}

const client = (id: string, status: Client['status'] = 'Active'): Client => ({
  id,
  name: id === 'ecommerce' ? 'E-commerce' : id.toUpperCase(),
  status,
  createdAt: '2026-11-01T00:00:00Z',
  disabledAt: null,
});

async function setup(options: { current?: string | null; clients?: readonly Client[]; unknown?: string | null; url?: string } = {}) {
  const current = signal<string | null>(options.current === undefined ? 'ecommerce' : options.current);
  const list = signal<readonly Client[]>(options.clients ?? [client('ecommerce'), client('erp', 'Disabled')]);
  const context = {
    current,
    currentClient: computed(() => list().find((c) => c.id === current()) ?? null),
    unknownClientId: signal(options.unknown ?? null),
    clients: { hasValue: () => true, value: list, error: () => undefined },
    select: vi.fn((id: string) => {
      current.set(id);
      return Promise.resolve(true);
    }),
    reload: vi.fn(),
  };
  const session = {
    state: signal('signed-in'),
    expiresAt: signal(new Date('2026-11-04T19:04:00Z')),
    signOut: vi.fn(() => of(undefined)),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([
        {
          path: '',
          component: Shell,
          children: [
            { path: 'keys', data: { needsClient: true }, component: ClientPage },
            { path: 'clients', component: ClientsPage },
          ],
        },
      ]),
      { provide: ClientContext, useValue: context },
      { provide: SessionService, useValue: session },
    ],
  });
  const harness = await RouterTestingHarness.create(options.url ?? '/keys?client=ecommerce');
  const el = harness.fixture.nativeElement as HTMLElement;
  return { el, harness, context, session, switcher: () => el.querySelector('[data-testid=client-switcher]') as HTMLElement };
}

describe('Shell', () => {
  it('names the chosen client, with its id, in the header', async () => {
    const { switcher, el } = await setup();
    expect(switcher().textContent).toContain('E-commerce');
    expect(switcher().textContent).toContain('ecommerce');
    expect(el.querySelector('#page')).not.toBeNull();
  });

  it('marks a disabled client', async () => {
    const { switcher } = await setup({ current: 'erp' });
    expect(switcher().textContent).toContain('Disabled');
  });

  it('lists every client in the switcher, and switches on choosing one', async () => {
    const { el, context } = await setup();
    const items = Array.from(el.querySelectorAll('[role=menu][aria-label=Clients] button[role=menuitem]'));
    expect(items.map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['E-commerce ecommerce', 'ERP erp · Disabled']);

    (items[1] as HTMLButtonElement).click();
    expect(context.select).toHaveBeenCalledWith('erp');
  });

  it('asks for a client in place of a page that needs one', async () => {
    const { el } = await setup({ current: null, url: '/keys' });
    expect(el.querySelector('#page')).toBeNull();
    expect(el.textContent).toContain('Choose the client to work on.');
  });

  it('says when the client in the address does not exist', async () => {
    const { el } = await setup({ current: null, unknown: 'nope', url: '/keys?client=nope' });
    expect(el.textContent?.replace(/\s+/g, ' ')).toContain('No client nope exists. Choose one.');
  });

  it('leads to adding a client when there are none', async () => {
    const { el } = await setup({ current: null, clients: [], url: '/keys' });
    expect(el.textContent).toContain('Add a client to get started. Each product that calls this service is a client.');
  });

  it('shows pages that need no client without one', async () => {
    const { el } = await setup({ current: null, url: '/clients' });
    expect(el.querySelector('#page')?.textContent).toBe('clients');
  });

  it('keeps the chosen client on every nav link', async () => {
    const { el } = await setup();
    const hrefs = Array.from(el.querySelectorAll('nav[aria-label=Main] a')).map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual([
      '/jobs/new?client=ecommerce',
      '/jobs?client=ecommerce',
      '/templates?client=ecommerce',
      '/keys?client=ecommerce',
      '/clients?client=ecommerce',
    ]);
  });

  it('shows when the session ends on the session menu itself', async () => {
    const { el } = await setup();
    const trigger = el.querySelector('[data-testid=session-menu]') as HTMLElement;
    expect(trigger.textContent).toMatch(/Operator\s+· until \d{2}:\d{2}/);
    expect(el.textContent).toContain('Signed in as operator');
    expect(el.textContent).toContain('2026-11-04 19:04 UTC');
  });

  it('signs out from the session menu', async () => {
    const { el, session } = await setup();
    const signOut = Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Sign out');
    signOut?.click();
    expect(session.signOut).toHaveBeenCalled();
  });
});
