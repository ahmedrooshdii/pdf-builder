import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { polyfillDialog } from '../testing/polyfill-dialog';
import { ALERT_DIALOG } from './alert-dialog';

@Component({
  imports: [ALERT_DIALOG],
  template: `
    <app-alert-dialog [(open)]="open" labelledBy="t" describedBy="d">
      <h2 appDialogTitle id="t">Revoke production?</h2>
      <p appDialogDescription id="d">This cannot be undone.</p>
      <div appDialogFooter>
        <button type="button" appAlertDialogCancel (click)="open.set(false)">Cancel</button>
        <button type="button" id="confirm">Revoke key</button>
      </div>
    </app-alert-dialog>
  `,
})
class Host {
  readonly open = signal(true);
}

function setup() {
  polyfillDialog();
  const fixture = TestBed.createComponent(Host);
  TestBed.tick();
  const el = fixture.nativeElement as HTMLElement;
  return { el, host: fixture.componentInstance, dialog: el.querySelector('dialog') as HTMLDialogElement };
}

describe('AlertDialog', () => {
  it('is announced as an alert dialog with its title and description', () => {
    const { dialog } = setup();
    expect(dialog.getAttribute('role')).toBe('alertdialog');
    expect(dialog.getAttribute('aria-labelledby')).toBe('t');
    expect(dialog.getAttribute('aria-describedby')).toBe('d');
  });

  it('starts focus on Cancel, so Enter right away never confirms', () => {
    const { el } = setup();
    expect(el.querySelector('[appAlertDialogCancel]')?.hasAttribute('autofocus')).toBe(true);
  });

  it('has no corner close button and ignores the backdrop', () => {
    const { dialog, host } = setup();
    expect(dialog.querySelector('button[aria-label=Close]')).toBeNull();

    dialog.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    TestBed.tick();
    expect(host.open()).toBe(true);
  });

  it('treats Escape as Cancel', () => {
    const { dialog, host } = setup();
    dialog.close();
    TestBed.tick();
    expect(host.open()).toBe(false);
  });
});
