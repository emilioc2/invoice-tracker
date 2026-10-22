import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { useDashboardSummaryQuery } from "@/hooks/useDashboard";
import { useMarkInvoicePaidMutation, useSendInvoiceMutation } from "@/hooks/useInvoices";
import { server } from "@/test/server";
import { createTestQueryClient } from "@/test/test-utils";
import type { Invoice } from "@/types/invoice";

// Covers architecture.md's rule (the note after the useCreateInvoiceMutation example): "any
// mutation that changes an invoice's status or existence ... must invalidate both
// ['invoices']-prefixed keys and ['dashboard', 'summary']". Rather than asserting on
// `invalidateQueries` having been called (an implementation detail), these tests assert the
// actually-observable effect: a component subscribed to `useDashboardSummaryQuery` re-fetches
// and re-renders with fresh data once the mutation succeeds.

const BASE_INVOICE: Invoice = {
  id: "invoice-1",
  clientId: "client-1",
  invoiceNumber: "INV-0001",
  createdAtDate: "2026-01-01T00:00:00Z",
  issueDate: "2026-01-01T00:00:00Z",
  dueDate: "2026-02-01T00:00:00Z",
  status: "Unpaid",
  subtotal: 100,
  taxRate: 0,
  taxAmount: 0,
  total: 100,
  notes: "",
  lastModifiedDate: "2026-01-01T00:00:00Z",
  archivedAt: null,
  lineItems: [],
};

function DashboardAndSendHarness({ invoiceId }: { invoiceId: string }) {
  const { data } = useDashboardSummaryQuery();
  const sendMutation = useSendInvoiceMutation();
  return (
    <div>
      <div data-testid="outstanding-total">{data?.outstandingTotal ?? "loading"}</div>
      <button onClick={() => sendMutation.mutate(invoiceId)}>Send invoice</button>
    </div>
  );
}

function DashboardAndMarkPaidHarness({ invoiceId }: { invoiceId: string }) {
  const { data } = useDashboardSummaryQuery();
  const markPaidMutation = useMarkInvoicePaidMutation();
  return (
    <div>
      <div data-testid="outstanding-total">{data?.outstandingTotal ?? "loading"}</div>
      <button onClick={() => markPaidMutation.mutate(invoiceId)}>Mark paid</button>
    </div>
  );
}

describe("invoice mutations invalidate the dashboard summary query", () => {
  beforeEach(() => {
    // Nothing persisted between tests needs clearing here; MSW handlers are reset by the
    // shared setup.ts afterEach, and each test gets its own QueryClient.
  });

  it("useSendInvoiceMutation's success invalidates ['dashboard','summary'] and triggers a real refetch", async () => {
    let summaryCallCount = 0;
    server.use(
      http.get("*/api/dashboard/summary", () => {
        summaryCallCount += 1;
        return HttpResponse.json({
          outstandingTotal: summaryCallCount === 1 ? 100 : 250,
          overdueCount: 0,
          countsByStatus: [],
        });
      }),
      http.post("*/api/invoices/:id/send", () =>
        HttpResponse.json({
          invoice: { ...BASE_INVOICE, status: "Unpaid" },
          emailDelivery: { status: "Sent", notificationId: "notif-1" },
        }),
      ),
    );

    const queryClient = createTestQueryClient();
    render(
      <QueryClientProvider client={queryClient}>
        <DashboardAndSendHarness invoiceId={BASE_INVOICE.id} />
      </QueryClientProvider>,
    );

    await waitFor(() => expect(screen.getByTestId("outstanding-total").textContent).toBe("100"));

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /send invoice/i }));

    // If invalidation didn't happen, the dashboard query would stay cached at "100" forever.
    await waitFor(() => expect(screen.getByTestId("outstanding-total").textContent).toBe("250"));
    expect(summaryCallCount).toBeGreaterThanOrEqual(2);
  });

  it("useMarkInvoicePaidMutation's success invalidates ['dashboard','summary'] and triggers a real refetch", async () => {
    let summaryCallCount = 0;
    server.use(
      http.get("*/api/dashboard/summary", () => {
        summaryCallCount += 1;
        return HttpResponse.json({
          outstandingTotal: summaryCallCount === 1 ? 300 : 0,
          overdueCount: 0,
          countsByStatus: [],
        });
      }),
      http.post("*/api/invoices/:id/mark-paid", () =>
        HttpResponse.json({ ...BASE_INVOICE, status: "Paid" }),
      ),
    );

    const queryClient = createTestQueryClient();
    render(
      <QueryClientProvider client={queryClient}>
        <DashboardAndMarkPaidHarness invoiceId={BASE_INVOICE.id} />
      </QueryClientProvider>,
    );

    await waitFor(() => expect(screen.getByTestId("outstanding-total").textContent).toBe("300"));

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /mark paid/i }));

    await waitFor(() => expect(screen.getByTestId("outstanding-total").textContent).toBe("0"));
    expect(summaryCallCount).toBeGreaterThanOrEqual(2);
  });
});
