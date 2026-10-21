import { apiFetch } from "@/api/client";
import type { Client } from "@/types/client";

// Stub — real list/filter/create/update/archive wiring lands in step 11 with the Clients pages.
export function list(): Promise<Client[]> {
  return apiFetch<Client[]>("/api/clients");
}
