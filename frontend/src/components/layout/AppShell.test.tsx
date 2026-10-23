import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AppShell } from "@/components/layout/AppShell";
import { AuthProvider } from "@/context/AuthContext";
import { server } from "@/test/server";
import { clearSession, createTestQueryClient, seedAuthenticatedSession } from "@/test/test-utils";
import type { AuthResult, MembershipOption } from "@/types/auth";

// Covers architecture.md's "Business-selector and switcher UI" paragraph's switcher half:
// GET /api/auth/memberships must only be called once the "Switch business" dropdown is actually
// opened (never eagerly on page load), and the dropdown must render loading/empty/populated
// states correctly, plus "Add a business" opening a dialog whose successful submit completes auth.

const MEMBERSHIPS: MembershipOption[] = [
  { businessId: "business-2", businessName: "Second Business", role: "Staff" },
];

const ADD_BUSINESS_RESULT: AuthResult = {
  accessToken: "new-business-token",
  expiresAtUtc: "2026-01-01T00:00:00Z",
  userId: "user-1",
  businessId: "business-3",
  email: "owner@example.com",
  role: "Owner",
  mustChangePassword: false,
  requiresBusinessSelection: false,
  preAuthToken: null,
  memberships: null,
};

function renderAppShell() {
  seedAuthenticatedSession({
    userId: "user-1",
    businessId: "business-1",
    email: "owner@example.com",
    role: "Owner",
    mustChangePassword: false,
  });

  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={["/dashboard"]}>
          <AppShell>
            <div>Page content</div>
          </AppShell>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe("AppShell's BusinessSwitcher", () => {
  beforeEach(() => {
    clearSession();
  });

  it("does not call GET /api/auth/memberships until the dropdown is opened", async () => {
    let membershipsCalled = false;
    server.use(
      http.get("*/api/auth/memberships", () => {
        membershipsCalled = true;
        return HttpResponse.json(MEMBERSHIPS);
      }),
    );

    renderAppShell();

    // Give any eager/mount-time fetch a chance to fire before asserting it didn't.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(membershipsCalled).toBe(false);

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /switch business/i }));

    await waitFor(() => expect(membershipsCalled).toBe(true));
  });

  it("renders the loading state while the fetch is in flight", async () => {
    let resolveMemberships: (value: MembershipOption[]) => void;
    const pending = new Promise<MembershipOption[]>((resolve) => {
      resolveMemberships = resolve;
    });
    server.use(
      http.get("*/api/auth/memberships", async () => {
        const data = await pending;
        return HttpResponse.json(data);
      }),
    );

    renderAppShell();

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /switch business/i }));

    expect(await screen.findByText("Loading…")).toBeInTheDocument();

    resolveMemberships!(MEMBERSHIPS);
    await waitFor(() => expect(screen.getByText("Second Business")).toBeInTheDocument());
  });

  it("renders the empty state for a single-membership account with no other businesses", async () => {
    // The real backend endpoint returns the caller's own full active-membership list (including
    // their current business, not just "others" — see GetMyMembershipsQueryHandler), so a
    // genuinely single-membership account's real response is a one-item array containing only
    // their own current business ("business-1", matching seedAuthenticatedSession below) — not
    // an empty array. BusinessSwitcher's own `otherMemberships` filter (AppShell.tsx) is what
    // turns that into the empty state; asserting against this realistic payload (rather than a
    // bare `[]`) is what actually exercises that filter, not just the empty-list render branch.
    server.use(
      http.get("*/api/auth/memberships", () =>
        HttpResponse.json([{ businessId: "business-1", businessName: "My Only Business", role: "Owner" }]),
      ),
    );

    renderAppShell();

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /switch business/i }));

    expect(await screen.findByText("No other businesses found for this account.")).toBeInTheDocument();
  });

  it("renders the populated state with the other membership(s) listed", async () => {
    server.use(http.get("*/api/auth/memberships", () => HttpResponse.json(MEMBERSHIPS)));

    renderAppShell();

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /switch business/i }));

    expect(await screen.findByText("Second Business")).toBeInTheDocument();
    expect(screen.getByText("Staff")).toBeInTheDocument();
  });

  it("displays an Owner-role other membership as \"Admin\", never the raw \"Owner\" string", async () => {
    // Display-only: "Owner" is relabeled "Admin" in the UI (see `displayRole` in lib/utils.ts).
    // MEMBERSHIPS above only ever exercises a Staff-role other-membership, which would not have
    // caught a regression of the switcher leaking the raw role — this covers that gap directly.
    server.use(
      http.get("*/api/auth/memberships", () =>
        HttpResponse.json([{ businessId: "business-2", businessName: "Second Business", role: "Owner" }]),
      ),
    );

    renderAppShell();

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /switch business/i }));

    expect(await screen.findByText("Second Business")).toBeInTheDocument();
    expect(screen.getByText("Admin")).toBeInTheDocument();
    expect(screen.queryByText("Owner")).not.toBeInTheDocument();
  });

  it("'Add a business' opens the dialog, and a successful submit completes auth", async () => {
    server.use(http.get("*/api/auth/memberships", () => HttpResponse.json([])));

    let capturedBody: unknown;
    server.use(
      http.post("*/api/auth/businesses", async ({ request }) => {
        capturedBody = await request.json();
        return HttpResponse.json(ADD_BUSINESS_RESULT);
      }),
    );

    renderAppShell();

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /switch business/i }));
    await user.click(await screen.findByText("Add a business"));

    expect(await screen.findByRole("heading", { name: "Add a business" })).toBeInTheDocument();

    await user.type(screen.getByLabelText(/business name/i), "Third Business");
    await user.type(screen.getByLabelText(/business contact email/i), "third@example.com");
    await user.click(screen.getByRole("button", { name: /^add business$/i }));

    await waitFor(() =>
      expect(localStorage.getItem("invoice-tracker.accessToken")).toBe(ADD_BUSINESS_RESULT.accessToken),
    );
    expect(capturedBody).toEqual({ businessName: "Third Business", businessEmail: "third@example.com" });
  });
});
