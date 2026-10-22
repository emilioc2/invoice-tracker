import { apiFetch } from "@/api/client";
import type { Client, ClientInput } from "@/types/client";
import type { PaginatedResult } from "@/types/common";

export interface ClientListParams {
  search?: string;
  pageNumber?: number;
  pageSize?: number;
}

// Builds the query string carefully: an empty/undefined `search` is omitted entirely rather
// than sent as `search=` (the backend treats both the same, but a clean query string is easier
// to read in the network tab and in the TanStack Query key).
function buildListQuery(params: ClientListParams): string {
  const query = new URLSearchParams();
  if (params.search) {
    query.set("search", params.search);
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

export function list(params: ClientListParams = {}): Promise<PaginatedResult<Client>> {
  return apiFetch<PaginatedResult<Client>>(`/api/clients${buildListQuery(params)}`);
}

export function getById(id: string): Promise<Client> {
  return apiFetch<Client>(`/api/clients/${id}`);
}

export function create(input: ClientInput): Promise<Client> {
  return apiFetch<Client>("/api/clients", { method: "POST", body: input });
}

export function update(id: string, input: ClientInput): Promise<Client> {
  return apiFetch<Client>(`/api/clients/${id}`, { method: "PUT", body: input });
}

// This is the status-transition action (Active -> Removed via the backend's guarded
// `Client.TransitionTo`, idempotent at the HTTP layer) — surfaced in the UI only as "Remove",
// never "Delete"/"Archive", matching the backend's own `RemoveClientCommand` naming. One-way
// only: once Removed, there's no restore capability, so no `reactivate` counterpart exists here.
export function remove(id: string): Promise<void> {
  return apiFetch<void>(`/api/clients/${id}`, { method: "DELETE" });
}
