import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { useAuth } from "@/hooks/useAuth";

/** Wraps every authenticated route — redirects to `/login` when unauthenticated. */
export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { token } = useAuth();
  const location = useLocation();

  if (!token) {
    return <Navigate to="/login" replace state={{ from: location }} />;
  }

  return <>{children}</>;
}

/**
 * Separate guard from `ProtectedRoute` above (left untouched) — blocks navigation to any normal
 * app screen while `user.mustChangePassword` is still `true`, per architecture.md's "Forced
 * password change for temp-password logins" section. Nested inside `ProtectedRoute` (so it only
 * ever runs once a token is confirmed present) and wraps every existing authenticated route via
 * `App.tsx`'s `Protected` wrapper. `/change-password` itself is wrapped in `ProtectedRoute` only,
 * not this guard, so the redirect target stays reachable.
 */
export function MustChangePasswordGuard({ children }: { children: ReactNode }) {
  const { user } = useAuth();

  if (user?.mustChangePassword) {
    return <Navigate to="/change-password" replace />;
  }

  return <>{children}</>;
}

/**
 * Role-aware guard, nested the same way as `MustChangePasswordGuard` above — blocks navigation
 * to a screen whose `requiredRole` the current user's *internal* `role` doesn't match, redirecting
 * to `/dashboard` (matching `App.tsx`'s `*` catch-all's redirect target) instead of rendering the
 * screen. Checks the raw role value (e.g. `"Owner"`), never the display label ("Admin" per
 * `displayRole` in `lib/utils.ts`) — see requirements.md's Permissions section: Staff has "no user
 * management," which the Users screen itself is folded into (`GET /api/users` stays callable by
 * any authenticated user at the API layer; this is a UI-only guard).
 */
export function RequireRole({ role, children }: { role: string; children: ReactNode }) {
  const { user } = useAuth();

  if (user?.role !== role) {
    return <Navigate to="/dashboard" replace />;
  }

  return <>{children}</>;
}
