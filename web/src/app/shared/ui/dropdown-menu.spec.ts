import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { polyfillPopover } from '../testing/polyfill-popover';
import { DROPDOWN_MENU } from './dropdown-menu';

@Component({
  imports: [DROPDOWN_MENU],
  template: `
    <app-dropdown-menu label="Session">
      <button type="button" appDropdownMenuTrigger id="trigger">Open</button>
      <div appDropdownMenuLabel>Signed in</div>
      <button type="button" appDropdownMenuItem id="one" (click)="chosen.set('one')">One</button>
      <button type="button" appDropdownMenuItem id="two">Two</button>
      <button type="button" appDropdownMenuItem id="three">Three</button>
    </app-dropdown-menu>
  `,
})
class Host {
  readonly chosen = signal<string | null>(null);
}

function setup() {
  polyfillPopover();
  const fixture = TestBed.createComponent(Host);
  document.body.appendChild(fixture.nativeElement);
  TestBed.tick();
  const el = fixture.nativeElement as HTMLElement;
  const byId = (id: string) => el.querySelector<HTMLElement>(`#${id}`) as HTMLElement;
  const key = (key: string) => {
    (document.activeElement ?? el).dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
    TestBed.tick();
  };
  return { el, byId, key, host: fixture.componentInstance };
}

describe('DropdownMenu', () => {
  it('opens from its trigger onto the first item', () => {
    const { byId } = setup();
    const trigger = byId('trigger');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(trigger.getAttribute('aria-haspopup')).toBe('menu');

    trigger.click();
    TestBed.tick();

    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(document.activeElement).toBe(byId('one'));
  });

  it('moves between items with the arrows, Home and End, wrapping around', () => {
    const { byId, key } = setup();
    byId('trigger').click();
    TestBed.tick();

    key('ArrowDown');
    expect(document.activeElement).toBe(byId('two'));
    key('End');
    expect(document.activeElement).toBe(byId('three'));
    key('ArrowDown');
    expect(document.activeElement).toBe(byId('one'));
    key('ArrowUp');
    expect(document.activeElement).toBe(byId('three'));
    key('Home');
    expect(document.activeElement).toBe(byId('one'));
  });

  it('opens on the last item with ArrowUp on the trigger', () => {
    const { byId } = setup();
    byId('trigger').focus();
    byId('trigger').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }));
    TestBed.tick();
    expect(document.activeElement).toBe(byId('three'));
  });

  it('closes with Escape and gives focus back to the trigger', () => {
    const { byId, key } = setup();
    byId('trigger').click();
    TestBed.tick();

    key('Escape');

    expect(byId('trigger').getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(byId('trigger'));
  });

  it('closes when an item is chosen', () => {
    const { byId, host } = setup();
    byId('trigger').click();
    TestBed.tick();

    byId('one').click();
    TestBed.tick();

    expect(host.chosen()).toBe('one');
    expect(byId('trigger').getAttribute('aria-expanded')).toBe('false');
  });

  it('marks items as menu items', () => {
    const { el } = setup();
    expect(el.querySelectorAll('[role=menuitem]')).toHaveLength(3);
    expect(el.querySelector('[role=menu]')?.getAttribute('aria-label')).toBe('Session');
  });
});
