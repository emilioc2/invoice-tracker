import { apiFetch } from "@/api/client";
import type { AuthResult, MembershipOption } from "@/types/auth";

export function login(email: string, password: string): Promise<AuthResult> {
  return apiFetch<AuthResult>("/api/auth/login", {
    method: "POST",
    skipAuth: true,
    body: { email, password },
  });
}

// Mirrors the backend's `RegisterRequest`/`RegisterCommand`: `businessEmail` (the business's own
// contact email) and `ownerEmail` (the new Owner user's login identity) are two separate fields,
// not one shared email.
export function register(
  businessName: string,
  businessEmail: string,
  ownerEmail: string,
  password: string,
): Promise<AuthResult> {
  return apiFetch<AuthResult>("/api/auth/register", {
    method: "POST",
    skipAuth: true,
    body: { businessName, businessEmail, ownerEmail, password },
  });
}

// Completes a login that returned `requiresBusinessSelection: true`. `skipAuth: true` like
// `login`/`register` above — this call carries no normal bearer token at all (no JWT has been
// issued yet); the preauth token is a request-body field, not an Authorization header, so the
// backend performs its own signature/expiry validation on it (see AuthController's doc comment).
export function selectBusiness(preAuthToken: string, businessId: string): Promise<AuthResult> {
  return apiFetch<AuthResult>("/api/auth/select-business", {
    method: "POST",
    skipAuth: true,
    body: { preAuthToken, businessId },
  });
}

// Switches the caller's active session to a different one of their own active memberships —
// requires a valid JWT (normal authenticated call, no `skipAuth`), matching `changePassword`'s
// pattern below. The backend re-validates the caller actually holds an active membership in
// `businessId` server-side (404 otherwise) before issuing a new token scoped to it.
export function switchBusiness(businessId: string): Promise<AuthResult> {
  return apiFetch<AuthResult>("/api/auth/switch-business", {
    method: "POST",
    body: { businessId },
  });
}

// Lists the caller's own active memberships (other than, or including, whichever business the
// current token is scoped to) — backs AppShell's "Switch business" affordance, fetched lazily
// only when that dropdown is opened (see `hooks/useMemberships.ts`), rather than eagerly on every
// page load. Same `MembershipOptionDto` shape as login's multi-membership response. Requires a
// valid JWT (normal authenticated call, no `skipAuth`).
export function listMyMemberships(): Promise<MembershipOption[]> {
  return apiFetch<MembershipOption[]>("/api/auth/memberships");
}

// "Add a business" — requires a valid JWT. Unlike the other three AuthResult-returning calls
// above, the backend's `AddBusinessCommand` does take a body (`BusinessName`/`BusinessEmail`,
// same shape as `register`'s business half) — it creates a brand-new Business, not just a
// membership in an existing one, so it needs a name/contact email like registration does.
export function addBusiness(businessName: string, businessEmail: string): Promise<AuthResult> {
  return apiFetch<AuthResult>("/api/auth/businesses", {
    method: "POST",
    body: { businessName, businessEmail },
  });
}

export function forgotPassword(email: string): Promise<{ message: string }> {
  return apiFetch<{ message: string }>("/api/auth/forgot-password", {
    method: "POST",
    skipAuth: true,
    body: { email },
  });
}

export function resetPassword(
  email: string,
  token: string,
  newPassword: string,
): Promise<{ message: string }> {
  return apiFetch<{ message: string }>("/api/auth/reset-password", {
    method: "POST",
    skipAuth: true,
    body: { email, token, newPassword },
  });
}

// Mid-session forced/self-initiated password change — distinct from reset-password's reset-token
// flow. Requires a valid JWT (no `skipAuth`), so the base wrapper attaches the current session's
// Authorization header. Returns `204 No Content` on success, which `apiFetch` resolves to
// `undefined`, same pattern as `invoicesApi.ts`/`clientsApi.ts`'s `archive()`.
export function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  return apiFetch<void>("/api/auth/change-password", {
    method: "POST",
    body: { currentPassword, newPassword },
  });
}
