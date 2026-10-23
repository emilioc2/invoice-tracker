import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AuthProvider } from "@/context/AuthContext";
import { SettingsPage } from "@/routes/SettingsPage";
import { server } from "@/test/server";
import { createTestQueryClient, seedAuthenticatedSession } from "@/test/test-utils";
import type { BusinessProfile } from "@/types/business";

// Covers requirements.md/architecture.md's "Business payment details" section at the component
// level: GET /api/business populates the form, PUT /api/business/payment-details saves it and
// shows success/error feedback consistent with this app's other forms (e.g. UsersPage's "Success"
// Alert pattern), and both Owner and Staff can reach/use the page (no RequireRole here).

const BUSINESS_PROFILE: BusinessProfile = {
  id: "business-1",
  name: "Acme Business",
  email: "business@acme.test",
  paymentDetails: "Pay by bank transfer to Acme Business, acct 12345.",
};

function renderSettingsPage() {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={["/settings"]}>
          <SettingsPage />
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe("SettingsPage", () => {
  beforeEach(() => {
    seedAuthenticatedSession();
  });

  it("fetches and pre-fills the current payment details from GET /api/business", async () => {
    server.use(http.get("*/api/business", () => HttpResponse.json(BUSINESS_PROFILE)));

    renderSettingsPage();

    expect(
      await screen.findByDisplayValue("Pay by bank transfer to Acme Business, acct 12345."),
    ).toBeInTheDocument();
  });

  it("renders an empty field when the business has no payment details set", async () => {
    server.use(
      http.get("*/api/business", () =>
        HttpResponse.json({ ...BUSINESS_PROFILE, paymentDetails: null }),
      ),
    );

    renderSettingsPage();

    await waitFor(() => expect(screen.queryByText("Loading…")).not.toBeInTheDocument());
    expect(screen.getByLabelText(/default payment details/i)).toHaveValue("");
  });

  it("saves the edited value via PUT /api/business/payment-details and shows a success message", async () => {
    server.use(http.get("*/api/business", () => HttpResponse.json(BUSINESS_PROFILE)));
    let capturedBody: unknown = null;
    server.use(
      http.put("*/api/business/payment-details", async ({ request }) => {
        capturedBody = await request.json();
        return HttpResponse.json({
          ...BUSINESS_PROFILE,
          paymentDetails: "Pay via Venmo @acme-business",
        });
      }),
    );

    renderSettingsPage();
    const user = userEvent.setup();

    const field = await screen.findByDisplayValue(
      "Pay by bank transfer to Acme Business, acct 12345.",
    );
    await user.clear(field);
    await user.type(field, "Pay via Venmo @acme-business");
    await user.click(screen.getByRole("button", { name: /save/i }));

    expect(await screen.findByText("Payment details saved.")).toBeInTheDocument();
    expect(capturedBody).toEqual({ paymentDetails: "Pay via Venmo @acme-business" });
  });

  it("sends null when the field is cleared entirely", async () => {
    server.use(http.get("*/api/business", () => HttpResponse.json(BUSINESS_PROFILE)));
    let capturedBody: unknown = null;
    server.use(
      http.put("*/api/business/payment-details", async ({ request }) => {
        capturedBody = await request.json();
        return HttpResponse.json({ ...BUSINESS_PROFILE, paymentDetails: null });
      }),
    );

    renderSettingsPage();
    const user = userEvent.setup();

    const field = await screen.findByDisplayValue(
      "Pay by bank transfer to Acme Business, acct 12345.",
    );
    await user.clear(field);
    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => expect(capturedBody).toEqual({ paymentDetails: null }));
  });

  it("shows an error message and does not clear the field on a failed save", async () => {
    server.use(http.get("*/api/business", () => HttpResponse.json(BUSINESS_PROFILE)));
    server.use(
      http.put("*/api/business/payment-details", () =>
        HttpResponse.json({ title: "Unable to save payment details." }, { status: 400 }),
      ),
    );

    renderSettingsPage();
    const user = userEvent.setup();

    const field = await screen.findByDisplayValue(
      "Pay by bank transfer to Acme Business, acct 12345.",
    );
    await user.click(screen.getByRole("button", { name: /save/i }));

    expect(await screen.findByText("Unable to save payment details.")).toBeInTheDocument();
    expect(field).toHaveValue("Pay by bank transfer to Acme Business, acct 12345.");
  });

  it("shows an error state when GET /api/business fails", async () => {
    server.use(
      http.get("*/api/business", () => HttpResponse.json({ title: "Server error" }, { status: 500 })),
    );

    renderSettingsPage();

    expect(await screen.findByText("Unable to load business settings")).toBeInTheDocument();
  });
});
