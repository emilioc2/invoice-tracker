import { apiFetch } from "@/api/client";
import type { Invoice } from "@/types/invoice";

// Stub — real list/filter/create/update/status-action/PDF wiring lands in step 11 with the
// Invoices pages.
export function list(): Promise<Invoice[]> {
  return apiFetch<Invoice[]>("/api/invoices");
}
