import { apiFetch } from "@/api/client";
import type { User } from "@/types/auth";

// Stub — real create/deactivate wiring lands in step 11 with the Users (team management) page.
export function list(): Promise<User[]> {
  return apiFetch<User[]>("/api/users");
}
