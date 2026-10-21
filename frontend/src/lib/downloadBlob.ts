/**
 * Triggers a browser "Save as" for an in-memory `Blob` via a temporary object URL + hidden
 * `<a download>` click, then revokes the URL. Shared by any API module that downloads binary
 * content (currently just the invoice PDF export) so the DOM-manipulation bit lives in one place
 * instead of being duplicated in a route component.
 */
export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
