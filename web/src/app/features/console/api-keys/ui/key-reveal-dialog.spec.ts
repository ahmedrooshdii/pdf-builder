import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BROWSER_STORAGE } from '../../../../core/storage/browser-storage';
import { polyfillDialog } from '../../../../shared/testing/polyfill-dialog';
import { KeyRevealDialog } from './key-reveal-dialog';

const KEY = 'pdf_k7f2a9c1_Q8vLx2Tn9WcZr4YpB6sK1dHf7MjG3aE0uVnR5tXo';

@Component({
  imports: [KeyRevealDialog],
  template: `
    @if (shown()) {
      <app-key-reveal-dialog [key]="key" clientId="ecommerce" (confirmed)="confirmed.set(true)" />
    }
  `,
})
class Host {
  readonly key = KEY;
  readonly shown = signal(true);
  readonly confirmed = signal(false);
}

function setup(clipboard: Partial<Clipboard> | undefined = { writeText: vi.fn(() => Promise.resolve()) }) {
  polyfillDialog();
  Object.defineProperty(navigator, 'clipboard', { value: clipboard, configurable: true });
  const storage = { getItem: vi.fn(), setItem: vi.fn(), removeItem: vi.fn() } as unknown as Storage;
  TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: BROWSER_STORAGE, useValue: storage }] });

  const local = vi.spyOn(Storage.prototype, 'setItem');
  const logs = (['log', 'info', 'warn', 'error', 'debug'] as const).map((method) =>
    vi.spyOn(console, method).mockImplementation(() => undefined),
  );

  const fixture = TestBed.createComponent(Host);
  TestBed.tick();
  const el = fixture.nativeElement as HTMLElement;
  const dialog = () => el.querySelector('dialog') as HTMLDialogElement;
  const button = (text: string) =>
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;
  const settle = async () => {
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  };

  const restore = () => {
    local.mockRestore();
    logs.forEach((spy) => spy.mockRestore());
  };

  return { el, dialog, button, settle, storage, local, logs, restore, host: fixture.componentInstance, clipboard };
}

describe('KeyRevealDialog', () => {
  it('shows the key once, with the warning', () => {
    const { el, restore } = setup();
    expect(el.querySelector('[data-testid=revealed-key]')?.textContent).toBe(KEY);
    expect(el.textContent).toContain('Copy it now. It cannot be shown again.');
    expect(el.textContent).toContain('Copy the key for ecommerce');
    restore();
  });

  it('has no close button and ignores Escape, the backdrop and the browser closing it', () => {
    const { dialog, restore } = setup();
    expect(dialog().querySelector('button[aria-label=Close]')).toBeNull();

    const cancel = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(cancel);
    expect(cancel.defaultPrevented).toBe(true);

    dialog().dispatchEvent(new MouseEvent('click', { bubbles: true }));
    dialog().close();
    TestBed.tick();

    expect(dialog().hasAttribute('open')).toBe(true);
    restore();
  });

  it('copies the key', async () => {
    const { button, settle, clipboard, restore } = setup();
    button('Copy').click();
    await settle();
    expect(clipboard?.writeText).toHaveBeenCalledWith(KEY);
    restore();
  });

  it('selects the key and says how to copy it when the browser refuses', async () => {
    const { el, button, settle, restore } = setup({ writeText: vi.fn(() => Promise.reject(new Error('denied'))) });
    const selectAll = vi.fn();
    vi.spyOn(globalThis, 'getSelection').mockReturnValue({ selectAllChildren: selectAll } as unknown as Selection);

    button('Copy').click();
    await settle();

    expect(el.textContent).toContain('Copy did not work. The key is selected: press Ctrl+C.');
    expect(selectAll).toHaveBeenCalledWith(el.querySelector('[data-testid=revealed-key]'));
    restore();
  });

  it('drops the key when the operator says it is copied', () => {
    const { el, button, host, restore } = setup();
    button('I have copied it').click();
    TestBed.tick();

    expect(host.confirmed()).toBe(true);
    expect(el.querySelector('[data-testid=revealed-key]')).toBeNull();
    expect(el.textContent).not.toContain(KEY);
    restore();
  });

  it('never puts the key in storage, the console, the address or the history', async () => {
    const { button, settle, storage, local, logs, restore } = setup();
    const router = TestBed.inject(Router);

    button('Copy').click();
    await settle();
    button('I have copied it').click();
    TestBed.tick();

    const calls = [
      ...(storage.setItem as ReturnType<typeof vi.fn>).mock.calls,
      ...local.mock.calls,
      ...logs.flatMap((spy) => spy.mock.calls),
    ];
    expect(JSON.stringify(calls)).not.toContain(KEY);
    expect(router.url).not.toContain(KEY);
    expect(location.href).not.toContain(KEY);
    expect(JSON.stringify(history.state ?? null)).not.toContain(KEY);
    restore();
  });
});
