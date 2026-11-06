import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../../core/http/api-base-url';
import { GeneratePdfRequest, JobAccepted, PdfJob } from './pdf-job.models';

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
   * A job's PDF, for saving or previewing.
   *
   * Fetched with the console's own credentials rather than through a `/files/` link: a link works for
   * whoever holds it until it expires, and the console has no need to hold one. Another client's job,
   * or one that does not exist, answers 404; a deleted file answers 410 `FILE_EXPIRED`.
   */
  downloadFile(jobId: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/api/pdf/jobs/${encodeURIComponent(jobId)}/file`, { responseType: 'blob' });
  }
}
