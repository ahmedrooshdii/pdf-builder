import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../../core/http/api-base-url';
import { GeneratePdfRequest, JobAccepted, PdfJob } from './pdf-job.models';

/** A file id is 32 hex characters; a job id is a dashed GUID. Mixing them up is a 404. */
const FILE_ID_PATTERN = /^[0-9a-f]{32}$/i;

/** Thin typed wrapper over the HTTP endpoints. No state, no retries: those belong to callers. */
@Injectable({ providedIn: 'root' })
export class PdfJobsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  enqueue(request: GeneratePdfRequest): Observable<JobAccepted> {
    return this.http.post<JobAccepted>(`${this.baseUrl}/api/pdf/jobs`, request);
  }

  get(jobId: string): Observable<PdfJob> {
    return this.http.get<PdfJob>(`${this.baseUrl}/api/pdf/jobs/${encodeURIComponent(jobId)}`);
  }

  /**
   * The PDF's bytes, for previewing. The API serves files as `attachment`, so pointing an iframe
   * at {@link fileUrl} would start a download instead of showing the document.
   */
  download(fileId: string): Observable<Blob> {
    return this.http.get(this.fileUrl(fileId), { responseType: 'blob' });
  }

  /**
   * Where the browser downloads a finished PDF.
   *
   * Built from the file id rather than taken from the job's `fileUrl`, because that one is absolute
   * to the API's `Storage:PublicBaseUrl` and the browser should stay on this app's origin. The id
   * is checked first, so nothing else can be smuggled into the path.
   */
  fileUrl(fileId: string): string {
    if (!FILE_ID_PATTERN.test(fileId)) {
      throw new Error(`'${fileId}' is not a file id.`);
    }
    return `${this.baseUrl}/files/${fileId}`;
  }
}
