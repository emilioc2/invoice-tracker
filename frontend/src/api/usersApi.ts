import { apiFetch } from "@/api/client";
import type { User } from "@/types/auth";

// Not paginated — "a business's team is expected to stay small," per the backend's own doc
// comment on `ListUsersQuery`/`UsersController`.
export function list(): Promise<User[]> {
  return apiFetch<User[]>("/api/users");
}

// Owner-only (enforced server-side via `[Authorize(Roles = "Owner")]`; the UI also hides this
// affordance from non-Owners). Response never includes the generated temp password — it's only
// ever sent via `IEmailSender`.
export function create(email: string): Promise<User> {
  return apiFetch<User>("/api/users", { method: "POST", body: { email } });
}

// Owner-only. Backend rejects deactivating yourself or any Owner with a plain `400 { title }` —
// the UI predicts both cases and hides the action before the user can even click it.
export function deactivate(id: string): Promise<void> {
  return apiFetch<void>(`/api/users/${id}`, { method: "DELETE" });
}
