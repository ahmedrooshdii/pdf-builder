/**
 * Hands a blob to the browser as a download named `fileName`: a temporary object URL on a temporary
 * `<a download>`, clicked, then revoked. Used where the file has to be fetched with the console's own
 * credentials, which a plain link would not carry.
 */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.style.display = 'none';
  document.body.appendChild(link);
  try {
    link.click();
  } finally {
    link.remove();
    // Not at once: some browsers start the download after the click handler returns.
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }
}
