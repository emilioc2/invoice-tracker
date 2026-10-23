import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AuthProvider } from "@/context/AuthContext";
import { ChangePasswordPage } from "@/routes/ChangePasswordPage";
import { server } from "@/test/server";
import { createTestQueryClient, seedAuthenticatedSession } from "@/test/test-utils";

// Exercises requirements.md's "Forced password change for temp-password logins" flow at the
// component level: a successful POST /api/auth/change-password must clear `mustChangePassword`
// and navigate to /dashboard; a failed one must not do either and must surface an error.

function renderChangePasswordPage() {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={["/change-password"]}>
          <Routes>
            <Route path="/change-password" element={<ChangePasswordPage />} />
            <Route path="/dashboard" element={<div>Dashboard page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>, newPassword: string) {
  await user.type(screen.getByLabelText(/new password/i), newPassword);
  await user.click(screen.getByRole("button", { name: /set new password/i }));
}

describe("ChangePasswordPage", () => {
  beforeEach(() => {
    seedAuthenticatedSession({
      userId: "user-1",
      businessId: "business-1",
      email: "staff@example.com",
      role: "Staff",
      mustChangePassword: true,
    });
  });

  it("on success, calls the API with only the new password, clears the flag, and navigates to /dashboard", async () => {
    let capturedBody: unknown = null;
    server.use(
      http.post("*/api/auth/change-password", async ({ request }) => {
        capturedBody = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderChangePasswordPage();
    const user = userEvent.setup();

    await fillAndSubmit(user, "NewStrongPass123");

    await waitFor(() => expect(screen.getByText("Dashboard page")).toBeInTheDocument());

    expect(capturedBody).toEqual({
      newPassword: "NewStrongPass123",
    });
    const storedUser = JSON.parse(localStorage.getItem("invoice-tracker.user") ?? "null");
    expect(storedUser?.mustChangePassword).toBe(false);
  });

  it("on a failed request, shows an error and does not navigate or clear the flag", async () => {
    server.use(
      http.post("*/api/auth/change-password", () =>
        HttpResponse.json({ title: "Unable to change password." }, { status: 400 }),
      ),
    );

    renderChangePasswordPage();
    const user = userEvent.setup();

    await fillAndSubmit(user, "NewStrongPass123");

    expect(await screen.findByText("Unable to change password.")).toBeInTheDocument();
    expect(screen.queryByText("Dashboard page")).not.toBeInTheDocument();

    const storedUser = JSON.parse(localStorage.getItem("invoice-tracker.user") ?? "null");
    expect(storedUser?.mustChangePassword).toBe(true);
  });
});
