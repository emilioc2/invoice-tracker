import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { MustChangePasswordGuard, ProtectedRoute, RequireRole } from "@/components/layout/ProtectedRoute";
import { AuthProvider } from "@/context/AuthContext";
import { clearSession, createTestQueryClient, seedAuthenticatedSession } from "@/test/test-utils";

function renderProtectedRouteApp() {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={["/dashboard"]}>
          <Routes>
            <Route
              path="/dashboard"
              element={
                <ProtectedRoute>
                  <div>Secret dashboard content</div>
                </ProtectedRoute>
              }
            />
            <Route path="/login" element={<div>Login page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe("ProtectedRoute", () => {
  beforeEach(() => {
    // AuthContext reads the token synchronously from localStorage on mount (lazy initial
    // state) — clear it explicitly rather than trusting whatever a previous test left behind.
    clearSession();
  });

  it("redirects to /login and does not render protected content when unauthenticated", () => {
    renderProtectedRouteApp();

    expect(screen.getByText("Login page")).toBeInTheDocument();
    expect(screen.queryByText("Secret dashboard content")).not.toBeInTheDocument();
  });

  it("renders the protected content (no redirect) when a session is present", () => {
    seedAuthenticatedSession();

    renderProtectedRouteApp();

    expect(screen.getByText("Secret dashboard content")).toBeInTheDocument();
    expect(screen.queryByText("Login page")).not.toBeInTheDocument();
  });
});

// Exercises architecture.md's "Forced password-change route guard" paragraph: the inverse of
// ProtectedRoute above — it keeps an authenticated-but-not-yet-compliant user out of every normal
// app screen until `mustChangePassword` clears, by redirecting to /change-password.
function renderGuardedApp() {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={["/dashboard"]}>
          <Routes>
            <Route
              path="/dashboard"
              element={
                <MustChangePasswordGuard>
                  <div>Dashboard content</div>
                </MustChangePasswordGuard>
              }
            />
            <Route path="/change-password" element={<div>Change password page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe("MustChangePasswordGuard", () => {
  beforeEach(() => {
    clearSession();
  });

  it("redirects to /change-password when the user's mustChangePassword flag is true", () => {
    seedAuthenticatedSession({
      userId: "user-1",
      businessId: "business-1",
      email: "staff@example.com",
      role: "Staff",
      mustChangePassword: true,
    });

    renderGuardedApp();

    expect(screen.getByText("Change password page")).toBeInTheDocument();
    expect(screen.queryByText("Dashboard content")).not.toBeInTheDocument();
  });

  it("does not redirect when the user's mustChangePassword flag is false", () => {
    seedAuthenticatedSession({
      userId: "user-1",
      businessId: "business-1",
      email: "owner@example.com",
      role: "Owner",
      mustChangePassword: false,
    });

    renderGuardedApp();

    expect(screen.getByText("Dashboard content")).toBeInTheDocument();
    expect(screen.queryByText("Change password page")).not.toBeInTheDocument();
  });
});

// Covers user_testing_defects.md item #16: a Staff member navigating directly to a
// role-restricted route (e.g. typing /users, an old bookmark) must be redirected, not shown the
// page — the nav-link hide in AppShell alone only stops discovery, not direct access. Mirrors
// `requirements.md`'s Permissions section (Staff: "no user management," folded into the Users
// screen itself) and checks the internal role value, never the "Admin" display label (item #2).
function renderRoleGuardedApp() {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={["/users"]}>
          <Routes>
            <Route
              path="/users"
              element={
                <RequireRole role="Owner">
                  <div>Users page content</div>
                </RequireRole>
              }
            />
            <Route path="/dashboard" element={<div>Dashboard page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe("RequireRole", () => {
  beforeEach(() => {
    clearSession();
  });

  it("redirects a Staff user away from an Owner-only route instead of rendering it", () => {
    seedAuthenticatedSession({
      userId: "user-1",
      businessId: "business-1",
      email: "staff@example.com",
      role: "Staff",
      mustChangePassword: false,
    });

    renderRoleGuardedApp();

    expect(screen.getByText("Dashboard page")).toBeInTheDocument();
    expect(screen.queryByText("Users page content")).not.toBeInTheDocument();
  });

  it("renders the route for an Owner user (no redirect)", () => {
    seedAuthenticatedSession({
      userId: "user-1",
      businessId: "business-1",
      email: "owner@example.com",
      role: "Owner",
      mustChangePassword: false,
    });

    renderRoleGuardedApp();

    expect(screen.getByText("Users page content")).toBeInTheDocument();
    expect(screen.queryByText("Dashboard page")).not.toBeInTheDocument();
  });
});
