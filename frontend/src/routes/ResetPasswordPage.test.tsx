import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AuthProvider } from "@/context/AuthContext";
import { ResetPasswordPage } from "@/routes/ResetPasswordPage";
import { server } from "@/test/server";
import { clearSession, createTestQueryClient } from "@/test/test-utils";

// user_testing_defects.md item #21: the Identity reset token is link-only — it must never be
// rendered as a visible/editable field, and there must be no manual-token-entry fallback when
// the link's query params are missing.

function renderResetPasswordPage(initialEntry: string) {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={[initialEntry]}>
          <Routes>
            <Route path="/reset-password" element={<ResetPasswordPage />} />
            <Route path="/forgot-password" element={<div>Forgot-password page</div>} />
            <Route path="/login" element={<div>Login page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe("ResetPasswordPage", () => {
  beforeEach(() => {
    clearSession();
  });

  it("with a valid reset link, submits the link's email/token without ever rendering them anywhere in the DOM", async () => {
    let capturedBody: unknown = null;
    server.use(
      http.post("*/api/auth/reset-password", async ({ request }) => {
        capturedBody = await request.json();
        return HttpResponse.json({ message: "Password reset." });
      }),
    );

    const { container } = renderResetPasswordPage(
      "/reset-password?email=owner%40example.com&token=CfDJ8L1A-some-opaque-token",
    );

    // Only a "New password" field is visible — no Email or Token inputs/labels.
    expect(screen.getByLabelText(/new password/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/email/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/token/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/CfDJ8L1A-some-opaque-token/)).not.toBeInTheDocument();

    // The raw token must not appear anywhere in the page's markup at all — not even as a hidden
    // input's `value` attribute, inspectable via DevTools (#21).
    expect(container.innerHTML).not.toContain("CfDJ8L1A-some-opaque-token");
    expect(container.querySelector("input[type='hidden']")).not.toBeInTheDocument();

    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/new password/i), "NewStrongPass123");
    await user.click(screen.getByRole("button", { name: /reset password/i }));

    await waitFor(() => expect(screen.getByText("Login page")).toBeInTheDocument());

    expect(capturedBody).toEqual({
      email: "owner@example.com",
      token: "CfDJ8L1A-some-opaque-token",
      newPassword: "NewStrongPass123",
    });
  });

  it("shows a server-side invalid-token error as a general banner, since there's no field left to anchor it to", async () => {
    server.use(
      http.post("*/api/auth/reset-password", async () => {
        return HttpResponse.json(
          { title: "Reset failed.", errors: ["Invalid token."] },
          { status: 400 },
        );
      }),
    );

    renderResetPasswordPage(
      "/reset-password?email=owner%40example.com&token=CfDJ8L1A-some-opaque-token",
    );

    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/new password/i), "NewStrongPass123");
    await user.click(screen.getByRole("button", { name: /reset password/i }));

    expect(await screen.findByText(/invalid token/i)).toBeInTheDocument();
  });

  it("with no token/email in the URL, shows a message instead of the form and links back to /forgot-password", async () => {
    renderResetPasswordPage("/reset-password");

    expect(screen.queryByLabelText(/new password/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /reset password/i })).not.toBeInTheDocument();
    expect(screen.getByText(/use the link from your email/i)).toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(screen.getByRole("link", { name: /back to forgot password/i }));

    await waitFor(() => expect(screen.getByText("Forgot-password page")).toBeInTheDocument());
  });
});
