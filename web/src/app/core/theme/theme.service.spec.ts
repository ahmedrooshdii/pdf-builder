import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BROWSER_STORAGE } from '../storage/browser-storage';
import { THEME_STORAGE_KEY, ThemeService } from './theme.service';

function setup(saved: string | null) {
  const store = new Map<string, string>(saved ? [[THEME_STORAGE_KEY, saved]] : []);
  const storage = {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, value: string) => void store.set(key, value),
  } as Storage;

  TestBed.configureTestingModule({ providers: [{ provide: BROWSER_STORAGE, useValue: storage }] });
  const service = TestBed.inject(ThemeService);
  TestBed.tick();
  return { service, store, root: TestBed.inject(DOCUMENT).documentElement };
}

describe('ThemeService', () => {
  afterEach(() => document.documentElement.classList.remove('dark'));

  it('starts from the saved choice and applies it to <html>', () => {
    const { service, root } = setup('dark');

    expect(service.theme()).toBe('dark');
    expect(root.classList).toContain('dark');
  });

  it('toggles, applies and remembers the new theme', () => {
    const { service, store, root } = setup('dark');

    service.toggle();
    TestBed.tick();

    expect(root.classList).not.toContain('dark');
    expect(store.get(THEME_STORAGE_KEY)).toBe('light');
  });

  it('ignores a saved value it does not recognise', () => {
    const { service } = setup('sepia');

    expect(['light', 'dark']).toContain(service.theme());
  });
});
