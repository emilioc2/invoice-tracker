// Mirrors backend `AuthResultDto` (InvoiceTracker.Application/Auth) — System.Text.Json default
// camelCase serialization applies, no custom naming policy configured in Program.cs.
export interface AuthResult {
  accessToken: string;
  expiresAtUtc: string;
  userId: string;
  businessId: string;
  email: string;
  role: string;
  mustChangePassword: boolean;
}

// `status`/`statusChangedAt` replace the former `isActive` boolean (see architecture.md's
// "Domain invariants: centralized status transitions") — `UserStatus` is
// `Active | Deactivated`, set via the backend's guarded `User.TransitionTo`. Unlike Client, both
// directions are legal: Active -> Deactivated (deactivate) and Deactivated -> Active (reactivate).
export type UserStatus = "Active" | "Deactivated";

// Mirrors backend `UserDto` (GET /api/users items).
export interface User {
  id: string;
  email: string;
  role: string;
  status: UserStatus;
  statusChangedAt: string;
}

// The subset of AuthResult that AuthContext keeps around as "current user" state.
export interface AuthenticatedUser {
  userId: string;
  businessId: string;
  email: string;
  role: string;
  mustChangePassword: boolean;
}
