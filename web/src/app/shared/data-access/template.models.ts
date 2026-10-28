/** Mirrors `TemplateSource`: authored through the API, or shipped in the templates/ folder. */
export type TemplateSource = 'Authored' | 'BuiltIn';

/**
 * Mirrors `TemplateStatus`. Jobs can use `Published` and `Modified` templates (they render the
 * published version); a `Draft` has never been published and jobs cannot see it.
 */
export type TemplateStatus = 'Draft' | 'Published' | 'Modified';

/** Mirrors `PdfRenderOptions`. */
export interface PageOptions {
  readonly format: string;
  readonly landscape: boolean;
  readonly printBackground: boolean;
  readonly preferCssPageSize: boolean;
  readonly margins: { readonly top: string; readonly right: string; readonly bottom: string; readonly left: string };
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
  return a.html === b.html && a.css === b.css && a.sampleJson === b.sampleJson && JSON.stringify(a.options) === JSON.stringify(b.options);
}
