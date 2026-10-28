import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/http/api-base-url';
import { TemplateContent, TemplateDetail, TemplateSummary } from './template.models';

/** Typed wrapper over `/api/templates`. Stateless: caching and retries belong to callers. */
@Injectable({ providedIn: 'root' })
export class TemplatesApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${inject(API_BASE_URL)}/api/templates`;

  list(): Observable<readonly TemplateSummary[]> {
    return this.http.get<readonly TemplateSummary[]>(this.baseUrl);
  }

  get(templateKey: string): Observable<TemplateDetail> {
    return this.http.get<TemplateDetail>(this.url(templateKey));
  }

  /** Starts a draft from the server's working example. */
  create(templateKey: string): Observable<TemplateDetail> {
    return this.http.post<TemplateDetail>(this.baseUrl, { templateKey });
  }

  duplicate(sourceKey: string, templateKey: string): Observable<TemplateDetail> {
    return this.http.post<TemplateDetail>(`${this.url(sourceKey)}/duplicate`, { templateKey });
  }

  saveDraft(templateKey: string, revision: number, content: TemplateContent): Observable<TemplateDetail> {
    return this.http.put<TemplateDetail>(`${this.url(templateKey)}/draft`, { revision, content });
  }

  publish(templateKey: string, revision: number): Observable<TemplateDetail> {
    return this.http.post<TemplateDetail>(`${this.url(templateKey)}/publish`, { revision });
  }

  /** Answers 204, so the body is null. */
  delete(templateKey: string): Observable<null> {
    return this.http.delete<null>(this.url(templateKey));
  }

  /** The content bound to its sample data, as HTML. Nothing is saved and no browser is started. */
  preview(content: TemplateContent): Observable<string> {
    return this.http.post(`${this.baseUrl}/preview`, { content }, { responseType: 'text' });
  }

  private url(templateKey: string): string {
    return `${this.baseUrl}/${encodeURIComponent(templateKey)}`;
  }
}
