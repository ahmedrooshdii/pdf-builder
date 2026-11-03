import { Component, computed, input, model } from '@angular/core';
import {
  PAGE_FORMATS,
  PAGE_LENGTH_PATTERN,
  AUTO_HEIGHT,
  PAGE_LENGTH_RULE,
  PageMargins,
  PageOptions,
  hasCustomSize,
  isAutoHeight,
} from '../data-access/template.models';
import { Input, Label } from './form-controls';
import { Select, SelectOption } from './select';
import { Switch } from './switch';

const CUSTOM = 'custom';

const SIZE_OPTIONS: readonly SelectOption[] = [
  ...PAGE_FORMATS.map((f) => ({ value: f, label: f })),
  { value: CUSTOM, label: 'Custom size', hint: 'receipts, labels' },
];

const SIDES: readonly { key: keyof PageMargins; label: string }[] = [
  { key: 'top', label: 'Top' },
  { key: 'right', label: 'Right' },
  { key: 'bottom', label: 'Bottom' },
  { key: 'left', label: 'Left' },
];

let nextId = 0;

/**
 * Paper size, orientation and margins, edited as one {@link PageOptions} value. Lengths are checked
 * as they are typed against the same rule the server applies, so a typo shows before saving.
 */
@Component({
  selector: 'app-page-setup-form',
  imports: [Input, Label, Select, Switch],
  template: `
    <fieldset class="grid gap-5" [disabled]="readonly()">
      <div class="grid gap-2">
        <label appLabel [for]="id + '-size'">Paper size</label>
        <app-select
          [inputId]="id + '-size'"
          [options]="sizeOptions"
          [disabled]="readonly()"
          [value]="sizeChoice()"
          (valueChange)="chooseSize($event)"
        />
      </div>

      @if (isCustom()) {
        <div class="grid grid-cols-2 gap-3">
          <div class="grid gap-2">
            <label appLabel [for]="id + '-width'">Width</label>
            <input
              appInput
              [id]="id + '-width'"
              placeholder="80mm"
              autocomplete="off"
              [attr.aria-invalid]="invalid(value().width) || null"
              [value]="value().width ?? ''"
              (input)="patch({ width: text($event) })"
            />
          </div>
          <div class="grid gap-2">
            <label appLabel [for]="id + '-height'">Height</label>
            <input
              appInput
              [id]="id + '-height'"
              placeholder="297mm"
              autocomplete="off"
              [disabled]="autoHeight()"
              [attr.aria-invalid]="(!autoHeight() && invalid(value().height)) || null"
              [value]="autoHeight() ? 'Fits the content' : (value().height ?? '')"
              (input)="patch({ height: text($event) })"
            />
          </div>
          <div class="col-span-2 flex items-start justify-between gap-4">
            <div class="grid gap-1">
              <label appLabel [for]="id + '-auto'">Fit height to content</label>
              <p class="text-xs text-muted-foreground">
                One page exactly as tall as the document, like a till roll. Orientation and the template's
                <code class="font-mono">&#64;page</code> size are ignored.
              </p>
            </div>
            <button
              appSwitch
              aria-label="Fit height to content"
              [id]="id + '-auto'"
              [checked]="autoHeight()"
              (checkedChange)="patch({ height: $event ? auto : '297mm' })"
            ></button>
          </div>
          <p class="col-span-2 text-xs text-muted-foreground">
            A till receipt is usually 80mm or 58mm wide. {{ lengthRule }}
          </p>
        </div>
      }

      @if (!autoHeight()) {
        <div class="flex items-center justify-between gap-4">
          <label appLabel [for]="id + '-landscape'">Landscape</label>
          <button appSwitch aria-label="Landscape" [id]="id + '-landscape'" [checked]="value().landscape" (checkedChange)="patch({ landscape: $event })"></button>
        </div>
      }

      <div class="grid gap-2">
        <span class="text-sm leading-none font-medium">Margins</span>
        <div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
          @for (side of sides; track side.key) {
            <div class="grid gap-1.5">
              <label appLabel class="text-xs text-muted-foreground" [for]="id + '-margin-' + side.key">{{ side.label }}</label>
              <input
                appInput
                autocomplete="off"
                [id]="id + '-margin-' + side.key"
                [attr.aria-invalid]="invalid(value().margins[side.key]) || null"
                [value]="value().margins[side.key]"
                (input)="patchMargin(side.key, text($event))"
              />
            </div>
          }
        </div>
        @if (anyMarginInvalid()) {
          <p class="text-xs text-destructive">{{ lengthRule }}</p>
        }
      </div>

      <div class="flex items-start justify-between gap-4">
        <div class="grid gap-1">
          <label appLabel [for]="id + '-background'">Print backgrounds</label>
          <p class="text-xs text-muted-foreground">Background colours and images. Off saves ink.</p>
        </div>
        <button
          appSwitch
          aria-label="Print backgrounds"
          [id]="id + '-background'"
          [checked]="value().printBackground"
          (checkedChange)="patch({ printBackground: $event })"
        ></button>
      </div>

      <div class="flex items-start justify-between gap-4">
        <div class="grid gap-1">
          <label appLabel [for]="id + '-css'">Let CSS decide the size</label>
          <p class="text-xs text-muted-foreground">
            When on, an <code class="font-mono">&#64;page {{ '{' }} size: … {{ '}' }}</code> rule in the template wins over the size above.
          </p>
        </div>
        <button
          appSwitch
          aria-label="Let CSS decide the size"
          [id]="id + '-css'"
          [checked]="value().preferCssPageSize"
          (checkedChange)="patch({ preferCssPageSize: $event })"
        ></button>
      </div>
    </fieldset>
  `,
})
export class PageSetupForm {
  readonly value = model.required<PageOptions>();
  readonly readonly = input(false);

  protected readonly id = `page-setup-${nextId++}`;
  protected readonly sizeOptions = SIZE_OPTIONS;
  protected readonly sides = SIDES;
  protected readonly lengthRule = PAGE_LENGTH_RULE;

  protected readonly auto = AUTO_HEIGHT;

  protected readonly isCustom = computed(() => this.value().width != null || this.value().height != null);
  protected readonly autoHeight = computed(() => isAutoHeight(this.value().height));
  protected readonly sizeChoice = computed(() => (this.isCustom() ? CUSTOM : formatName(this.value().format)));
  protected readonly anyMarginInvalid = computed(() => SIDES.some((s) => this.invalid(this.value().margins[s.key])));

  protected chooseSize(choice: string): void {
    if (choice === CUSTOM) {
      // Start from something printable rather than two empty boxes: the commonest custom paper,
      // an 80mm till roll.
      if (!hasCustomSize(this.value())) this.patch({ width: '80mm', height: AUTO_HEIGHT });
    } else {
      this.patch({ format: choice, width: null, height: null });
    }
  }

  protected patch(change: Partial<PageOptions>): void {
    this.value.update((v) => ({ ...v, ...change }));
  }

  protected patchMargin(side: keyof PageMargins, length: string): void {
    this.value.update((v) => ({ ...v, margins: { ...v.margins, [side]: length } }));
  }

  protected invalid(length: string | null | undefined): boolean {
    return length != null && !PAGE_LENGTH_PATTERN.test(length);
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement).value.trim();
  }
}

/** The server matches formats case-insensitively; the select needs the canonical spelling. */
function formatName(format: string): string {
  return PAGE_FORMATS.find((f) => f.toLowerCase() === format.toLowerCase()) ?? format;
}

/** True when every length in the options would pass the server's check. */
export function validPageOptions(options: PageOptions): boolean {
  const lengths = [options.margins.top, options.margins.right, options.margins.bottom, options.margins.left];
  if (options.width != null || options.height != null) {
    lengths.push(options.width ?? '');
    if (!isAutoHeight(options.height)) lengths.push(options.height ?? '');
  }
  return lengths.every((l) => PAGE_LENGTH_PATTERN.test(l));
}
