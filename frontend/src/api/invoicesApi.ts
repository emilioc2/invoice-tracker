import { apiFetch, apiFetchBlob } from "@/api/client";
import { dateOnlyToUtcIso } from "@/lib/utils";
import { downloadBlob } from "@/lib/downloadBlob";
import type {
  Invoice,
  InvoiceInput,
  InvoiceStatus,
  ResendInvoiceEmailResult,
  SendInvoiceResult,
} from "@/types/invoice";
import type { PaginatedResult } from "@/types/common";

export interface InvoiceListParams {
  status?: InvoiceStatus;
  clientId?: string;
  /** Date-only string from a `<input type="date">` (e.g. `2026-01-01`). */
  dateFrom?: string;
  /** Date-only string from a `<input type="date">` (e.g. `2026-01-31`). */
  dateTo?: string;
  pageNumber?: number;
  pageSize?: number;
}

// Same "omit empty/undefined filters entirely" convention as `clientsApi.ts` — a clean query
// string, and a TanStack Query key that doesn't churn on filters that aren't actually set.
function buildListQuery(params: InvoiceListParams): string {
  const query = new URLSearchParams();
  if (params.status) {
    query.set("status", params.status);
  }
  if (params.clientId) {
    query.set("clientId", params.clientId);
  }
  if (params.dateFrom) {
    // See `dateOnlyToUtcIso`'s doc comment: a bare date-only string 500s against the backend's
    // `timestamp with time zone` columns, so this always sends a full UTC instant instead.
    query.set("dateFrom", dateOnlyToUtcIso(params.dateFrom));
  }
  if (params.dateTo) {
    query.set("dateTo", dateOnlyToUtcIso(params.dateTo));
  }
  if (params.pageNumber !== undefined) {
    query.set("pageNumber", String(params.pageNumber));
  }
  if (params.pageSize !== undefined) {
    query.set("pageSize", String(params.pageSize));
  }
  const queryString = query.toString();
  return queryString ? `?${queryString}` : "";
}

export function list(params: InvoiceListParams = {}): Promise<PaginatedResult<Invoice>> {
  return apiFetch<PaginatedResult<Invoice>>(`/api/invoices${buildListQuery(params)}`);
}

export function getById(id: string): Promise<Invoice> {
  return apiFetch<Invoice>(`/api/invoices/${id}`);
}

export function create(input: InvoiceInput): Promise<Invoice> {
  return apiFetch<Invoice>("/api/invoices", { method: "POST", body: input });
}

export function update(id: string, input: InvoiceInput): Promise<Invoice> {
  return apiFetch<Invoice>(`/api/invoices/${id}`, { method: "PUT", body: input });
}

export function send(id: string): Promise<SendInvoiceResult> {
  return apiFetch<SendInvoiceResult>(`/api/invoices/${id}/send`, { method: "POST" });
}

// Valid for any invoice status except Draft (400 otherwise) — re-delivers the same PDF+details
// email without transitioning status or touching the Draft-only edit lock.
export function resendEmail(id: string): Promise<ResendInvoiceEmailResult> {
  return apiFetch<ResendInvoiceEmailResult>(`/api/invoices/${id}/resend-email`, {
    method: "POST",
  });
}

export function markPaid(id: string): Promise<Invoice> {
  return apiFetch<Invoice>(`/api/invoices/${id}/mark-paid`, { method: "POST" });
}

export function cancel(id: string): Promise<Invoice> {
  return apiFetch<Invoice>(`/api/invoices/${id}/cancel`, { method: "POST" });
}

// Soft-delete/archive action, same "Archive," never "Delete" semantics as `clientsApi.ts`.
export function archive(id: string): Promise<void> {
  return apiFetch<void>(`/api/invoices/${id}`, { method: "DELETE" });
}

/**
 * Downloads the invoice's PDF and triggers a browser save as `${invoiceNumber}.pdf`. Named via
 * the invoice's own `invoiceNumber` (already known client-side) rather than the response's
 * `Content-Disposition` header — the current CORS policy doesn't expose that header to browser
 * JS cross-origin, so reading it here wouldn't work even though the server sets it.
 */
export async function downloadPdf(id: string, invoiceNumber: string): Promise<void> {
  const blob = await apiFetchBlob(`/api/invoices/${id}/pdf`);
  downloadBlob(blob, `${invoiceNumber}.pdf`);
}
