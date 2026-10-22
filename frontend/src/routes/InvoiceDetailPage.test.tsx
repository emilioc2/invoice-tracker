import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AuthProvider } from "@/context/AuthContext";
import { InvoiceDetailPage } from "@/routes/InvoiceDetailPage";
import { server } from "@/test/server";
import { createTestQueryClient, seedAuthenticatedSession } from "@/test/test-utils";
import type { Invoice } from "@/types/invoice";

// Covers the new Send/Resend email-delivery slice described in architecture.md's "Invoice
// send/resend email delivery" section: the Resend button's visibility by status, that a
// `Failed` emailDelivery outcome surfaces via the non-destructive info Alert (with wording that
// differs between Send and Resend) rather than the destructive error Alert, that a `Sent`
// outcome shows no extra message, and that a genuine request failure still goes through the
// existing destructive `actionError` Alert.

const BASE_CLIENT = {
  id: "client-1",
  name: "Acme Co",
  email: "a@acme.test",
  phone: "",
  address: "",
  createdAtDate: "2026-01-01T00:00:00Z",
  lastModifiedDate: "2026-01-01T00:00:00Z",
  archivedAt: null,
};

function buildInvoice(overrides: Partial<Invoice> = {}): Invoice {
  return {
    id: "invoice-1",
    clientId: "client-1",
    invoiceNumber: "INV-0001",
    createdAtDate: "2026-01-01T00:00:00Z",
    issueDate: null,
    dueDate: "2026-02-01T00:00:00Z",
    status: "Draft",
    subtotal: 100,
    taxRate: 0,
    taxAmount: 0,
    total: 100,
    notes: "",
    lastModifiedDate: "2026-01-01T00:00:00Z",
    archivedAt: null,
    lineItems: [],
    ...overrides,
  };
}

function renderDetailPage(invoice: Invoice) {
  server.use(
    http.get("*/api/invoices/:id", () => HttpResponse.json(invoice)),
    http.get("*/api/clients/:id", () => HttpResponse.json(BASE_CLIENT)),
  );

  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={[`/invoices/${invoice.id}`]}>
          <Routes>
            <Route path="/invoices/:invoiceId" element={<InvoiceDetailPage />} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe("InvoiceDetailPage — Resend button visibility", () => {
  beforeEach(() => {
    seedAuthenticatedSession();
  });

  it("does not show a Resend email button for a Draft invoice", async () => {
    renderDetailPage(buildInvoice({ status: "Draft" }));

    expect(await screen.findByText("INV-0001")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /resend email/i })).not.toBeInTheDocument();
  });

  it("shows a Resend email button for a non-Draft invoice", async () => {
    renderDetailPage(
      buildInvoice({ status: "Unpaid", issueDate: "2026-01-02T00:00:00Z" }),
    );

    expect(await screen.findByText("INV-0001")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /resend email/i })).toBeInTheDocument();
  });
});

describe("InvoiceDetailPage — Send email delivery outcome", () => {
  beforeEach(() => {
    seedAuthenticatedSession();
  });

  it("shows no info message when Send succeeds and email delivery is Sent", async () => {
    const draft = buildInvoice({ status: "Draft" });
    renderDetailPage(draft);
    server.use(
      http.post("*/api/invoices/:id/send", () =>
        HttpResponse.json({
          invoice: { ...draft, status: "Unpaid", issueDate: "2026-01-05T00:00:00Z" },
          emailDelivery: { status: "Sent", notificationId: "notif-1" },
        }),
      ),
    );

    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: /^send$/i }));
    await user.click(await screen.findByRole("button", { name: /^send$/i }));

    await waitFor(() =>
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument(),
    );
    expect(screen.queryByText(/email delivery/i)).not.toBeInTheDocument();
  });

  it("shows the Send-specific info Alert when email delivery fails, without the destructive error Alert", async () => {
    const draft = buildInvoice({ status: "Draft" });
    renderDetailPage(draft);
    server.use(
      http.post("*/api/invoices/:id/send", () =>
        HttpResponse.json({
          invoice: { ...draft, status: "Unpaid", issueDate: "2026-01-05T00:00:00Z" },
          emailDelivery: { status: "Failed", notificationId: "notif-2" },
        }),
      ),
    );

    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: /^send$/i }));
    await user.click(await screen.findByRole("button", { name: /^send$/i }));

    expect(
      await screen.findByText(
        "Invoice sent, but email delivery failed. You can try resending it below.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText("Action failed")).not.toBeInTheDocument();
  });
});

describe("InvoiceDetailPage — Resend email delivery outcome", () => {
  beforeEach(() => {
    seedAuthenticatedSession();
  });

  it("shows the Resend-specific info Alert when email delivery fails", async () => {
    renderDetailPage(buildInvoice({ status: "Unpaid", issueDate: "2026-01-02T00:00:00Z" }));
    server.use(
      http.post("*/api/invoices/:id/resend-email", () =>
        HttpResponse.json({
          emailDelivery: { status: "Failed", notificationId: "notif-3" },
        }),
      ),
    );

    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: /resend email/i }));
    await user.click(await screen.findByRole("button", { name: /^resend$/i }));

    expect(
      await screen.findByText(
        "Email delivery failed. You can try resending it again below.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText("Action failed")).not.toBeInTheDocument();
  });

  it("shows no info message when Resend succeeds and email delivery is Sent", async () => {
    const invoice = buildInvoice({ status: "Paid", issueDate: "2026-01-02T00:00:00Z" });
    renderDetailPage(invoice);
    server.use(
      http.post("*/api/invoices/:id/resend-email", () =>
        HttpResponse.json({
          emailDelivery: { status: "Sent", notificationId: "notif-4" },
        }),
      ),
    );

    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: /resend email/i }));
    await user.click(await screen.findByRole("button", { name: /^resend$/i }));

    await waitFor(() =>
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument(),
    );
    expect(screen.queryByText(/email delivery/i)).not.toBeInTheDocument();
    // Status/invoice content is unaffected by a resend.
    expect(screen.getByText("INV-0001")).toBeInTheDocument();
  });

  it("falls back to the destructive error Alert, not the info Alert, on an outright request failure", async () => {
    renderDetailPage(buildInvoice({ status: "Unpaid", issueDate: "2026-01-02T00:00:00Z" }));
    server.use(
      http.post("*/api/invoices/:id/resend-email", () =>
        HttpResponse.json({ title: "Invoice is in Draft status." }, { status: 400 }),
      ),
    );

    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: /resend email/i }));
    await user.click(await screen.findByRole("button", { name: /^resend$/i }));

    expect(await screen.findByText("Action failed")).toBeInTheDocument();
    expect(screen.getByText("Invoice is in Draft status.")).toBeInTheDocument();
    expect(screen.queryByText(/email delivery failed/i)).not.toBeInTheDocument();
  });
});
