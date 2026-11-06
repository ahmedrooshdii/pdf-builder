import { saveBlob } from './save-blob';

describe('saveBlob', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('clicks a temporary download link with the file name, then removes it and revokes the URL', () => {
    vi.useFakeTimers();
    URL.createObjectURL = vi.fn(() => 'blob:saved');
    URL.revokeObjectURL = vi.fn();
    const clicked: { href: string; download: string }[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push({ href: this.getAttribute('href') ?? '', download: this.download });
      // While it is being clicked the link is in the document, as some browsers require.
      expect(document.body.contains(this)).toBe(true);
    });

    saveBlob(new Blob(['%PDF'], { type: 'application/pdf' }), 'invoice-j1.pdf');

    expect(clicked).toEqual([{ href: 'blob:saved', download: 'invoice-j1.pdf' }]);
    expect(document.querySelector('a[download]')).toBeNull();
    expect(URL.revokeObjectURL).not.toHaveBeenCalled();
    vi.runAllTimers();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:saved');
  });
});
