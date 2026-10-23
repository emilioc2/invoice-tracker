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
import type { BusinessProfile } from "@/types/business";
import type { Invoice } from "@/types/invoice";

const BUSINESS_PROFILE: BusinessProfile = {
  id: "business-1",
  name: "Acme Business",
  email: "business@acme.test",
  paymentDetails: "Pay by bank transfer to Acme Business, acct 12345.",
};

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
  paymentDetails: null,
  lastModifiedDate: "2026-01-01T00:00:00Z",
  archivedAt: null,
  lineItems: [{ id: "line-1", description: "Consulting services", quantity: 1, unitPrice: 100, lineTotal: 100 }],
};

function renderInvoiceFormPage(initialRoute = "/invoices/new") {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={[initialRoute]}>
          <Routes>
            <Route path="/invoices/new" element={<InvoiceFormPage />} />
            <Route path="/invoices/:invoiceId/edit" element={<InvoiceFormPage />} />
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
    server.use(http.get("*/api/business", () => HttpResponse.json(BUSINESS_PROFILE)));
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

// user_testing_defects.md item #18 (mobile forms/dialogs pass): the line-item row's icon-only
// remove button was the highest-risk touch target in the app (a size-7/28px button by default) —
// confirms it now carries a 44px-minimum mobile hit area, reverting to the existing desktop
// sizing at the ~768px breakpoint.
describe("InvoiceFormPage line-item editor mobile touch targets", () => {
  beforeEach(() => {
    seedAuthenticatedSession();
    server.use(http.get("*/api/clients", () => HttpResponse.json(CLIENTS_RESPONSE)));
    server.use(http.get("*/api/business", () => HttpResponse.json(BUSINESS_PROFILE)));
  });

  it("gives the remove-line-item button a 44px-minimum mobile hit area", () => {
    renderInvoiceFormPage();

    const removeButton = screen.getByRole("button", { name: /remove line item 1/i });
    expect(removeButton.className).toContain("size-11");
    // Unchanged at md+ (icon-sm's size-7) — the desktop layout this mobile pass must not alter.
    expect(removeButton.className).toContain("md:size-7");
  });

  it("gives the quantity/unit price inputs a 44px-minimum mobile height", () => {
    renderInvoiceFormPage();

    expect(screen.getByLabelText(/quantity/i).className).toContain("h-11");
    expect(screen.getByLabelText(/unit price/i).className).toContain("h-11");
  });
});

// requirements.md/architecture.md's "Business payment details" section: the create form
// pre-fills `paymentDetails` from the business's current default as an editable starting point;
// the edit form (Draft-only) pre-fills from the invoice's own value instead, never the business
// default, and submits whatever the user actually leaves in the field either way.
describe("InvoiceFormPage — payment details field", () => {
  beforeEach(() => {
    seedAuthenticatedSession();
    server.use(http.get("*/api/clients", () => HttpResponse.json(CLIENTS_RESPONSE)));
  });

  it("pre-fills the field from the business's current default in create mode", async () => {
    server.use(http.get("*/api/business", () => HttpResponse.json(BUSINESS_PROFILE)));
    renderInvoiceFormPage();

    expect(
      await screen.findByDisplayValue(BUSINESS_PROFILE.paymentDetails as string),
    ).toBeInTheDocument();
  });

  it("submits a user-edited payment details value, not the untouched business default", async () => {
    server.use(http.get("*/api/business", () => HttpResponse.json(BUSINESS_PROFILE)));
    let capturedBody: Record<string, unknown> = {};
    server.use(
      http.post("*/api/invoices", async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(CREATED_INVOICE, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderInvoiceFormPage();

    await screen.findByDisplayValue(BUSINESS_PROFILE.paymentDetails as string);
    const paymentDetailsField = screen.getByLabelText(/payment details/i);
    await user.clear(paymentDetailsField);
    await user.type(paymentDetailsField, "Pay via Venmo @acme-business");

    await selectClient(user);
    fireEvent.change(screen.getByLabelText(/due date/i), { target: { value: "2026-02-01" } });
    await user.type(screen.getByLabelText(/description/i), "Consulting services");

    await user.click(screen.getByRole("button", { name: /create invoice/i }));

    await waitFor(() =>
      expect(capturedBody.paymentDetails).toBe("Pay via Venmo @acme-business"),
    );
  });

  it("pre-fills the field from the invoice's own value, not the business default, in edit mode", async () => {
    const draftInvoice: Invoice = {
      ...CREATED_INVOICE,
      id: "invoice-edit-1",
      status: "Draft",
      paymentDetails: "Pay by cheque to Invoice Co.",
    };
    server.use(
      http.get("*/api/invoices/:id", () => HttpResponse.json(draftInvoice)),
      // A business default is deliberately present here too, to prove it's ignored in edit mode.
      http.get("*/api/business", () => HttpResponse.json(BUSINESS_PROFILE)),
    );

    renderInvoiceFormPage(`/invoices/${draftInvoice.id}/edit`);

    expect(await screen.findByDisplayValue("Pay by cheque to Invoice Co.")).toBeInTheDocument();
    expect(
      screen.queryByDisplayValue(BUSINESS_PROFILE.paymentDetails as string),
    ).not.toBeInTheDocument();
  });

  it("does not show edit affordances for payment details on a non-Draft invoice", async () => {
    const sentInvoice: Invoice = {
      ...CREATED_INVOICE,
      id: "invoice-sent-1",
      status: "Unpaid",
      issueDate: "2026-01-02T00:00:00Z",
      paymentDetails: "Pay by cheque to Invoice Co.",
    };
    server.use(http.get("*/api/invoices/:id", () => HttpResponse.json(sentInvoice)));

    renderInvoiceFormPage(`/invoices/${sentInvoice.id}/edit`);

    expect(await screen.findByText("This invoice can no longer be edited")).toBeInTheDocument();
    expect(screen.queryByLabelText(/payment details/i)).not.toBeInTheDocument();
  });
});
