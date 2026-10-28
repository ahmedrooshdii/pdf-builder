import { Component, Directive, OnDestroy, computed, input, signal } from '@angular/core';
import { type VariantProps, cva } from 'class-variance-authority';
import { Button } from './button';
import { type ClassValue, cn } from './cn';
import { Icon } from './icon';

/*
 * shadcn/ui `base-nova` Code Block: a bordered surface with an optional header (title + actions),
 * line numbers drawn with CSS counters so they are never copied, and a copy button. Highlighting
 * is limited to JSON, which is all this app shows, and done by a small tokenizer instead of Shiki.
 */

const codeBlockVariants = cva(
  'group/code-block relative flex w-full min-w-0 flex-col overflow-hidden rounded-lg text-sm has-[>[data-slot=code-block-content]:focus-visible]:ring-3 has-[>[data-slot=code-block-content]:focus-visible]:ring-ring/50 has-[[data-slot=code-editor]:focus-within]:border-ring has-[[data-slot=code-editor]:focus-within]:ring-3 has-[[data-slot=code-editor]:focus-within]:ring-ring/50 data-[size=sm]:text-xs',
  {
    variants: {
      variant: {
        default:
          'border border-input bg-(--code-surface) text-foreground has-[>[data-slot=code-block-content]:focus-visible]:border-ring',
        destructive: 'border border-destructive/30 text-destructive dark:border-destructive/40',
      },
    },
    defaultVariants: { variant: 'default' },
  },
);

@Directive({
  selector: '[appCodeBlock]',
  host: {
    'data-slot': 'code-block',
    '[attr.data-variant]': 'variant()',
    '[attr.data-size]': 'size()',
    '[class]': 'computedClass()',
  },
})
export class CodeBlock {
  readonly variant = input<VariantProps<typeof codeBlockVariants>['variant']>('default');
  readonly size = input<'default' | 'sm'>('default');
  readonly userClass = input<ClassValue>('', { alias: 'class' });
  protected readonly computedClass = computed(() =>
    cn(codeBlockVariants({ variant: this.variant() }), this.userClass()),
  );
}

@Directive({
  selector: '[appCodeBlockHeader]',
  host: {
    'data-slot': 'code-block-header',
    class:
      'flex min-w-0 shrink-0 items-center gap-2 px-3 pt-3 text-muted-foreground group-data-[size=sm]/code-block:px-2.5 group-data-[size=sm]/code-block:pt-2.5 group-data-[variant=destructive]/code-block:text-destructive',
  },
})
export class CodeBlockHeader {}

@Directive({
  selector: '[appCodeBlockTitle]',
  host: {
    'data-slot': 'code-block-title',
    class:
      "flex min-w-0 items-center gap-1.5 truncate font-sans text-sm font-medium text-foreground group-data-[size=sm]/code-block:text-xs group-data-[variant=destructive]/code-block:text-destructive [&_app-icon:not([class*='size-'])]:size-3.5 [&_app-icon]:text-muted-foreground",
  },
})
export class CodeBlockTitle {}

@Directive({
  selector: '[appCodeBlockActions]',
  host: { 'data-slot': 'code-block-actions', class: '-my-1 ms-auto -me-1.5 flex shrink-0 items-center gap-1' },
})
export class CodeBlockActions {}

@Directive({
  selector: '[appCodeBlockFooter]',
  host: {
    'data-slot': 'code-block-footer',
    class:
      'flex min-w-0 shrink-0 items-center gap-2 border-t px-3 py-2 text-muted-foreground group-data-[size=sm]/code-block:px-2.5',
  },
})
export class CodeBlockFooter {}

/** Copies `value`, and says so for two seconds, to sighted and screen-reader users alike. */
@Component({
  selector: 'app-code-block-copy-button',
  imports: [Button, Icon],
  template: `
    <button
      appButton
      type="button"
      variant="ghost"
      size="icon-xs"
      data-slot="code-block-copy-button"
      [attr.aria-label]="copied() ? 'Copied' : label()"
      (click)="copy()"
    >
      <app-icon [name]="copied() ? 'check' : 'copy'" />
    </button>
    <span aria-live="polite" class="sr-only">{{ copied() ? 'Copied' : '' }}</span>
  `,
  host: { class: 'contents' },
})
export class CodeBlockCopyButton implements OnDestroy {
  readonly value = input.required<string>();
  readonly label = input('Copy');

  protected readonly copied = signal(false);
  private timer: ReturnType<typeof setTimeout> | undefined;

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.value());
    } catch {
      return; // Clipboard denied (insecure context, permissions): nothing to confirm.
    }
    this.copied.set(true);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.copied.set(false), 2000);
  }

  ngOnDestroy(): void {
    clearTimeout(this.timer);
  }
}

type TokenKind = 'key' | 'string' | 'number' | 'keyword' | 'punctuation' | 'plain';

interface Token {
  readonly kind: TokenKind;
  readonly text: string;
}

const JSON_TOKEN = /("(?:[^"\\]|\\.)*")(\s*:)?|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)|\b(true|false|null)\b|([{}[\],])/g;

/** Splits one line of JSON into coloured runs. Text it does not recognise passes through plain. */
export function tokenizeJson(line: string): Token[] {
  const tokens: Token[] = [];
  let last = 0;
  for (const match of line.matchAll(JSON_TOKEN)) {
    if (match.index > last) tokens.push({ kind: 'plain', text: line.slice(last, match.index) });
    const [whole, str, colon, num, keyword, punct] = match;
    if (str !== undefined) {
      tokens.push({ kind: colon ? 'key' : 'string', text: str });
      if (colon) tokens.push({ kind: 'punctuation', text: colon });
    } else if (num !== undefined) {
      tokens.push({ kind: 'number', text: num });
    } else if (keyword !== undefined) {
      tokens.push({ kind: 'keyword', text: keyword });
    } else if (punct !== undefined) {
      tokens.push({ kind: 'punctuation', text: punct });
    }
    last = match.index + whole.length;
  }
  if (last < line.length) tokens.push({ kind: 'plain', text: line.slice(last) });
  return tokens;
}

const TOKEN_CLASS: Readonly<Record<TokenKind, string>> = {
  key: 'text-(--code-key)',
  string: 'text-(--code-string)',
  number: 'text-(--code-number)',
  keyword: 'text-(--code-keyword)',
  punctuation: 'text-(--code-punctuation)',
  plain: '',
};

/** The scrolling `<pre>`. Give it `code` as a string; it splits lines and numbers them. */
@Component({
  selector: 'app-code-block-content',
  template: `
    <pre
      data-slot="code-block-content"
      dir="ltr"
      tabindex="0"
      [attr.data-wrap]="wrap() || null"
      [attr.data-numbered]="lineNumbers() || null"
      [class]="preClass()"
    ><code data-slot="code-block-code" class="grid min-h-lh min-w-max content-start in-data-numbered:grid-cols-[auto_1fr] in-data-wrap:min-w-0">@for (line of lines(); track $index) {<span data-slot="code-block-line" class="relative -mx-3 block min-h-lh px-3 group-data-[size=sm]/code-block:-mx-2.5 group-data-[size=sm]/code-block:px-2.5 before:min-w-6 before:text-end before:text-muted-foreground in-data-numbered:col-span-2 in-data-numbered:grid in-data-numbered:grid-cols-subgrid in-data-numbered:gap-x-3 in-data-numbered:[counter-increment:line] in-data-numbered:before:content-[counter(line)] in-data-numbered:before:select-none"><span data-slot="code-block-line-content" class="min-w-0">@for (token of line; track $index) {<span [class]="tokenClass[token.kind]">{{ token.text }}</span>} @empty {<ng-container>{{ newline }}</ng-container>}</span></span>}</code></pre>
  `,
  host: { class: 'contents' },
})
export class CodeBlockContent {
  readonly code = input.required<string>();
  readonly language = input<'json' | 'text'>('text');
  readonly lineNumbers = input(false);
  readonly wrap = input(false);
  readonly userClass = input<ClassValue>('', { alias: 'class' });

  protected readonly tokenClass = TOKEN_CLASS;
  /** An empty line holds a newline, so copying a selection keeps blank lines. */
  protected readonly newline = '\n';

  protected readonly lines = computed(() =>
    this.code()
      .replace(/\n$/, '')
      .split('\n')
      .map((line) =>
        this.language() === 'json' ? tokenizeJson(line) : line ? [{ kind: 'plain' as const, text: line }] : [],
      ),
  );

  protected readonly preClass = computed(() =>
    cn(
      '[counter-reset:line] min-h-0 min-w-0 scroll-fade-x scrollbar-thin overflow-auto px-3 py-3 text-start font-mono leading-relaxed outline-none group-has-data-[slot=code-block-footer]/code-block:pb-2 group-has-data-[slot=code-block-header]/code-block:pt-2 group-data-[size=sm]/code-block:px-2.5 group-data-[size=sm]/code-block:py-2.5 data-wrap:scroll-fade-y data-wrap:wrap-break-word data-wrap:whitespace-pre-wrap',
      this.userClass(),
    ),
  );
}

export const CODE_BLOCK = [
  CodeBlock,
  CodeBlockHeader,
  CodeBlockTitle,
  CodeBlockActions,
  CodeBlockFooter,
  CodeBlockCopyButton,
  CodeBlockContent,
] as const;
