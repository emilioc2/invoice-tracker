import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AuthProvider } from "@/context/AuthContext";
import { NotificationsPage } from "@/routes/NotificationsPage";
import { server } from "@/test/server";
import { createTestQueryClient, seedAuthenticatedSession } from "@/test/test-utils";
import type { PaginatedResult } from "@/types/common";
import type { Notification } from "@/types/notification";

function renderNotificationsPage() {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={["/notifications"]}>
          <Routes>
            <Route path="/notifications" element={<NotificationsPage />} />
            <Route path="/invoices/:invoiceId" element={<div>Invoice detail page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe("NotificationsPage denormalized invoice number", () => {
  beforeEach(() => {
    seedAuthenticatedSession();
  });

  it("renders the list response's invoiceNumber directly and links to the invoice detail page", async () => {
    let invoiceDetailFetched = false;
    const notificationsResponse: PaginatedResult<Notification> = {
      items: [
        {
          id: "notification-1",
          invoiceId: "invoice-1",
          invoiceNumber: "INV-0001",
          status: "Sent",
          sentAt: "2026-01-02T00:00:00Z",
          channel: "Email",
          message: "Your invoice is overdue.",
          sourceEventId: "event-1",
        },
      ],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 1,
    };

    server.use(
      http.get("*/api/notifications", () => HttpResponse.json(notificationsResponse)),
      http.get("*/api/invoices/:id", () => {
        invoiceDetailFetched = true;
        return HttpResponse.json({ id: "invoice-1", invoiceNumber: "INV-0001" });
      }),
    );

    renderNotificationsPage();

    const link = await screen.findByRole("link", { name: "INV-0001" });
    expect(link).toHaveAttribute("href", "/invoices/invoice-1");
    expect(invoiceDetailFetched).toBe(false);
  });

  it("falls back to 'Unknown invoice' when invoiceNumber is null", async () => {
    const notificationsResponse: PaginatedResult<Notification> = {
      items: [
        {
          id: "notification-2",
          invoiceId: "invoice-2",
          invoiceNumber: null,
          status: "Pending",
          sentAt: null,
          channel: "Email",
          message: "Your invoice is overdue.",
          sourceEventId: "event-2",
        },
      ],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 1,
    };

    server.use(http.get("*/api/notifications", () => HttpResponse.json(notificationsResponse)));

    renderNotificationsPage();

    const link = await screen.findByRole("link", { name: "Unknown invoice" });
    expect(link).toHaveAttribute("href", "/invoices/invoice-2");
  });
});
