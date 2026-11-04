import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ProblemDetails } from '../../../../core/http/problem-details';
import { polyfillDialog } from '../../../../shared/testing/polyfill-dialog';
import { AddClientDialog } from './add-client-dialog';

@Component({
  imports: [AddClientDialog],
  template: `<app-add-client-dialog [(open)]="open" [problem]="problem()" (submitted)="sent.set($event)" />`,
})
class Host {
  readonly open = signal(true);
  readonly problem = signal<ProblemDetails | null>(null);
  readonly sent = signal<{ id: string; name: string } | null>(null);
}

function setup() {
  polyfillDialog();
  const fixture = TestBed.createComponent(Host);
  TestBed.tick();
  const el = fixture.nativeElement as HTMLElement;
  const type = (selector: string, value: string) => {
    const input = el.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    TestBed.tick();
  };
  const submit = async () => {
    el.querySelector<HTMLButtonElement>('button[type=submit]')?.click();
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  };
  const text = (selector: string) => el.querySelector(selector)?.textContent?.trim() ?? '';
  return { el, type, submit, text, host: fixture.componentInstance };
}

const RULE = 'Use lowercase letters, digits and hyphens, starting with a letter, up to 40 characters.';

describe('AddClientDialog', () => {
  it('says what an id may contain, and that it is permanent', () => {
    expect(setup().text('#client-id-hint')).toBe(
      'Lowercase letters, digits and hyphens, starting with a letter. Cannot be changed later.',
    );
  });

  it.each(['Ecommerce', '1shop', 'a_b', 'a'.repeat(41)])('refuses the id %j as it is typed', (id) => {
    const { type, text } = setup();
    type('#client-id', id);
    expect(text('#client-id-errors')).toBe(RULE);
  });

  it('accepts a well-formed id', () => {
    const { type, text } = setup();
    type('#client-id', 'e-commerce-2');
    expect(text('#client-id-errors')).toBe('');
  });

  it('refuses an empty name and one over 100 characters', async () => {
    const { type, submit, text, host } = setup();
    type('#client-id', 'ecommerce');
    await submit();
    expect(text('#client-name-errors')).toBe('Enter a name.');

    type('#client-name', 'x'.repeat(101));
    await submit();
    expect(text('#client-name-errors')).toBe('Use at most 100 characters.');
    expect(host.sent()).toBeNull();
  });

  it('shows a taken id on the Id field', () => {
    const { host, text } = setup();
    host.problem.set({ status: 409, title: 'Client id taken', code: 'CLIENT_ID_TAKEN' });
    TestBed.tick();
    expect(text('#client-id-server')).toBe('A client with this id already exists.');
  });

  it('sends id and name', async () => {
    const { type, submit, host } = setup();
    type('#client-id', 'ecommerce');
    type('#client-name', ' E-commerce ');
    await submit();
    expect(host.sent()).toEqual({ id: 'ecommerce', name: 'E-commerce' });
  });
});
