import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { polyfillDialog } from '../testing/polyfill-dialog';
import { DIALOG } from './dialog';

@Component({
  imports: [DIALOG],
  template: `
    <app-dialog [(open)]="open" [dismissible]="dismissible()" labelledBy="t">
      <h2 appDialogTitle id="t">Title</h2>
      <button type="button" id="inside">Inside</button>
    </app-dialog>
  `,
})
class Host {
  readonly open = signal(true);
  readonly dismissible = signal(true);
}

function setup(dismissible: boolean) {
  polyfillDialog();
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.dismissible.set(dismissible);
  TestBed.tick();
  const dialog = (fixture.nativeElement as HTMLElement).querySelector('dialog') as HTMLDialogElement;
  return { fixture, dialog, host: fixture.componentInstance };
}

describe('Dialog', () => {
  it('closes from its close button, a backdrop click or the browser when dismissible', () => {
    const { dialog, host } = setup(true);
    expect(dialog.hasAttribute('open')).toBe(true);

    dialog.querySelector<HTMLButtonElement>('button[aria-label=Close]')?.click();
    TestBed.tick();
    expect(host.open()).toBe(false);

    host.open.set(true);
    TestBed.tick();
    dialog.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    TestBed.tick();
    expect(host.open()).toBe(false);
  });

  describe('when it must be answered', () => {
    it('has no close button', () => {
      const { dialog } = setup(false);
      expect(dialog.querySelector('button[aria-label=Close]')).toBeNull();
    });

    it('ignores Escape', () => {
      const { dialog, host } = setup(false);
      const cancel = new Event('cancel', { cancelable: true });

      dialog.dispatchEvent(cancel);

      expect(cancel.defaultPrevented).toBe(true);
      expect(host.open()).toBe(true);
    });

    it('ignores a backdrop click', () => {
      const { dialog, host } = setup(false);
      dialog.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      TestBed.tick();
      expect(host.open()).toBe(true);
    });

    it('opens again when the browser closes it anyway', () => {
      const { dialog, host } = setup(false);

      dialog.close();
      TestBed.tick();

      expect(host.open()).toBe(true);
      expect(dialog.hasAttribute('open')).toBe(true);
    });

    it('closes when its own state says so', () => {
      const { dialog, host } = setup(false);
      host.open.set(false);
      TestBed.tick();
      expect(dialog.hasAttribute('open')).toBe(false);
    });
  });
});
