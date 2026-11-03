import { DEFAULT_PAGE_OPTIONS, PageOptions } from '../../../shared/data-access/template.models';
import { pageSetupBetween } from './pdf-job.models';

describe('pageSetupBetween', () => {
  const template: PageOptions = { ...DEFAULT_PAGE_OPTIONS, margins: { top: '0mm', right: '0mm', bottom: '0mm', left: '0mm' } };

  it('sends nothing when nothing changed', () => {
    expect(pageSetupBetween(template, { ...template })).toBeUndefined();
  });

  it('sends only what changed', () => {
    expect(pageSetupBetween(template, { ...template, landscape: true })).toEqual({ landscape: true });
  });

  it('sends a custom size as width and height, without the format', () => {
    expect(pageSetupBetween(template, { ...template, width: '80mm', height: '200mm' })).toEqual({
      width: '80mm',
      height: '200mm',
    });
  });

  it('sends an auto height as it is', () => {
    expect(pageSetupBetween(template, { ...template, width: '80mm', height: 'auto' })).toEqual({
      width: '80mm',
      height: 'auto',
    });
  });

  it('sends the format when going back from a custom size to the same named one', () => {
    const receipt = { ...template, width: '80mm', height: '200mm' };
    expect(pageSetupBetween(receipt, { ...receipt, width: null, height: null })).toEqual({ format: 'A4' });
  });

  it('sends only the margin sides that changed', () => {
    const edited = { ...template, margins: { ...template.margins, top: '15mm' } };
    expect(pageSetupBetween(template, edited)).toEqual({ margins: { top: '15mm' } });
  });
});
