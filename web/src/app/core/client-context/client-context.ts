import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';
import { Client } from '../../shared/data-access/client.models';
import { ClientsApi } from '../../shared/data-access/clients.api';
import { SessionService } from '../auth/session.service';
import { SIGN_IN_PATH } from '../auth/redirect-to-sign-in';
import { BROWSER_STORAGE } from '../storage/browser-storage';

const STORAGE_KEY = 'pdf-builder.client';

/**
 * The client the console is working on (research R13).
 *
 * The URL's `?client=` is the source of truth, so a copied link opens on the client it was copied
 * from, and back/forward switch client too. The browser remembers the last choice as the default
 * for an address that names none; failing both, the only client is chosen when there is exactly
 * one. An id in the URL that names no client is reported (`unknownClientId`), never replaced by
 * another client's.
 */
@Injectable({ providedIn: 'root' })
export class ClientContext {
  private readonly router = inject(Router);
  private readonly storage = inject(BROWSER_STORAGE);
  private readonly api = inject(ClientsApi);
  private readonly session = inject(SessionService);

  /** Every client, loaded once signed in. */
  readonly clients = rxResource({
    params: () => (this.session.state() === 'signed-in' ? true : undefined),
    stream: () => this.api.list(),
  });

  private readonly fromUrl = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.readUrlClient()),
    ),
    { initialValue: this.readUrlClient() },
  );

  private readonly remembered = signal<string | null>(this.readRemembered());

  private readonly list = computed(() => (this.clients.hasValue() ? this.clients.value() : null));

  /** The chosen client's id, or null when none is chosen yet. */
  readonly current = computed<string | null>(() => {
    const list = this.list();
    const known = (id: string) => list === null || list.some((client) => client.id === id);

    const fromUrl = this.fromUrl();
    if (fromUrl) return known(fromUrl) ? fromUrl : null;

    const remembered = this.remembered();
    if (remembered && known(remembered)) return remembered;

    return list?.length === 1 ? list[0].id : null;
  });

  /** The chosen client's record, once the list has loaded. */
  readonly currentClient = computed<Client | null>(
    () => this.list()?.find((client) => client.id === this.current()) ?? null,
  );

  /** An id the URL names that no client has. */
  readonly unknownClientId = computed<string | null>(() => {
    const fromUrl = this.fromUrl();
    const list = this.list();
    return fromUrl && list && !list.some((client) => client.id === fromUrl) ? fromUrl : null;
  });

  constructor() {
    // A client chosen by default is written into the address too, so copying it keeps the client.
    effect(() => {
      const current = this.current();
      if (current && !this.fromUrl() && !this.router.url.startsWith(SIGN_IN_PATH)) {
        void this.navigateWithClient(current, this.router.url, true);
      }
    });
  }

  /** Chooses a client: on the current page, or on `path` when given. Remembered as the default. */
  select(clientId: string, path?: string): Promise<boolean> {
    this.remember(clientId);
    return this.navigateWithClient(clientId, path ?? this.router.url, false);
  }

  reload(): void {
    this.clients.reload();
  }

  private navigateWithClient(clientId: string, url: string, replaceUrl: boolean): Promise<boolean> {
    const tree = this.router.parseUrl(url);
    tree.queryParams = { ...tree.queryParams, client: clientId };
    return this.router.navigateByUrl(tree, { replaceUrl });
  }

  private readUrlClient(): string | null {
    const value = this.router.parseUrl(this.router.url).queryParams['client'];
    return typeof value === 'string' && value !== '' ? value : null;
  }

  private readRemembered(): string | null {
    try {
      return this.storage?.getItem(STORAGE_KEY) ?? null;
    } catch {
      return null;
    }
  }

  private remember(clientId: string): void {
    this.remembered.set(clientId);
    try {
      this.storage?.setItem(STORAGE_KEY, clientId);
    } catch {
      // Storage refused (quota, private window): the URL still carries the choice.
    }
  }
}
