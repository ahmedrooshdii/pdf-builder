/**
 * jsdom treats a `popover` element as closed (so nothing inside it can take focus) but has no
 * `showPopover()`/`hidePopover()` to open it. This stand-in opens one by setting the attribute aside
 * and closes it by putting it back, which is enough for focus and keyboard tests. Tests only.
 */
export function polyfillPopover(): void {
  const proto = HTMLElement.prototype as HTMLElement & { __popoverPolyfilled?: boolean };
  if (typeof proto.showPopover === 'function' && !proto.__popoverPolyfilled) return;

  proto.showPopover = function (this: HTMLElement) {
    this.dataset['popover'] = this.getAttribute('popover') ?? 'auto';
    this.removeAttribute('popover');
  };
  proto.hidePopover = function (this: HTMLElement) {
    this.setAttribute('popover', this.dataset['popover'] ?? 'auto');
  };
  proto.__popoverPolyfilled = true;
}
