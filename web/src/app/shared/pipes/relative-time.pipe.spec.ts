import { TestBed } from '@angular/core/testing';
import { NOW } from '../util/clock';
import { RelativeTimePipe } from './relative-time.pipe';

const now = Date.parse('2026-11-04T12:00:00Z');

function pipe(): RelativeTimePipe {
  TestBed.configureTestingModule({ providers: [RelativeTimePipe, { provide: NOW, useValue: () => now }] });
  return TestBed.inject(RelativeTimePipe);
}

const ago = (ms: number) => new Date(now - ms).toISOString();

describe('RelativeTimePipe', () => {
  it('says never for a key that was never used', () => {
    expect(pipe().transform(null)).toBe('never');
  });

  it('rounds anything under a minute to less than a minute', () => {
    expect(pipe().transform(ago(30_000))).toBe('less than a minute ago');
  });

  it('counts minutes and hours under a day', () => {
    const p = pipe();
    expect(p.transform(ago(60_000))).toBe('1 minute ago');
    expect(p.transform(ago(3 * 60_000))).toBe('3 minutes ago');
    expect(p.transform(ago(5 * 3_600_000))).toBe('5 hours ago');
  });

  it('shows the date from a day on', () => {
    expect(pipe().transform(ago(2 * 86_400_000))).toBe('2 Nov 2026');
  });
});
