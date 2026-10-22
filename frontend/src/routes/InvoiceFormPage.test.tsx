import { QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AuthProvider } from "@/context/AuthContext";
import { InvoiceFormPage } from "@/routes/InvoiceFormPage";
import { server } from "@/test/server";
import { createTestQueryClient, seedAuthenticatedSession } from "@/test/test-utils";
import type { Invoice } from "@/types/invoice";

const CLIENTS_RESPONSE = {
  items: [
    {
      id: "client-1",
      name: "Acme Co",
      email: "billing@acme.test",
      phone: "",
      address: "",
      createdAtDate: "2026-01-01T00:00:00Z",
      lastModifiedDate: "2026-01-01T00:00:00Z",
      status: "Active",
      statusChangedAt: "2026-01-01T00:00:00Z",
    },
  ],
  totalCount: 1,
  pageNumber: 1,
  pageSize: 100,
  totalPages: 1,
};

const CREATED_INVOICE: Invoice = {
  id: "invoice-123",
  clientId: "client-1",
  invoiceNumber: "INV-0001",
  createdAtDate: "2026-01-01T00:00:00Z",
  issueDate: null,
  dueDate: "2026-02-01T00:00:00.000Z",
  status: "Draft",
  subtotal: 100,
  taxRate: 0,
  taxAmount: 0,
  total: 100,
  notes: "",
  lastModifiedDate: "2026-01-01T00:00:00Z",
  archivedAt: null,
  lineItems: [{ id: "line-1", description: "Consulting services", quantity: 1, unitPrice: 100, lineTotal: 100 }],
};

function renderInvoiceFormPage() {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={["/invoices/new"]}>
          <Routes>
            <Route path="/invoices/new" element={<InvoiceFormPage />} />
            <Route
              path="/invoices/:invoiceId"
              element={<div>Invoice detail page</div>}
            />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

/** Selects a client option from the (Radix) client `Select` by its visible name. */
async function selectClient(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("combobox", { name: /client/i }));
  const option = await screen.findByRole("option", { name: "Acme Co" });
  await user.click(option);
}

describe("InvoiceFormPage validation (create mode)", () => {
  beforeEach(() => {
    seedAuthenticatedSession();
    server.use(http.get("*/api/clients", () => HttpResponse.json(CLIENTS_RESPONSE)));
  });

  it("blocks submission and shows inline errors when a line item has quantity 0", async () => {
    let postInvoiceCalled = false;
    server.use(
      http.post("*/api/invoices", () => {
        postInvoiceCalled = true;
        return HttpResponse.json(CREATED_INVOICE, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderInvoiceFormPage();

    await selectClient(user);
    fireEvent.change(screen.getByLabelText(/due date/i), { target: { value: "2026-02-01" } });
    await user.type(screen.getByLabelText(/description/i), "Consulting services");

    const quantityInput = screen.getByLabelText(/quantity/i);
    await user.clear(quantityInput);
    await user.type(quantityInput, "0");

    await user.click(screen.getByRole("button", { name: /create invoice/i }));

    expect(await screen.findByText("Quantity must be greater than 0.")).toBeInTheDocument();
    expect(postInvoiceCalled).toBe(false);
    expect(screen.queryByText("Invoice detail page")).not.toBeInTheDocument();
  });

  it("blocks submission and shows inline errors when required fields are left empty", async () => {
    let postInvoiceCalled = false;
    server.use(
      http.post("*/api/invoices", () => {
        postInvoiceCalled = true;
        return HttpResponse.json(CREATED_INVOICE, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderInvoiceFormPage();

    // Submit with nothing filled in at all: empty client, empty due date, empty description.
    await user.click(screen.getByRole("button", { name: /create invoice/i }));

    expect(await screen.findByText("Client is required.")).toBeInTheDocument();
    expect(screen.getByText("Due date is required.")).toBeInTheDocument();
    expect(screen.getByText("Description is required.")).toBeInTheDocument();
    expect(postInvoiceCalled).toBe(false);
  });

  it("submits successfully and navigates to the created invoice when input is valid", async () => {
    let postInvoiceCalled = false;
    server.use(
      http.post("*/api/invoices", async ({ request }) => {
        postInvoiceCalled = true;
        const body = (await request.json()) as Record<string, unknown>;
        expect(body.clientId).toBe("client-1");
        return HttpResponse.json(CREATED_INVOICE, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderInvoiceFormPage();

    await selectClient(user);
    fireEvent.change(screen.getByLabelText(/due date/i), { target: { value: "2026-02-01" } });
    await user.type(screen.getByLabelText(/description/i), "Consulting services");
    // Quantity (1) and unit price (0) already satisfy validation via the row's defaults.

    await user.click(screen.getByRole("button", { name: /create invoice/i }));

    await waitFor(() => expect(postInvoiceCalled).toBe(true));
    expect(await screen.findByText("Invoice detail page")).toBeInTheDocument();
  });
});
