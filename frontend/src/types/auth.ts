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

// Mirrors backend `UserDto` (GET /api/users items).
export interface User {
  id: string;
  email: string;
  role: string;
  isActive: boolean;
}

// The subset of AuthResult that AuthContext keeps around as "current user" state.
export interface AuthenticatedUser {
  userId: string;
  businessId: string;
  email: string;
  role: string;
  mustChangePassword: boolean;
}
