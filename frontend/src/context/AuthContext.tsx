import { useQueryClient } from "@tanstack/react-query";
import { createContext, useCallback, useEffect, useState } from "react";
import type { ReactNode } from "react";
import * as authApi from "@/api/authApi";
import { getStoredToken, registerUnauthorizedHandler, setStoredToken } from "@/api/client";
import type { AuthenticatedUser, AuthResult, LoginOutcome } from "@/types/auth";

interface AuthContextValue {
  user: AuthenticatedUser | null;
  token: string | null;
  login: (email: string, password: string) => Promise<LoginOutcome>;
  register: (
    businessName: string,
    businessEmail: string,
    ownerEmail: string,
    password: string,
  ) => Promise<void>;
  logout: () => void;
  /**
   * Clears the current user's `mustChangePassword` flag in state + localStorage, without a new
   * login — the already-issued JWT stays valid and usable (the flag is response-body/DB state,
   * never a JWT claim, per architecture.md's "Forced password change" section). Called by
   * `ChangePasswordPage` after a successful `POST /api/auth/change-password`.
   */
  clearMustChangePassword: () => void;
  /**
   * Completes a business-selection/switch-business/add-a-business transition: takes an
   * already-resolved (`requiresBusinessSelection: false`) `AuthResult` from
   * `authApi.selectBusiness()`/`switchBusiness()`/`addBusiness()` and does exactly what
   * `login()`'s own success path does — persists the token/user and calls `queryClient.clear()` —
   * so all four auth-transition points (login, logout, selectBusiness, switchBusiness/addBusiness)
   * clear the cache the same way, per architecture.md's "Auth transitions and the TanStack Query
   * cache" paragraph. `queryClient.clear()` also wipes any cached `["auth", "memberships"]` query
   * result from `useMembershipsQuery` — the switcher's dropdown re-fetches fresh from
   * `GET /api/auth/memberships` next time it's opened, so there's no separate membership-list
   * cache for this context to manage.
   */
  completeBusinessSelection: (result: AuthResult) => void;
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined);

const USER_STORAGE_KEY = "invoice-tracker.user";

function readStoredUser(): AuthenticatedUser | null {
  const raw = localStorage.getItem(USER_STORAGE_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as AuthenticatedUser;
  } catch {
    return null;
  }
}

function persistUser(user: AuthenticatedUser | null): void {
  if (user) {
    localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
  } else {
    localStorage.removeItem(USER_STORAGE_KEY);
  }
}

// Builds the "current user" slice AuthContext keeps around from a resolved (non-business-
// selection) AuthResult. Only ever called once `requiresBusinessSelection` has been confirmed
// `false`, so the non-null assertions below are safe — the backend's AuthResultDto guarantees
// every other field is populated in that case.
function toAuthenticatedUser(result: AuthResult): AuthenticatedUser {
  return {
    userId: result.userId!,
    businessId: result.businessId!,
    email: result.email!,
    role: result.role!,
    mustChangePassword: result.mustChangePassword!,
  };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  // localStorage reads are synchronous, so rehydration happens via lazy initial state —
  // no separate "isInitializing" render pass or effect-driven setState is needed.
  const [token, setToken] = useState<string | null>(() => getStoredToken());
  const [user, setUser] = useState<AuthenticatedUser | null>(() => readStoredUser());
  const queryClient = useQueryClient();

  // Multi-tenant data must never persist across a user switch in the same browser tab: every
  // query key here is unscoped by business/user, so a second business's user logging in could
  // otherwise briefly render the first business's stale cached data. `queryClient.clear()` wipes
  // the entire cache on every auth transition — both the explicit, user-initiated logout and the
  // automatic one triggered by `api/client.ts`'s 401 handler (registered below), as well as a
  // fresh login/register completion (which may follow a previous session on the same tab).
  const logout = useCallback(() => {
    queryClient.clear();
    setStoredToken(null);
    persistUser(null);
    setToken(null);
    setUser(null);
  }, [queryClient]);

  // Register this tab's logout as the handler `api/client.ts` calls whenever any request
  // comes back 401. A side effect (no setState), so this is a pure useEffect use case.
  useEffect(() => {
    registerUnauthorizedHandler(logout);
    return () => registerUnauthorizedHandler(null);
  }, [logout]);

  // Shared by login's resolved branch, register, and the exported `completeBusinessSelection` —
  // persists the token/user and wipes any stale cached data left over from a previous session on
  // this tab.
  const persistResolvedAuth = useCallback(
    (result: AuthResult) => {
      const authenticatedUser = toAuthenticatedUser(result);
      setStoredToken(result.accessToken);
      persistUser(authenticatedUser);
      setToken(result.accessToken);
      setUser(authenticatedUser);
      queryClient.clear();
    },
    [queryClient],
  );

  const login = useCallback(
    async (email: string, password: string): Promise<LoginOutcome> => {
      const result = await authApi.login(email, password);

      if (result.requiresBusinessSelection) {
        // No token/user persisted yet — the caller (LoginPage) must redirect to
        // SelectBusinessPage with this preauth token + membership list in hand.
        return {
          requiresBusinessSelection: true,
          preAuthToken: result.preAuthToken!,
          memberships: result.memberships ?? [],
        };
      }

      persistResolvedAuth(result);
      return { requiresBusinessSelection: false };
    },
    [persistResolvedAuth],
  );

  // Mirrors `login`'s logic exactly: the backend's `AuthResultDto` is the shared success shape
  // for both register and login — register logs the new Owner in immediately with a usable JWT,
  // so there's no separate "register then separately log in" step. Register can never return
  // `requiresBusinessSelection: true` (it only ever creates a brand-new identity with exactly one
  // membership), so no branching is needed here.
  const register = useCallback(
    async (businessName: string, businessEmail: string, ownerEmail: string, password: string) => {
      const result = await authApi.register(businessName, businessEmail, ownerEmail, password);
      persistResolvedAuth(result);
    },
    [persistResolvedAuth],
  );

  const clearMustChangePassword = useCallback(() => {
    setUser((current) => {
      if (!current) return current;
      const updated: AuthenticatedUser = { ...current, mustChangePassword: false };
      persistUser(updated);
      return updated;
    });
  }, []);

  const value: AuthContextValue = {
    user,
    token,
    login,
    register,
    logout,
    clearMustChangePassword,
    // Same persist-token/user-plus-clear-cache logic as login's resolved branch — exposed
    // directly rather than wrapped, since there's nothing else for this transition to do.
    completeBusinessSelection: persistResolvedAuth,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
