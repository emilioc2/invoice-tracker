import { apiFetch } from "@/api/client";
import type { AuthResult } from "@/types/auth";

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
