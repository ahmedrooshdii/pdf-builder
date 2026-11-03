/** Mirrors `TemplateSource`: authored through the API, or shipped in the templates/ folder. */
export type TemplateSource = 'Authored' | 'BuiltIn';

/**
 * Mirrors `TemplateStatus`. Jobs can use `Published` and `Modified` templates (they render the
 * published version); a `Draft` has never been published and jobs cannot see it.
 */
export type TemplateStatus = 'Draft' | 'Published' | 'Modified';

export interface PageMargins {
  readonly top: string;
  readonly right: string;
  readonly bottom: string;
  readonly left: string;
}

/** Mirrors `PdfRenderOptions`. */
export interface PageOptions {
  /** A named paper size. Ignored when `width` and `height` are both set. */
  readonly format: string;
  /** Custom page size as CSS lengths, for receipts, labels and other paper with no name. */
  readonly width?: string | null;
  readonly height?: string | null;
  readonly landscape: boolean;
  readonly printBackground: boolean;
  readonly preferCssPageSize: boolean;
  readonly margins: PageMargins;
}

/** Mirrors `PdfRenderOptions.Default`. */
export const DEFAULT_PAGE_OPTIONS: PageOptions = {
  format: 'A4',
  width: null,
  height: null,
  landscape: false,
  printBackground: true,
  preferCssPageSize: true,
  margins: { top: '10mm', right: '10mm', bottom: '10mm', left: '10mm' },
};

/** Mirrors `PageSize.Formats` on the server. */
export const PAGE_FORMATS = ['A0', 'A1', 'A2', 'A3', 'A4', 'A5', 'A6', 'Letter', 'Legal', 'Tabloid', 'Ledger'] as const;

/** Mirrors `PageSize.IsValidLength`: a number and one of the units Chromium takes. */
export const PAGE_LENGTH_PATTERN = /^\d{1,5}(\.\d{1,4})?(mm|cm|in|px)$/;
export const PAGE_LENGTH_RULE = 'A number with mm, cm, in or px, for example 80mm.';

/** Mirrors `PageSize.Auto`: a height that fits the content, for one long page like a till roll. */
export const AUTO_HEIGHT = 'auto';

export function isAutoHeight(height: string | null | undefined): boolean {
  return height?.toLowerCase() === AUTO_HEIGHT;
}

export function hasCustomSize(options: PageOptions): boolean {
  return !!options.width && !!options.height;
}

/** Compared field by field, so key order from the server cannot make an untouched draft look edited. */
export function sameOptions(a: PageOptions, b: PageOptions): boolean {
  return (
    a.format === b.format &&
    (a.width ?? null) === (b.width ?? null) &&
    (a.height ?? null) === (b.height ?? null) &&
    a.landscape === b.landscape &&
    a.printBackground === b.printBackground &&
    a.preferCssPageSize === b.preferCssPageSize &&
    a.margins.top === b.margins.top &&
    a.margins.right === b.margins.right &&
    a.margins.bottom === b.margins.bottom &&
    a.margins.left === b.margins.left
  );
}

export interface TemplateContent {
  readonly html: string;
  readonly css: string;
  /** Kept as text so the editor shows it exactly as written. */
  readonly sampleJson: string;
  readonly options: PageOptions;
}

/** A row of `GET /api/templates`. */
export interface TemplateSummary {
  readonly templateKey: string;
  readonly source: TemplateSource;
  readonly status: TemplateStatus;
  /** Optimistic-concurrency token: send it back with a save or publish. */
  readonly revision: number;
  readonly updatedAt: string;
  readonly publishedAt: string | null;
}

/** `GET /api/templates/{templateKey}`. */
export interface TemplateDetail extends TemplateSummary {
  readonly draft: TemplateContent;
  readonly published: TemplateContent | null;
}

/** Mirrors the HTTP-only `TemplateErrorCodes`. */
export const TemplateErrorCodes = {
  KeyTaken: 'TEMPLATE_KEY_TAKEN',
  ReadOnly: 'TEMPLATE_READ_ONLY',
  RevisionConflict: 'TEMPLATE_REVISION_CONFLICT',
  RenderFailed: 'TEMPLATE_RENDER_FAILED',
} as const;

/** Mirrors `TemplateKey` on the server, where the key becomes a file name. */
export const TEMPLATE_KEY_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/;
export const TEMPLATE_KEY_RULE = "Letters, digits, '_' and '-' only, starting with a letter or digit (max 64).";

/** Whether a job can render this template right now. */
export function isUsable(template: TemplateSummary): boolean {
  return template.status !== 'Draft';
}

export function sameContent(a: TemplateContent, b: TemplateContent): boolean {
  return a.html === b.html && a.css === b.css && a.sampleJson === b.sampleJson && sameOptions(a.options, b.options);
}
