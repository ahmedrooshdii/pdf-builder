import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of } from 'rxjs';
import { ClientContext } from '../../../../core/client-context/client-context';
import { Client } from '../../../../shared/data-access/client.models';
import { ClientsApi } from '../../../../shared/data-access/clients.api';
import { polyfillDialog } from '../../../../shared/testing/polyfill-dialog';
import { ClientsPage } from './clients.page';

const client = (id: string, status: Client['status'] = 'Active'): Client => ({
  id,
  name: id === 'ecommerce' ? 'E-commerce' : id.toUpperCase(),
  status,
  createdAt: '2026-11-01T00:00:00Z',
  disabledAt: null,
});

function setup(list: readonly Client[] = [client('ecommerce'), client('erp', 'Disabled')]) {
  polyfillDialog();
  const value = signal<readonly Client[]>(list);
  const context = {
    clients: { hasValue: () => true, value, error: () => undefined },
    select: vi.fn(() => Promise.resolve(true)),
    reload: vi.fn(),
  };
  const api = {
    create: vi.fn((c: { id: string; name: string }): Observable<Client> => of(client(c.id))),
    update: vi.fn((id: string, changes: { disabled?: boolean }) => of(client(id, changes.disabled ? 'Disabled' : 'Active'))),
  };
  TestBed.configureTestingModule({
    providers: [
      { provide: ClientContext, useValue: context },
      { provide: ClientsApi, useValue: api },
    ],
  });
  const fixture = TestBed.createComponent(ClientsPage);
  TestBed.tick();
  const el = fixture.nativeElement as HTMLElement;
  const button = (text: string, within: ParentNode = el) =>
    Array.from(within.querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;
  const row = (id: string) => el.querySelector(`tr[data-client-id="${id}"]`) as HTMLElement;
  const settle = async () => {
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  };
  return { el, button, row, settle, context, api, value, fixture };
}

describe('ClientsPage', () => {
  it('lists name, id, state and created date', () => {
    const { el, row } = setup();
    const headers = Array.from(el.querySelectorAll('th')).map((th) => th.textContent?.trim());
    expect(headers).toEqual(['Name', 'Id', 'State', 'Created', 'Actions']);
    expect(row('ecommerce').textContent).toContain('Active');
    expect(row('erp').textContent).toContain('Disabled');
  });

  it('asks before disabling, naming the client and what happens to its keys', async () => {
    const { el, button, row, settle, api, context } = setup();

    button('Disable client', row('ecommerce')).click();
    await settle();
    expect(el.querySelector('#disable-title')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Disable E-commerce (ecommerce)?');
    expect(el.querySelector('#disable-body')?.textContent?.trim()).toBe(
      'Its keys stop working within a minute, until you enable it again. Nothing is deleted.',
    );
    expect(api.update).not.toHaveBeenCalled();

    button('Disable client', el.querySelector('dialog') as HTMLElement).click();
    await settle();
    expect(api.update).toHaveBeenCalledWith('ecommerce', { disabled: true });
    expect(context.reload).toHaveBeenCalled();
  });

  it('enables without asking', async () => {
    const { button, row, settle, api, context } = setup();
    button('Enable client', row('erp')).click();
    await settle();
    expect(api.update).toHaveBeenCalledWith('erp', { disabled: false });
    expect(context.reload).toHaveBeenCalled();
  });

  it('choosing a client opens its keys', () => {
    const { button, row, context } = setup();
    button('E-commerce', row('ecommerce')).click();
    expect(context.select).toHaveBeenCalledWith('ecommerce', '/keys');
  });

  it('chooses a client right after adding it', async () => {
    const { el, button, settle, api, context } = setup();
    button('Add client').click();
    await settle();

    const id = el.querySelector('#client-id') as HTMLInputElement;
    id.value = 'shop';
    id.dispatchEvent(new Event('input'));
    const name = el.querySelector('#client-name') as HTMLInputElement;
    name.value = 'Shop';
    name.dispatchEvent(new Event('input'));
    TestBed.tick();
    el.querySelector<HTMLButtonElement>('dialog button[type=submit]')?.click();
    await settle();

    expect(api.create).toHaveBeenCalledWith({ id: 'shop', name: 'Shop' });
    expect(context.reload).toHaveBeenCalled();
    expect(context.select).toHaveBeenCalledWith('shop', '/clients');
  });

  it('opens the add dialog when linked with ?add=1', () => {
    polyfillDialog();
    const { fixture, el } = setup();
    fixture.componentRef.setInput('add', '1');
    fixture.componentInstance.ngOnInit();
    TestBed.tick();
    expect(el.querySelector('#add-client-title')).not.toBeNull();
  });

  it('has an empty state that leads to adding the first client', () => {
    const { el } = setup([]);
    expect(el.textContent).toContain('Add a client to get started. Each product that calls this service is a client.');
  });
});
