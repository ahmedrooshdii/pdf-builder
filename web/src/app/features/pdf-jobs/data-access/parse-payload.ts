import { MAX_PAYLOAD_BYTES } from './pdf-job.models';

export type ParsedPayload =
  | { readonly ok: true; readonly value: Record<string, unknown> }
  | { readonly ok: false; readonly error: string };

/**
 * Applies the API's payload rules before the request leaves the browser: a JSON object, at most
 * 5 MB of UTF-8. The server checks again; this only saves a round trip and gives a better message.
 */
export function parsePayload(text: string): ParsedPayload {
  if (text.trim() === '') {
    return { ok: false, error: 'Enter a JSON object to bind to the template.' };
  }

  if (new TextEncoder().encode(text).byteLength > MAX_PAYLOAD_BYTES) {
    return { ok: false, error: `Payload exceeds ${MAX_PAYLOAD_BYTES / 1024 / 1024} MB.` };
  }

  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch (error) {
    return { ok: false, error: `Not valid JSON: ${(error as Error).message}` };
  }

  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return { ok: false, error: 'The payload must be a JSON object, not an array or a single value.' };
  }

  return { ok: true, value: value as Record<string, unknown> };
}
