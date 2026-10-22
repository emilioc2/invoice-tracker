import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { useLocation } from "react-router-dom";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AuthProvider } from "@/context/AuthContext";
import { DashboardPage } from "@/routes/DashboardPage";
import { server } from "@/test/server";
import { createTestQueryClient, seedAuthenticatedSession } from "@/test/test-utils";
import type { DashboardSummary } from "@/api/dashboardApi";

const SUMMARY: DashboardSummary = {
  outstandingTotal: 1234.56,
  overdueCount: 2,
  countsByStatus: [
    { status: "Draft", count: 1 },
    { status: "Unpaid", count: 3 },
    { status: "Overdue", count: 2 },
    { status: "Paid", count: 5 },
    { status: "Cancelled", count: 0 },
  ],
};

// Stub destination so these tests assert exactly what DashboardPage hands to `navigate`,
// without pulling in InvoicesListPage's own API/client dependencies.
function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-state">{JSON.stringify(location.state)}</div>;
}

function renderDashboard() {
  server.use(http.get("*/api/dashboard/summary", () => HttpResponse.json(SUMMARY)));

  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={["/"]}>
          <Routes>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/invoices" element={<LocationProbe />} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe("DashboardPage status card navigation", () => {
  beforeEach(() => {
    seedAuthenticatedSession();
  });

  it("navigates to /invoices with initialStatus 'Overdue' when the Overdue invoices card is clicked", async () => {
    const user = userEvent.setup();
    renderDashboard();

    const overdueCard = await screen.findByRole("button", { name: /overdue invoices/i });
    await user.click(overdueCard);

    expect(await screen.findByTestId("location-state")).toHaveTextContent(
      JSON.stringify({ initialStatus: "Overdue" }),
    );
  });

  it("navigates with the matching status for each 'Invoices by status' card", async () => {
    const user = userEvent.setup();
    renderDashboard();

    // Anchored to the start of the accessible name ("Paid 5") so it doesn't also match the
    // "Unpaid 3" card.
    const paidCard = await screen.findByRole("button", { name: /^paid\b/i });
    await user.click(paidCard);

    expect(await screen.findByTestId("location-state")).toHaveTextContent(
      JSON.stringify({ initialStatus: "Paid" }),
    );
  });

  it("does not render the Outstanding total card as a button, and clicking it does not navigate", async () => {
    const user = userEvent.setup();
    renderDashboard();

    const outstandingLabel = await screen.findByText("Outstanding total");
    const card = outstandingLabel.closest("div");
    expect(card).not.toBeNull();

    // Not a button, and not treated as one by assistive tech.
    expect(card?.tagName).toBe("DIV");
    expect(screen.queryByRole("button", { name: /outstanding total/i })).not.toBeInTheDocument();

    if (card) await user.click(card);

    // Still on the dashboard, not navigated away.
    expect(screen.getByText("Dashboard")).toBeInTheDocument();
    expect(screen.queryByTestId("location-state")).not.toBeInTheDocument();
  });

  it("activates the Overdue card via keyboard (Tab + Enter)", async () => {
    const user = userEvent.setup();
    renderDashboard();

    const overdueCard = await screen.findByRole("button", { name: /overdue invoices/i });
    overdueCard.focus();
    expect(overdueCard).toHaveFocus();

    await user.keyboard("{Enter}");

    expect(await screen.findByTestId("location-state")).toHaveTextContent(
      JSON.stringify({ initialStatus: "Overdue" }),
    );
  });
});
