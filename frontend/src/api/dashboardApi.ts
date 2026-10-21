import { apiFetch } from "@/api/client";

// Mirrors backend `StatusCountDto` (InvoiceTracker.Application/Dashboard/Queries/GetDashboardSummary).
export interface StatusCount {
  status: string;
  count: number;
}

// Mirrors backend `DashboardSummaryDto` — note `countsByStatus` is an array of `{status, count}`
// objects, not a dictionary/record, and always has exactly 5 entries (one per `InvoiceStatus`
// value, zero-filled), so the frontend never needs to handle a missing bucket.
export interface DashboardSummary {
  outstandingTotal: number;
  overdueCount: number;
  countsByStatus: StatusCount[];
}

export function getSummary(): Promise<DashboardSummary> {
  return apiFetch<DashboardSummary>("/api/dashboard/summary");
}
