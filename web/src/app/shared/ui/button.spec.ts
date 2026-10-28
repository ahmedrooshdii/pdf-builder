import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Button } from './button';

@Component({
  imports: [Button],
  template: `
    <button appButton>Plain</button>
    <button appButton variant="outline" size="sm" class="px-1 w-full">Custom</button>
  `,
})
class Host {}

describe('Button', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    TestBed.tick();
    const [plain, custom] = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
    return { plain: plain.classList, custom: custom.classList };
  }

  it('applies the default variant and size', () => {
    const { plain } = render();
    expect(plain).toContain('bg-primary');
    expect(plain).toContain('h-8');
  });

  it('lets a caller class override a built-in one rather than sit beside it', () => {
    const { custom } = render();
    expect(custom).toContain('px-1');
    expect(custom).not.toContain('px-2.5');
    expect(custom).toContain('w-full');
    expect(custom).toContain('border');
    expect(custom).not.toContain('bg-primary');
  });
});
