import { PageMargins, PageOptions, hasCustomSize } from '../../../shared/data-access/template.models';

/** Mirrors `PdfBuilder.Domain.Jobs.PdfJobStatus`, which the API serialises by name. */
export type PdfJobStatus = 'Pending' | 'Succeeded' | 'Failed';

/** Body of `GET /api/pdf/jobs/{jobId}`. */
export interface PdfJob {
  readonly jobId: string;
  readonly templateKey: string;
  readonly status: PdfJobStatus;
  readonly enqueuedAt: string;
  readonly completedAt: string | null;
  readonly fileId: string | null;
  readonly fileUrl: string | null;
  readonly sizeBytes: number | null;
  readonly errorCode: string | null;
  readonly errorMessage: string | null;
}

/**
 * Mirrors `PdfBuilder.Contracts.PdfPageSetup`: changes to the template's page setup for one job.
 * Anything left out keeps the template's value.
 */
export interface PageSetup {
  readonly format?: string;
  readonly width?: string;
  readonly height?: string;
  readonly landscape?: boolean;
  readonly printBackground?: boolean;
  readonly preferCssPageSize?: boolean;
  readonly margins?: Partial<PageMargins>;
}

/** Body of `POST /api/pdf/jobs`. */
export interface GeneratePdfRequest {
  readonly templateKey: string;
  readonly data: Readonly<Record<string, unknown>>;
  readonly pageSetup?: PageSetup;
}

/** Only what `edited` changes from `template`, so the request says exactly what differs. Undefined when nothing does. */
export function pageSetupBetween(template: PageOptions, edited: PageOptions): PageSetup | undefined {
  const setup: { -readonly [K in keyof PageSetup]: PageSetup[K] } = {};

  if (edited.width && edited.height) {
    if (edited.width !== template.width || edited.height !== template.height) {
      setup.width = edited.width;
      setup.height = edited.height;
    }
  } else if (edited.format !== template.format || hasCustomSize(template)) {
    setup.format = edited.format;
  }
  if (edited.landscape !== template.landscape) setup.landscape = edited.landscape;
  if (edited.printBackground !== template.printBackground) setup.printBackground = edited.printBackground;
  if (edited.preferCssPageSize !== template.preferCssPageSize) setup.preferCssPageSize = edited.preferCssPageSize;

  const margins: Partial<Record<keyof PageMargins, string>> = {};
  for (const side of ['top', 'right', 'bottom', 'left'] as const) {
    if (edited.margins[side] !== template.margins[side]) margins[side] = edited.margins[side];
  }
  if (Object.keys(margins).length > 0) setup.margins = margins;

  return Object.keys(setup).length > 0 ? setup : undefined;
}

/** 202 response of `POST /api/pdf/jobs`. */
export interface JobAccepted {
  readonly jobId: string;
  readonly statusUrl: string;
}

/** Mirrors `PdfBuilder.Contracts.PdfErrorCodes`. Branch on these, never on the message text. */
export const PdfErrorCodes = {
  ValidationFailed: 'VALIDATION_FAILED',
  TemplateNotFound: 'TEMPLATE_NOT_FOUND',
  TemplateRenderFailed: 'TEMPLATE_RENDER_FAILED',
  RenderFailed: 'RENDER_FAILED',
} as const;

/** Mirrors `GeneratePdfCommandValidator.MaxPayloadBytes`. */
export const MAX_PAYLOAD_BYTES = 5 * 1024 * 1024;

/** How long the API keeps job state (`JobStore:RetentionHours`). Older ids answer 404. */
export const JOB_RETENTION_HOURS = 24;

export function isTerminal(status: PdfJobStatus): boolean {
  return status !== 'Pending';
}

/** The record kept for a job the moment it is accepted, before the first poll returns. */
export function pendingJob(accepted: JobAccepted, templateKey: string, enqueuedAt: Date): PdfJob {
  return {
    jobId: accepted.jobId,
    templateKey,
    status: 'Pending',
    enqueuedAt: enqueuedAt.toISOString(),
    completedAt: null,
    fileId: null,
    fileUrl: null,
    sizeBytes: null,
    errorCode: null,
    errorMessage: null,
  };
}
