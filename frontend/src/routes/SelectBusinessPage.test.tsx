import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AuthProvider } from "@/context/AuthContext";
import { SelectBusinessPage } from "@/routes/SelectBusinessPage";
import { server } from "@/test/server";
import { clearSession, createTestQueryClient } from "@/test/test-utils";
import type { AuthResult, MembershipOption } from "@/types/auth";

// Covers architecture.md's "Business-selector and switcher UI" paragraph: SelectBusinessPage is
// reached only via LoginPage's redirect (router state carrying preAuthToken + memberships, never
// persisted to context/localStorage) — a direct landing with no state must bounce back to /login,
// and selecting a membership must complete the login exactly like a normal login() success.

const MEMBERSHIPS: MembershipOption[] = [
  { businessId: "business-a", businessName: "Business A", role: "Owner" },
  { businessId: "business-b", businessName: "Business B", role: "Staff" },
];

const SELECT_RESULT: AuthResult = {
  accessToken: "resolved-access-token",
  expiresAtUtc: "2026-01-01T00:00:00Z",
  userId: "user-1",
  businessId: "business-b",
  email: "owner@example.com",
  role: "Staff",
  mustChangePassword: false,
  requiresBusinessSelection: false,
  preAuthToken: null,
  memberships: null,
};

function renderAt(initialEntries: Parameters<typeof MemoryRouter>[0]["initialEntries"]) {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={initialEntries}>
          <Routes>
            <Route path="/select-business" element={<SelectBusinessPage />} />
            <Route path="/login" element={<div>Login page</div>} />
            <Route path="/dashboard" element={<div>Dashboard page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe("SelectBusinessPage", () => {
  beforeEach(() => {
    clearSession();
  });

  it("redirects to /login when router state is missing (direct landing, refresh, back button)", () => {
    renderAt(["/select-business"]);

    expect(screen.getByText("Login page")).toBeInTheDocument();
    expect(screen.queryByText("Choose a business")).not.toBeInTheDocument();
  });

  it("renders the membership list from router state", () => {
    renderAt([{ pathname: "/select-business", state: { preAuthToken: "preauth-token", memberships: MEMBERSHIPS } }]);

    expect(screen.getByText("Business A")).toBeInTheDocument();
    expect(screen.getByText("Business B")).toBeInTheDocument();
    // Display-only: "Owner" is relabeled "Admin" in the UI (see `displayRole` in lib/utils.ts) —
    // the raw role string should never leak through here.
    expect(screen.getByText("Admin")).toBeInTheDocument();
    expect(screen.getByText("Staff")).toBeInTheDocument();
  });

  it("selecting a membership calls selectBusiness and completes auth, navigating to /dashboard", async () => {
    let capturedBody: unknown;
    server.use(
      http.post("*/api/auth/select-business", async ({ request }) => {
        capturedBody = await request.json();
        return HttpResponse.json(SELECT_RESULT);
      }),
    );

    renderAt([{ pathname: "/select-business", state: { preAuthToken: "preauth-token", memberships: MEMBERSHIPS } }]);

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /Business B/i }));

    await waitFor(() => expect(screen.getByText("Dashboard page")).toBeInTheDocument());

    expect(capturedBody).toEqual({ preAuthToken: "preauth-token", businessId: "business-b" });
    expect(localStorage.getItem("invoice-tracker.accessToken")).toBe(SELECT_RESULT.accessToken);
  });
});
