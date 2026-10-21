import { apiFetch } from "@/api/client";
import type { AuthResult } from "@/types/auth";

// Stub signatures only — real request/response wiring (error states, loading UI) lands in
// step 11 alongside the actual login/register forms.

export function login(email: string, password: string): Promise<AuthResult> {
  return apiFetch<AuthResult>("/api/auth/login", {
    method: "POST",
    skipAuth: true,
    body: { email, password },
  });
}

export function register(
  businessName: string,
  email: string,
  password: string,
): Promise<AuthResult> {
  return apiFetch<AuthResult>("/api/auth/register", {
    method: "POST",
    skipAuth: true,
    body: { businessName, email, password },
  });
}

export function forgotPassword(email: string): Promise<void> {
  return apiFetch<void>("/api/auth/forgot-password", {
    method: "POST",
    skipAuth: true,
    body: { email },
  });
}

export function resetPassword(
  token: string,
  email: string,
  newPassword: string,
): Promise<void> {
  return apiFetch<void>("/api/auth/reset-password", {
    method: "POST",
    skipAuth: true,
    body: { token, email, newPassword },
  });
}
