import { apiFetch } from "@/api/client";
import type { Notification } from "@/types/notification";
import type { PaginatedResult } from "@/types/common";

export interface NotificationListParams {
  invoiceId?: string;
  pageNumber?: number;
  pageSize?: number;
}

// Same "omit empty/undefined filters entirely" convention as `clientsApi.ts`/`invoicesApi.ts`.
function buildListQuery(params: NotificationListParams): string {
  const query = new URLSearchParams();
  if (params.invoiceId) {
    query.set("invoiceId", params.invoiceId);
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

// Server sorts most-recent-first already — nothing to do client-side there.
export function list(params: NotificationListParams = {}): Promise<PaginatedResult<Notification>> {
  return apiFetch<PaginatedResult<Notification>>(`/api/notifications${buildListQuery(params)}`);
}
