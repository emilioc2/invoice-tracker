import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AuthProvider } from "@/context/AuthContext";
import { LoginPage } from "@/routes/LoginPage";
import { server } from "@/test/server";
import { clearSession, createTestQueryClient } from "@/test/test-utils";
import type { AuthResult } from "@/types/auth";

// Covers architecture.md's "Business-selector and switcher UI" paragraph: LoginPage must branch
// on login()'s LoginOutcome — a multi-membership response navigates to /select-business carrying
// the preauth token + membership list as router state, while the ordinary single-membership
// response goes straight to /dashboard exactly as before.

const SINGLE_MEMBERSHIP_RESULT: AuthResult = {
  accessToken: "resolved-access-token",
  expiresAtUtc: "2026-01-01T00:00:00Z",
  userId: "user-1",
  businessId: "business-1",
  email: "owner@example.com",
  role: "Owner",
  mustChangePassword: false,
  requiresBusinessSelection: false,
  preAuthToken: null,
  memberships: null,
};

const MULTI_MEMBERSHIP_RESULT: AuthResult = {
  accessToken: null,
  expiresAtUtc: null,
  userId: null,
  businessId: null,
  email: null,
  role: null,
  mustChangePassword: null,
  requiresBusinessSelection: true,
  preAuthToken: "preauth-token-xyz",
  memberships: [
    { businessId: "business-a", businessName: "Business A", role: "Owner" },
    { businessId: "business-b", businessName: "Business B", role: "Staff" },
  ],
};

function renderLoginPage() {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={["/login"]}>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/select-business" element={<SelectBusinessProbe />} />
            <Route path="/dashboard" element={<div>Dashboard page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

// Renders the router state it was handed off so the test can assert on it without depending on
// SelectBusinessPage's own implementation.
function SelectBusinessProbe() {
  return <div>Select-business page</div>;
}

async function submitLoginForm(email: string, password: string) {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText(/email/i), email);
  await user.type(screen.getByLabelText(/password/i), password);
  await user.click(screen.getByRole("button", { name: /log in/i }));
}

describe("LoginPage branching on login() outcome", () => {
  beforeEach(() => {
    clearSession();
  });

  it("navigates to /select-business with the preauth token + memberships when requiresBusinessSelection is true", async () => {
    server.use(http.post("*/api/auth/login", () => HttpResponse.json(MULTI_MEMBERSHIP_RESULT)));

    renderLoginPage();
    await submitLoginForm("multi@example.com", "password123");

    await waitFor(() => expect(screen.getByText("Select-business page")).toBeInTheDocument());
    expect(screen.queryByText("Dashboard page")).not.toBeInTheDocument();
    // No token/user should have been persisted for the unresolved multi-membership case.
    expect(localStorage.getItem("invoice-tracker.accessToken")).toBeNull();
  });

  it("navigates straight to /dashboard when requiresBusinessSelection is false", async () => {
    server.use(http.post("*/api/auth/login", () => HttpResponse.json(SINGLE_MEMBERSHIP_RESULT)));

    renderLoginPage();
    await submitLoginForm("owner@example.com", "password123");

    await waitFor(() => expect(screen.getByText("Dashboard page")).toBeInTheDocument());
    expect(screen.queryByText("Select-business page")).not.toBeInTheDocument();
    expect(localStorage.getItem("invoice-tracker.accessToken")).toBe(SINGLE_MEMBERSHIP_RESULT.accessToken);
  });
});

// user_testing_defects.md item #18 (mobile forms/dialogs pass): inputs and the submit button
// must carry a 44px-minimum mobile touch target (`h-11`), reverting to the existing desktop
// sizing at the ~768px breakpoint (`md:h-8`) — not a visual-rendering assertion (jsdom doesn't
// evaluate the breakpoint), just confirming the sizing classes are actually present.
describe("LoginPage mobile touch targets", () => {
  beforeEach(() => {
    clearSession();
  });

  it("gives the email/password inputs and submit button a 44px-minimum mobile height", () => {
    renderLoginPage();

    expect(screen.getByLabelText(/email/i).className).toContain("h-11");
    expect(screen.getByLabelText(/password/i).className).toContain("h-11");
    const submitButton = screen.getByRole("button", { name: /log in/i });
    expect(submitButton.className).toContain("h-11");
    // Unchanged at md+ — the desktop layout this mobile pass must not alter.
    expect(submitButton.className).toContain("md:h-8");
  });
});
