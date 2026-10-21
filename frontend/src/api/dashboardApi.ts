import { apiFetch } from "@/api/client";

// Placeholder shape — real DTO fields confirmed against the backend in step 11.
export interface DashboardSummary {
  outstandingTotal: number;
  overdueCount: number;
  countsByStatus: Record<string, number>;
}

// Stub — real wiring lands in step 11 with the Dashboard page.
export function getSummary(): Promise<DashboardSummary> {
  return apiFetch<DashboardSummary>("/api/dashboard/summary");
}
