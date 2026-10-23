import { QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppShell } from "@/components/layout/AppShell";
import { AuthProvider } from "@/context/AuthContext";
import { server } from "@/test/server";
import {
  clearSession,
  createTestQueryClient,
  mockViewport,
  seedAuthenticatedSession,
} from "@/test/test-utils";
import type { AuthResult, MembershipOption } from "@/types/auth";

// Covers architecture.md's "Business-selector and switcher UI" paragraph's switcher half:
// GET /api/auth/memberships must only be called once the "Switch business" dropdown is actually
// opened (never eagerly on page load), and the dropdown must render loading/empty/populated
// states correctly, plus "Add a business" opening a dialog whose successful submit completes auth.
//
// On desktop, "Switch business" lives as a submenu nested inside the header's "Account" dropdown
// (the header-declutter effort that replaced the old inline email/"Switch business"/"Log out"
// row) — so each test below opens "Account" first, then its "Switch business" submenu, before
// asserting on the switcher's contents.

async function openSwitchBusinessSubmenu(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: "Account" }));
  await user.click(await screen.findByRole("menuitem", { name: /switch business/i }));
}

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

function renderAppShell(role: "Owner" | "Staff" = "Owner") {
  seedAuthenticatedSession({
    userId: "user-1",
    businessId: "business-1",
    email: role === "Owner" ? "owner@example.com" : "staff@example.com",
    role,
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

// Covers user_testing_defects.md item #16 / requirements.md's Permissions section: Staff has "no
// user management," which the Users screen itself is folded into — the nav link must be hidden
// for Staff, checked against the internal role value, not the "Admin" display label (item #2).
describe("AppShell's nav link visibility", () => {
  beforeEach(() => {
    clearSession();
  });

  it("hides the Users nav link for a Staff user", () => {
    renderAppShell("Staff");

    expect(screen.queryByRole("link", { name: "Users" })).not.toBeInTheDocument();
    // Sanity check: other nav links are unaffected.
    expect(screen.getByRole("link", { name: "Dashboard" })).toBeInTheDocument();
  });

  it("shows the Users nav link for an Owner user", () => {
    renderAppShell("Owner");

    expect(screen.getByRole("link", { name: "Users" })).toBeInTheDocument();
  });

  // requirements.md's "Business payment details" section: unlike Users, Settings is reachable by
  // any authenticated user, Owner or Staff.
  it("shows the Settings nav link for both a Staff and an Owner user", () => {
    renderAppShell("Staff");
    expect(screen.getByRole("link", { name: "Settings" })).toBeInTheDocument();
  });

  it("shows the Settings nav link for an Owner user", () => {
    renderAppShell("Owner");
    expect(screen.getByRole("link", { name: "Settings" })).toBeInTheDocument();
  });
});

// Covers the header-declutter request: the desktop header's inline email/"Switch
// business"/"Log out" row is replaced by a single compact "Account" dropdown trigger, whose
// contents only appear once opened.
describe("AppShell's desktop Account menu", () => {
  beforeEach(() => {
    clearSession();
  });

  it("shows a compact Account trigger, with the email/Switch business/Log out cluster hidden until opened", () => {
    renderAppShell();

    expect(screen.getByRole("button", { name: "Account" })).toBeInTheDocument();
    expect(screen.queryByText("owner@example.com")).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /switch business/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Log out" })).not.toBeInTheDocument();
  });

  it("opening the Account dropdown reveals the email as non-interactive text, Switch business, and Log out", async () => {
    renderAppShell();
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Account" }));

    // The email is a plain label, not a selectable menu item — it must never carry
    // `role="menuitem"` (that's the bug this test would catch: treating it as selectable).
    expect(await screen.findByText("owner@example.com")).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "owner@example.com" })).not.toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /switch business/i })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Log out" })).toBeInTheDocument();
  });

  it("gives the Account trigger a 44px-minimum touch target", () => {
    renderAppShell();

    expect(screen.getByRole("button", { name: "Account" })).toHaveClass("h-11");
  });

  it("clicking Log out in the Account dropdown logs the user out", async () => {
    renderAppShell();
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Account" }));
    await user.click(screen.getByRole("menuitem", { name: "Log out" }));

    await waitFor(() => expect(localStorage.getItem("invoice-tracker.accessToken")).toBeNull());
  });
});

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
    await openSwitchBusinessSubmenu(user);

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
    await openSwitchBusinessSubmenu(user);

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
    await openSwitchBusinessSubmenu(user);

    expect(await screen.findByText("No other businesses found for this account.")).toBeInTheDocument();
  });

  it("renders the populated state with the other membership(s) listed", async () => {
    server.use(http.get("*/api/auth/memberships", () => HttpResponse.json(MEMBERSHIPS)));

    renderAppShell();

    const user = userEvent.setup();
    await openSwitchBusinessSubmenu(user);

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
    await openSwitchBusinessSubmenu(user);

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
    await openSwitchBusinessSubmenu(user);
    // `fireEvent.click` rather than `user.click` here: this item lives inside a nested
    // `DropdownMenuSub`, and jsdom's `getBoundingClientRect` always returns a zeroed-out rect —
    // Radix's submenu "pointer grace area" math (which guards against accidentally closing the
    // submenu while moving the mouse from the trigger into its content) degenerates on that and
    // swallows `user.click`'s simulated pointer-move-then-click sequence before it reaches this
    // item. Real browsers report real rects, so this is a jsdom/testing artifact, not a bug —
    // `user.click` works fine for every non-nested item elsewhere in this file.
    fireEvent.click(await screen.findByText("Add a business"));
    console.log("AFTER_CLICK_BODY", document.body.innerHTML.includes("Add a business") ? "has text" : "no text");

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

// Covers user_testing_defects.md item #18's shared-shell pass: below the ~768px breakpoint,
// `AppShell`'s unconditional horizontal nav row is replaced by a hamburger trigger that opens a
// drawer carrying everything the desktop header shows today (nav links, email, "Switch
// business", "Log out"). jsdom doesn't evaluate real CSS, so `mockViewport` drives `useIsMobile`
// directly rather than resizing an actual viewport — see `test-utils.tsx`'s doc comment.
describe("AppShell's mobile nav (below the ~768px breakpoint)", () => {
  beforeEach(() => {
    clearSession();
    mockViewport(true);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders a hamburger trigger instead of the full nav row, with nav links hidden until opened", () => {
    renderAppShell();

    expect(screen.getByRole("button", { name: /open navigation menu/i })).toBeInTheDocument();
    // The drawer's content (a Radix dialog) isn't mounted until opened — confirming this also
    // confirms there's no duplicate "Dashboard" link sitting hidden-only-by-CSS in the DOM.
    expect(screen.queryByRole("link", { name: "Dashboard" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /switch business/i })).not.toBeInTheDocument();
  });

  it("opens the drawer on click and shows nav links, the user's email, Switch business, and Log out", async () => {
    renderAppShell();
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));

    const drawer = await screen.findByRole("dialog");
    expect(within(drawer).getByRole("link", { name: "Dashboard" })).toBeInTheDocument();
    expect(within(drawer).getByRole("link", { name: "Clients" })).toBeInTheDocument();
    expect(within(drawer).getByRole("link", { name: "Invoices" })).toBeInTheDocument();
    expect(within(drawer).getByRole("link", { name: "Notifications" })).toBeInTheDocument();
    expect(within(drawer).getByRole("link", { name: "Settings" })).toBeInTheDocument();
    expect(within(drawer).getByText("owner@example.com")).toBeInTheDocument();
    expect(within(drawer).getByRole("button", { name: /switch business/i })).toBeInTheDocument();
    expect(within(drawer).getByRole("button", { name: "Log out" })).toBeInTheDocument();
  });

  it("gives the drawer's close button a 44x44px minimum touch target", async () => {
    renderAppShell();
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));

    const drawer = await screen.findByRole("dialog");
    expect(within(drawer).getByRole("button", { name: "Close" })).toHaveClass("size-11");
  });

  it("hides the Users nav link for a Staff user inside the drawer too (item #16's role-aware nav)", async () => {
    renderAppShell("Staff");
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));

    const drawer = await screen.findByRole("dialog");
    expect(within(drawer).queryByRole("link", { name: "Users" })).not.toBeInTheDocument();
    expect(within(drawer).getByRole("link", { name: "Dashboard" })).toBeInTheDocument();
  });

  it("shows the Users nav link inside the drawer for an Owner user", async () => {
    renderAppShell("Owner");
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));

    const drawer = await screen.findByRole("dialog");
    expect(within(drawer).getByRole("link", { name: "Users" })).toBeInTheDocument();
  });

  it("closes the drawer when a nav link inside it is clicked", async () => {
    renderAppShell();
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));
    const drawer = await screen.findByRole("dialog");

    await user.click(within(drawer).getByRole("link", { name: "Clients" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("clicking Log out inside the drawer logs the user out", async () => {
    renderAppShell();
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));
    const drawer = await screen.findByRole("dialog");

    await user.click(within(drawer).getByRole("button", { name: "Log out" }));

    await waitFor(() => expect(localStorage.getItem("invoice-tracker.accessToken")).toBeNull());
  });
});
