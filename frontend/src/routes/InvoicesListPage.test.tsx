import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AuthProvider } from "@/context/AuthContext";
import { InvoicesListPage } from "@/routes/InvoicesListPage";
import { server } from "@/test/server";
import { createTestQueryClient, seedAuthenticatedSession } from "@/test/test-utils";
import type { PaginatedResult } from "@/types/common";
import type { Invoice } from "@/types/invoice";

const EMPTY_INVOICES: PaginatedResult<Invoice> = {
  items: [],
  totalCount: 0,
  pageNumber: 1,
  pageSize: 20,
  totalPages: 1,
};

const EMPTY_CLIENTS = {
  items: [
    { id: "client-1", name: "Acme Co", email: "a@acme.test", phone: "", address: "", createdAtDate: "2026-01-01T00:00:00Z", lastModifiedDate: "2026-01-01T00:00:00Z", archivedAt: null },
  ],
  totalCount: 1,
  pageNumber: 1,
  pageSize: 100,
  totalPages: 1,
};

function renderInvoicesListPage(capturedUrls: string[]) {
  server.use(
    http.get("*/api/invoices", ({ request }) => {
      capturedUrls.push(request.url);
      return HttpResponse.json(EMPTY_INVOICES);
    }),
    http.get("*/api/clients", () => HttpResponse.json(EMPTY_CLIENTS)),
  );

  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={["/invoices"]}>
          <Routes>
            <Route path="/invoices" element={<InvoicesListPage />} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

function searchParamsOf(url: string): URLSearchParams {
  return new URL(url).searchParams;
}

describe("InvoicesListPage status filter", () => {
  beforeEach(() => {
    seedAuthenticatedSession();
  });

  it("sends no status param on initial load", async () => {
    const capturedUrls: string[] = [];
    renderInvoicesListPage(capturedUrls);

    await waitFor(() => expect(capturedUrls).toHaveLength(1));
    expect(searchParamsOf(capturedUrls[0]).get("status")).toBeNull();
  });

  it("includes ?status=<value> once a specific status is selected", async () => {
    const user = userEvent.setup();
    const capturedUrls: string[] = [];
    renderInvoicesListPage(capturedUrls);

    await waitFor(() => expect(capturedUrls).toHaveLength(1));

    await user.click(screen.getByRole("combobox", { name: /status/i }));
    const listbox = await screen.findByRole("listbox");
    await user.click(within(listbox).getByRole("option", { name: "Overdue" }));

    await waitFor(() => expect(capturedUrls).toHaveLength(2));
    expect(searchParamsOf(capturedUrls[1]).get("status")).toBe("Overdue");
  });

  it("removes the status param (not an empty string) when switching back to All statuses", async () => {
    const user = userEvent.setup();
    const capturedUrls: string[] = [];
    renderInvoicesListPage(capturedUrls);

    await waitFor(() => expect(capturedUrls).toHaveLength(1));

    await user.click(screen.getByRole("combobox", { name: /status/i }));
    let listbox = await screen.findByRole("listbox");
    await user.click(within(listbox).getByRole("option", { name: "Paid" }));
    await waitFor(() => expect(capturedUrls).toHaveLength(2));
    expect(searchParamsOf(capturedUrls[1]).get("status")).toBe("Paid");

    await user.click(screen.getByRole("combobox", { name: /status/i }));
    listbox = await screen.findByRole("listbox");
    await user.click(within(listbox).getByRole("option", { name: "All statuses" }));

    await waitFor(() => expect(capturedUrls).toHaveLength(3));
    const finalParams = searchParamsOf(capturedUrls[2]);
    expect(finalParams.has("status")).toBe(false);
    expect(finalParams.get("status")).not.toBe("");
  });
});
