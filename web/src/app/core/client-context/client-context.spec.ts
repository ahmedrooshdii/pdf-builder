import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Subject } from 'rxjs';
import { Client } from '../../shared/data-access/client.models';
import { ClientsApi } from '../../shared/data-access/clients.api';
import { SessionService } from '../auth/session.service';
import { BROWSER_STORAGE } from '../storage/browser-storage';
import { ClientContext } from './client-context';

@Component({ template: '' })
class Page {}

const client = (id: string): Client => ({ id, name: id.toUpperCase(), status: 'Active', createdAt: '2026-11-04T09:00:00Z', disabledAt: null });

class MemoryStorage {
  private readonly values = new Map<string, string>();
  getItem(key: string) {
    return this.values.get(key) ?? null;
  }
  setItem(key: string, value: string) {
    this.values.set(key, value);
  }
}

async function setup(options: { url?: string; clients?: readonly Client[]; storage?: Storage | null } = {}) {
  const list = new Subject<readonly Client[]>();
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: '**', component: Page }]),
      { provide: SessionService, useValue: { state: signal('signed-in') } },
      { provide: ClientsApi, useValue: { list: () => list } },
      { provide: BROWSER_STORAGE, useValue: options.storage === undefined ? new MemoryStorage() : options.storage },
    ],
  });
  const harness = await RouterTestingHarness.create(options.url ?? '/keys');
  const context = TestBed.inject(ClientContext);
  const router = TestBed.inject(Router);

  const load = async (clients: readonly Client[]) => {
    TestBed.tick();
    list.next(clients);
    list.complete();
    TestBed.tick();
    await harness.fixture.whenStable();
  };
  if (options.clients) await load(options.clients);

  return { context, router, harness, load };
}

describe('ClientContext', () => {
  it('takes the client from the URL first', async () => {
    const storage = new MemoryStorage();
    storage.setItem('pdf-builder.client', 'erp');
    const { context } = await setup({ url: '/keys?client=ecommerce', storage: storage as unknown as Storage, clients: [client('ecommerce'), client('erp')] });

    expect(context.current()).toBe('ecommerce');
  });

  it('falls back to the client remembered in this browser, and writes it into the URL', async () => {
    const storage = new MemoryStorage();
    storage.setItem('pdf-builder.client', 'erp');
    const { context, router } = await setup({ storage: storage as unknown as Storage, clients: [client('ecommerce'), client('erp')] });

    expect(context.current()).toBe('erp');
    expect(router.url).toBe('/keys?client=erp');
  });

  it('chooses the only client when nothing else says which', async () => {
    const { context } = await setup({ clients: [client('ecommerce')] });
    expect(context.current()).toBe('ecommerce');
  });

  it('chooses nothing when there are several and nothing says which', async () => {
    const { context } = await setup({ clients: [client('ecommerce'), client('erp')] });
    expect(context.current()).toBeNull();
    expect(context.unknownClientId()).toBeNull();
  });

  it('reports an unknown client in the URL instead of falling back to another', async () => {
    const storage = new MemoryStorage();
    storage.setItem('pdf-builder.client', 'erp');
    const { context } = await setup({ url: '/keys?client=nope', storage: storage as unknown as Storage, clients: [client('erp')] });

    expect(context.current()).toBeNull();
    expect(context.unknownClientId()).toBe('nope');
  });

  it('selecting updates the URL and what is remembered', async () => {
    const storage = new MemoryStorage();
    const { context, router, harness } = await setup({ storage: storage as unknown as Storage, clients: [client('ecommerce'), client('erp')] });

    await context.select('erp');
    await harness.fixture.whenStable();

    expect(router.url).toBe('/keys?client=erp');
    expect(storage.getItem('pdf-builder.client')).toBe('erp');
    expect(context.current()).toBe('erp');
  });

  it('works without storage, and with storage that throws', async () => {
    const throwing = {
      getItem: () => {
        throw new Error('blocked');
      },
      setItem: () => {
        throw new Error('blocked');
      },
    } as unknown as Storage;
    const { context, harness } = await setup({ storage: throwing, clients: [client('ecommerce'), client('erp')] });

    await context.select('erp');
    await harness.fixture.whenStable();
    expect(context.current()).toBe('erp');

    TestBed.resetTestingModule();
    const none = await setup({ storage: null, clients: [client('ecommerce')] });
    expect(none.context.current()).toBe('ecommerce');
  });
});
