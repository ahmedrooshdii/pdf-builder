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

/** Body of `POST /api/pdf/jobs`. */
export interface GeneratePdfRequest {
  readonly templateKey: string;
  readonly data: Readonly<Record<string, unknown>>;
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
