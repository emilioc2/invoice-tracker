// Mirrors backend `AuthResultDto` (InvoiceTracker.Application/Auth) — System.Text.Json default
// camelCase serialization applies, no custom naming policy configured in Program.cs.
//
// Multi-business membership (see requirements.md/architecture.md): every field except
// `requiresBusinessSelection` is nullable now, because the same DTO shape is reused for two
// different outcomes of `POST /api/auth/login`:
//  - the common, single-membership case: `requiresBusinessSelection: false`, every other field
//    populated exactly as before (zero visible change for this case).
//  - the multi-membership case: `requiresBusinessSelection: true`, the token-bearing fields are
//    all `null`, and `preAuthToken`/`memberships` are populated instead.
// `select-business`/`switch-business`/`businesses` ("Add a business") always return the first
// (resolved) shape.
export interface AuthResult {
  accessToken: string | null;
  expiresAtUtc: string | null;
  userId: string | null;
  businessId: string | null;
  email: string | null;
  role: string | null;
  mustChangePassword: boolean | null;
  requiresBusinessSelection: boolean;
  preAuthToken: string | null;
  memberships: MembershipOption[] | null;
}

// Mirrors backend `MembershipOptionDto` — one selectable option in a multi-membership login's
// business list, and the same shape reused client-side for the "switch business"/"add a
// business" affordance's cached list (see AuthContext's `knownMemberships`).
export interface MembershipOption {
  businessId: string;
  businessName: string;
  role: string;
}

// The subset of AuthResult's fields that are always present once `requiresBusinessSelection` is
// `false` — used internally to avoid repeating non-null assertions across every call site that
// persists a resolved login/register/select-business/switch-business/add-business result.
export interface ResolvedAuthResult {
  accessToken: string;
  expiresAtUtc: string;
  userId: string;
  businessId: string;
  email: string;
  role: string;
  mustChangePassword: boolean;
}

// What `AuthContext.login()` resolves to — a discriminated union instead of a bare `Promise<void>`
// so `LoginPage` can branch between "log in succeeded, go to the app" and "pick a business first."
export type LoginOutcome =
  | { requiresBusinessSelection: false }
  | { requiresBusinessSelection: true; preAuthToken: string; memberships: MembershipOption[] };

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
