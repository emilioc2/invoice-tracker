import { createContext, useCallback, useEffect, useState } from "react";
import type { ReactNode } from "react";
import * as authApi from "@/api/authApi";
import { getStoredToken, registerUnauthorizedHandler, setStoredToken } from "@/api/client";
import type { AuthenticatedUser } from "@/types/auth";

interface AuthContextValue {
  user: AuthenticatedUser | null;
  token: string | null;
  login: (email: string, password: string) => Promise<void>;
  register: (
    businessName: string,
    businessEmail: string,
    ownerEmail: string,
    password: string,
  ) => Promise<void>;
  logout: () => void;
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

export function AuthProvider({ children }: { children: ReactNode }) {
  // localStorage reads are synchronous, so rehydration happens via lazy initial state —
  // no separate "isInitializing" render pass or effect-driven setState is needed.
  const [token, setToken] = useState<string | null>(() => getStoredToken());
  const [user, setUser] = useState<AuthenticatedUser | null>(() => readStoredUser());

  const logout = useCallback(() => {
    setStoredToken(null);
    persistUser(null);
    setToken(null);
    setUser(null);
  }, []);

  // Register this tab's logout as the handler `api/client.ts` calls whenever any request
  // comes back 401. A side effect (no setState), so this is a pure useEffect use case.
  useEffect(() => {
    registerUnauthorizedHandler(logout);
    return () => registerUnauthorizedHandler(null);
  }, [logout]);

  const login = useCallback(async (email: string, password: string) => {
    const result = await authApi.login(email, password);
    const authenticatedUser: AuthenticatedUser = {
      userId: result.userId,
      businessId: result.businessId,
      email: result.email,
      role: result.role,
    };
    setStoredToken(result.accessToken);
    persistUser(authenticatedUser);
    setToken(result.accessToken);
    setUser(authenticatedUser);
  }, []);

  // Mirrors `login`'s logic exactly: the backend's `AuthResultDto` is the shared success shape
  // for both register and login — register logs the new Owner in immediately with a usable JWT,
  // so there's no separate "register then separately log in" step.
  const register = useCallback(
    async (businessName: string, businessEmail: string, ownerEmail: string, password: string) => {
      const result = await authApi.register(businessName, businessEmail, ownerEmail, password);
      const authenticatedUser: AuthenticatedUser = {
        userId: result.userId,
        businessId: result.businessId,
        email: result.email,
        role: result.role,
      };
      setStoredToken(result.accessToken);
      persistUser(authenticatedUser);
      setToken(result.accessToken);
      setUser(authenticatedUser);
    },
    [],
  );

  const value: AuthContextValue = {
    user,
    token,
    login,
    register,
    logout,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
