import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, input, model, output } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormValueControl } from '@angular/forms/signals';
import { Router, provideRouter } from '@angular/router';
import { CodeEditor } from '../../../../shared/ui/code-editor/code-editor';
import { JobHistoryStore } from '../../data-access/job-history.store';
import { NewJobPage } from './new-job.page';

/**
 * Stands in for the CodeMirror editor, which needs real layout that jsdom does not have. It honours
 * the same form-control contract, so the page under test cannot tell the difference.
 */
@Component({
  selector: 'app-code-editor',
  template: `<textarea id="data" [value]="value()" (input)="value.set(area.value)" (blur)="touch.emit()" #area></textarea>`,
})
class FakeCodeEditor implements FormValueControl<string> {
  readonly value = model('');
  readonly touch = output();
  readonly ariaLabel = input('');
  readonly ariaDescribedBy = input<string>();
  readonly placeholder = input('');
}

function setup() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  TestBed.overrideComponent(NewJobPage, { remove: { imports: [CodeEditor] }, add: { imports: [FakeCodeEditor] } });

  const fixture = TestBed.createComponent(NewJobPage);
  TestBed.tick();

  const el: HTMLElement = fixture.nativeElement;
  const find = <T extends Element>(selector: string): T => {
    const found = el.querySelector<T>(selector);
    if (!found) throw new Error(`Nothing matches '${selector}'.`);
    return found;
  };
  const type = (selector: string, value: string) => {
    const input = find<HTMLInputElement | HTMLTextAreaElement>(selector);
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };
  const submit = () => {
    find<HTMLButtonElement>('button[type=submit]').click();
    TestBed.tick();
  };
  /**
   * Lets the submit action's promise chain finish after a response, then renders. Not whenStable():
   * that also waits for requests still in flight, which only the test can answer.
   */
  const settle = async () => {
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  };

  return { el, type, submit, settle, http: TestBed.inject(HttpTestingController) };
}

describe('NewJobPage', () => {
  it('does not send a request while the payload is not a JSON object', () => {
    const { el, type, submit, http } = setup();

    type('#data', '[1, 2]');
    submit();

    http.expectNone('/api/pdf/jobs');
    expect(el.querySelector('#data-errors')?.textContent).toContain('must be a JSON object');
  });

  it('submits, records the job and opens it', async () => {
    const { type, submit, settle, http } = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    type('#data', '{ "customer": "Acme" }');
    submit();

    const request = http.expectOne({ method: 'POST', url: '/api/pdf/jobs' });
    expect(request.request.body).toEqual({ templateKey: 'invoice', data: { customer: 'Acme' } });
    request.flush({ jobId: 'j1', statusUrl: '/api/pdf/jobs/j1' }, { status: 202, statusText: 'Accepted' });
    await settle();

    expect(navigate).toHaveBeenCalledWith(['/jobs', 'j1']);
    expect(TestBed.inject(JobHistoryStore).jobs()[0]).toMatchObject({ jobId: 'j1', status: 'Pending' });
  });

  it("shows the server's validation messages on the field they are about", async () => {
    const { el, type, submit, settle, http } = setup();

    type('#data', '{}');
    submit();

    http.expectOne('/api/pdf/jobs').flush(
      { title: 'One or more validation errors occurred.', errors: { TemplateKey: ['Rejected by the server.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(el.querySelector('#templateKey-errors')?.textContent).toContain('Rejected by the server.');
  });

  it('says so when the API cannot be reached', async () => {
    const { el, type, submit, settle, http } = setup();

    type('#data', '{}');
    submit();

    http.expectOne('/api/pdf/jobs').error(new ProgressEvent('error'), { status: 0 });
    await settle();

    expect(el.querySelector('[role=alert]')?.textContent).toContain('could not be reached');
  });
});
