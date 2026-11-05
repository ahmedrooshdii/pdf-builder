/**
 * jsdom has `<dialog>` but not `showModal()`/`close()`. This stand-in toggles the `open` attribute
 * and fires `close` the way browsers do, which is all the dialog primitives rely on. Tests only.
 */
export function polyfillDialog(): void {
  const proto = HTMLDialogElement.prototype as HTMLDialogElement & { __polyfilled?: boolean };
  if (typeof proto.showModal === 'function' && !proto.__polyfilled) return;

  proto.showModal = function (this: HTMLDialogElement) {
    this.setAttribute('open', '');
  };
  proto.close = function (this: HTMLDialogElement) {
    if (!this.hasAttribute('open')) return;
    this.removeAttribute('open');
    this.dispatchEvent(new Event('close'));
  };
  proto.__polyfilled = true;
}
