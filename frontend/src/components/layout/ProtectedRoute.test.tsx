import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { ProtectedRoute } from "@/components/layout/ProtectedRoute";
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
