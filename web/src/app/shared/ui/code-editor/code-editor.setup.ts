/*
 * Everything CodeMirror, in one module that CodeEditor imports dynamically. Nothing here is in the
 * initial bundle or in any page that does not show an editor, and each language pack is loaded
 * only when an editor in that language opens.
 */
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands';
import {
  HighlightStyle,
  bracketMatching,
  foldGutter,
  indentOnInput,
  syntaxHighlighting,
} from '@codemirror/language';
import { Compartment, EditorState, Extension } from '@codemirror/state';
import {
  EditorView,
  drawSelection,
  highlightActiveLine,
  highlightActiveLineGutter,
  keymap,
  lineNumbers,
  placeholder,
} from '@codemirror/view';
import { tags } from '@lezer/highlight';

export type CodeLanguage = 'json' | 'html' | 'css';

/** Colours come from the same CSS variables as the read-only code blocks, so both follow the theme. */
const highlighting = HighlightStyle.define([
  { tag: [tags.propertyName, tags.attributeName], color: 'var(--code-key)' },
  { tag: [tags.string, tags.attributeValue], color: 'var(--code-string)' },
  { tag: [tags.number, tags.unit, tags.color], color: 'var(--code-number)' },
  { tag: [tags.bool, tags.null, tags.keyword, tags.tagName, tags.atom], color: 'var(--code-keyword)' },
  { tag: [tags.className, tags.definitionKeyword, tags.modifier], color: 'var(--code-key)' },
  { tag: [tags.comment, tags.meta], color: 'var(--muted-foreground)', fontStyle: 'italic' },
  {
    tag: [tags.brace, tags.squareBracket, tags.angleBracket, tags.separator, tags.punctuation],
    color: 'var(--code-punctuation)',
  },
]);

/** Makes CodeMirror look like the shadcn surface around it rather than like CodeMirror. */
const chrome = EditorView.theme({
  '&': { height: '100%', fontSize: '13px', backgroundColor: 'transparent', color: 'var(--foreground)' },
  '&.cm-focused': { outline: 'none' },
  '.cm-scroller': { fontFamily: 'var(--font-mono)', lineHeight: '1.625', scrollbarWidth: 'thin' },
  '.cm-content': { padding: '8px 0', caretColor: 'var(--foreground)' },
  '.cm-line': { padding: '0 12px 0 4px' },
  // Opaque: the gutter stays put while the text scrolls sideways underneath it.
  '.cm-gutters': {
    backgroundColor: 'var(--code-surface)',
    borderRight: '1px solid var(--border)',
    color: 'var(--muted-foreground)',
  },
  '.cm-lineNumbers .cm-gutterElement': { padding: '0 4px 0 12px', minWidth: '28px' },
  '.cm-activeLine': { backgroundColor: 'color-mix(in oklch, var(--muted) 60%, transparent)' },
  '.cm-activeLineGutter': { backgroundColor: 'var(--code-surface)', color: 'var(--foreground)' },
  '.cm-cursor': { borderLeftColor: 'var(--foreground)' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection': {
    backgroundColor: 'color-mix(in oklch, var(--ring) 35%, transparent) !important',
  },
  '.cm-matchingBracket': { backgroundColor: 'var(--muted)', outline: '1px solid var(--border)' },
  '.cm-foldGutter .cm-gutterElement': { color: 'var(--muted-foreground)', cursor: 'pointer' },
  '.cm-placeholder': { color: 'var(--muted-foreground)' },
  '.cm-lintRange-error': {
    backgroundImage: 'none',
    textDecoration: 'underline wavy var(--destructive)',
    textUnderlineOffset: '3px',
  },
  '.cm-tooltip': {
    border: 'none',
    borderRadius: 'var(--radius-md)',
    backgroundColor: 'var(--popover)',
    color: 'var(--popover-foreground)',
    boxShadow: '0 4px 12px rgb(0 0 0 / 0.12), 0 0 0 1px color-mix(in oklch, var(--foreground) 10%, transparent)',
  },
  '.cm-diagnostic': { padding: '6px 10px', fontFamily: 'var(--font-sans)', fontSize: '12px' },
  '.cm-diagnostic-error': { borderLeft: '3px solid var(--destructive)' },
});

/** Loads one language pack. JSON also gets a linter, debounced so a large paste is parsed once. */
async function languageSupport(language: CodeLanguage): Promise<Extension> {
  switch (language) {
    case 'json': {
      const [{ json, jsonParseLinter }, { linter }] = await Promise.all([
        import('@codemirror/lang-json'),
        import('@codemirror/lint'),
      ]);
      return [json(), linter(jsonParseLinter(), { delay: 300 })];
    }
    case 'html': {
      const { html } = await import('@codemirror/lang-html');
      return html({ autoCloseTags: true });
    }
    case 'css': {
      const { css } = await import('@codemirror/lang-css');
      return css();
    }
  }
}

export interface CodeEditorOptions {
  readonly doc: string;
  readonly language: CodeLanguage;
  readonly readOnly: boolean;
  readonly placeholder: string;
  readonly contentAttributes: Record<string, string>;
  readonly onChange: (value: string) => void;
  readonly onBlur: () => void;
}

export interface CodeEditorHandle {
  /** Replaces the document, for changes made outside the editor (Load sample, Format, a reload). */
  setValue(value: string): void;
  setReadOnly(readOnly: boolean): void;
  /** Updates attributes on the editable element, for example `aria-invalid`. */
  setContentAttributes(attributes: Record<string, string>): void;
  destroy(): void;
}

export async function createCodeEditor(parent: HTMLElement, options: CodeEditorOptions): Promise<CodeEditorHandle> {
  const language = await languageSupport(options.language);
  const attributes = new Compartment();
  const editable = new Compartment();
  const editability = (readOnly: boolean) => [EditorState.readOnly.of(readOnly), EditorView.editable.of(!readOnly)];

  const view = new EditorView({
    parent,
    state: EditorState.create({
      doc: options.doc,
      extensions: [
        lineNumbers(),
        foldGutter(),
        highlightActiveLine(),
        highlightActiveLineGutter(),
        drawSelection(),
        history(),
        indentOnInput(),
        bracketMatching(),
        EditorState.tabSize.of(2),
        keymap.of([...defaultKeymap, ...historyKeymap, indentWithTab]),
        language,
        syntaxHighlighting(highlighting),
        placeholder(options.placeholder),
        chrome,
        editable.of(editability(options.readOnly)),
        attributes.of(EditorView.contentAttributes.of(options.contentAttributes)),
        EditorView.updateListener.of((update) => {
          if (update.docChanged) options.onChange(update.state.doc.toString());
          if (update.focusChanged && !update.view.hasFocus) options.onBlur();
        }),
      ],
    }),
  });

  return {
    setValue(value) {
      if (value === view.state.doc.toString()) return;
      view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } });
    },
    setReadOnly(readOnly) {
      view.dispatch({ effects: editable.reconfigure(editability(readOnly)) });
    },
    setContentAttributes(next) {
      view.dispatch({ effects: attributes.reconfigure(EditorView.contentAttributes.of(next)) });
    },
    destroy() {
      view.destroy();
    },
  };
}
