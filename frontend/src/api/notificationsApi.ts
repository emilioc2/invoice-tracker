import { apiFetch } from "@/api/client";
import type { Notification } from "@/types/notification";

// Stub — real wiring lands in step 11 with the Notifications page.
export function list(invoiceId?: string): Promise<Notification[]> {
  const query = invoiceId ? `?invoiceId=${encodeURIComponent(invoiceId)}` : "";
  return apiFetch<Notification[]>(`/api/notifications${query}`);
}
