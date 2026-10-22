import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { apiFetch } from "@/api/client";
import { AuthProvider } from "@/context/AuthContext";
import { useAuth } from "@/hooks/useAuth";
import { server } from "@/test/server";
import { clearSession, seedAuthenticatedSession } from "@/test/test-utils";
import type { AuthResult } from "@/types/auth";

// These tests exercise the cross-tenant data-bleed fix described in architecture.md's "Auth
// transitions and the TanStack Query cache" paragraph: every auth transition (explicit logout,
// the automatic 401-triggered logout, and a fresh login) must call `queryClient.clear()` so a
// second business's user logging into the same tab never renders a first business's stale
// cached invoices/clients/dashboard data. Retries are disabled directly on the QueryClient
// instances below (rather than via `createTestQueryClient()`) because these tests need a handle
// on the exact client instance passed to `QueryClientProvider` to assert on its cache afterward.

function newQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
}

function LogoutButton() {
  const { logout } = useAuth();
  return <button onClick={logout}>Log out</button>;
}

function LoginButton({ email, password }: { email: string; password: string }) {
  const { login } = useAuth();
  return <button onClick={() => login(email, password)}>Log in</button>;
}

const LOGIN_RESULT: AuthResult = {
  accessToken: "new-session-token",
  expiresAtUtc: "2026-01-01T00:00:00Z",
  userId: "user-2",
  businessId: "business-2",
  email: "other-business-owner@example.com",
  role: "Owner",
  mustChangePassword: false,
  requiresBusinessSelection: false,
  preAuthToken: null,
  memberships: null,
};

describe("AuthContext query cache isolation across auth transitions", () => {
  beforeEach(() => {
    clearSession();
  });

  it("logout() clears the entire TanStack Query cache (explicit, user-initiated logout)", async () => {
    seedAuthenticatedSession();
    const queryClient = newQueryClient();
    queryClient.setQueryData(["invoices", {}], { items: [{ id: "biz-a-invoice" }] });
    queryClient.setQueryData(["clients", {}], { items: [{ id: "biz-a-client" }] });
    queryClient.setQueryData(["dashboard", "summary"], {
      outstandingTotal: 999,
      overdueCount: 3,
      countsByStatus: [],
    });
    expect(queryClient.getQueryCache().getAll().length).toBeGreaterThan(0);

    render(
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <LogoutButton />
        </AuthProvider>
      </QueryClientProvider>,
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /log out/i }));

    expect(queryClient.getQueryCache().getAll()).toHaveLength(0);
    expect(localStorage.getItem("invoice-tracker.accessToken")).toBeNull();
    expect(localStorage.getItem("invoice-tracker.user")).toBeNull();
  });

  it("the automatic 401-triggered logout (api/client.ts's unauthorizedHandler) routes through the same logout() and also clears the cache", async () => {
    seedAuthenticatedSession();
    const queryClient = newQueryClient();
    queryClient.setQueryData(["invoices", {}], { items: [{ id: "biz-a-invoice" }] });
    queryClient.setQueryData(["dashboard", "summary"], {
      outstandingTotal: 500,
      overdueCount: 1,
      countsByStatus: [],
    });
    expect(queryClient.getQueryCache().getAll().length).toBeGreaterThan(0);

    server.use(
      http.get("*/api/some-protected-resource", () =>
        HttpResponse.json({ message: "Unauthorized" }, { status: 401 }),
      ),
    );

    render(
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <div>App shell</div>
        </AuthProvider>
      </QueryClientProvider>,
    );

    // By the time AuthProvider has mounted, its effect has registered its own `logout` as
    // `api/client.ts`'s module-level `unauthorizedHandler`. Calling `apiFetch` directly here
    // (rather than going through some app component) proves that *this exact* registration is
    // what fires on a 401 — not a separate/parallel logout path that would bypass the cache clear.
    await expect(apiFetch("/api/some-protected-resource")).rejects.toThrow();

    await waitFor(() => expect(queryClient.getQueryCache().getAll()).toHaveLength(0));
    expect(localStorage.getItem("invoice-tracker.accessToken")).toBeNull();
  });

  it("login() clears any stale cache left over from a previous session on this tab", async () => {
    // Starts logged out (no stale token), but the query cache already holds data left behind by
    // a previous session on this tab/browser — the exact scenario the fix targets.
    const queryClient = newQueryClient();
    queryClient.setQueryData(["invoices", {}], { items: [{ id: "biz-a-invoice" }] });
    queryClient.setQueryData(["dashboard", "summary"], {
      outstandingTotal: 777,
      overdueCount: 2,
      countsByStatus: [],
    });
    expect(queryClient.getQueryCache().getAll().length).toBeGreaterThan(0);

    server.use(http.post("*/api/auth/login", () => HttpResponse.json(LOGIN_RESULT)));

    render(
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <LoginButton email={LOGIN_RESULT.email} password="password123" />
        </AuthProvider>
      </QueryClientProvider>,
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /log in/i }));

    await waitFor(() => expect(queryClient.getQueryCache().getAll()).toHaveLength(0));
    expect(localStorage.getItem("invoice-tracker.accessToken")).toBe(LOGIN_RESULT.accessToken);
  });
});
