import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AuthProvider } from "@/context/AuthContext";
import { UsersPage } from "@/routes/UsersPage";
import { server } from "@/test/server";
import { createTestQueryClient, seedAuthenticatedSession } from "@/test/test-utils";
import type { User } from "@/types/auth";

// Covers this item's new behavior (architecture.md's "Domain invariants: centralized status
// transitions" generalized to User): the Deactivate/Reactivate actions are mutually exclusive per
// row based on `status`, and clicking Reactivate actually calls the new
// `POST /api/users/{id}/reactivate` endpoint and reflects the result once the list re-fetches.

const ACTIVE_STAFF: User = {
  id: "staff-active",
  email: "active-staff@example.com",
  role: "Staff",
  status: "Active",
  statusChangedAt: "2026-01-01T00:00:00Z",
};

const DEACTIVATED_STAFF: User = {
  id: "staff-deactivated",
  email: "deactivated-staff@example.com",
  role: "Staff",
  status: "Deactivated",
  statusChangedAt: "2026-01-01T00:00:00Z",
};

function renderUsersPage(users: User[]) {
  server.use(http.get("*/api/users", () => HttpResponse.json(users)));

  seedAuthenticatedSession({
    userId: "owner-1",
    businessId: "business-1",
    email: "owner@example.com",
    role: "Owner",
    mustChangePassword: false,
  });

  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={["/users"]}>
          <UsersPage />
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe("UsersPage Deactivate/Reactivate actions", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("shows Deactivate for an Active Staff user and Reactivate for a Deactivated one", async () => {
    renderUsersPage([ACTIVE_STAFF, DEACTIVATED_STAFF]);

    await screen.findByText(ACTIVE_STAFF.email);

    const activeRow = screen.getByText(ACTIVE_STAFF.email).closest("tr")!;
    const deactivatedRow = screen.getByText(DEACTIVATED_STAFF.email).closest("tr")!;

    expect(within(activeRow).getByRole("button", { name: "Deactivate" })).toBeInTheDocument();
    expect(within(activeRow).queryByRole("button", { name: "Reactivate" })).not.toBeInTheDocument();

    expect(within(deactivatedRow).getByRole("button", { name: "Reactivate" })).toBeInTheDocument();
    expect(within(deactivatedRow).queryByRole("button", { name: "Deactivate" })).not.toBeInTheDocument();
  });

  it("clicking Reactivate calls the reactivate endpoint and the row flips to Deactivate", async () => {
    const user = userEvent.setup();
    let reactivateCalled = false;

    renderUsersPage([DEACTIVATED_STAFF]);

    server.use(
      http.post("*/api/users/:id/reactivate", ({ params }) => {
        reactivateCalled = true;
        expect(params.id).toBe(DEACTIVATED_STAFF.id);
        return new HttpResponse(null, { status: 204 });
      }),
      http.get("*/api/users", () =>
        HttpResponse.json(reactivateCalled ? [{ ...DEACTIVATED_STAFF, status: "Active" }] : [DEACTIVATED_STAFF]),
      ),
    );

    await screen.findByText(DEACTIVATED_STAFF.email);
    await user.click(screen.getByRole("button", { name: "Reactivate" }));

    await waitFor(() => expect(reactivateCalled).toBe(true));
    await screen.findByRole("button", { name: "Deactivate" });
  });
});
