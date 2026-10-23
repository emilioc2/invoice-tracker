import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import type { z } from "zod";
import * as authApi from "@/api/authApi";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { applyApiErrorToForm } from "@/lib/apiErrors";
import { resetPasswordSchema } from "@/schemas/authSchemas";

// The rendered/registered form only ever carries `newPassword` — `email`/`token` are never a
// react-hook-form field (not even an unrendered one), so they can't end up in the DOM as a hidden
// input's `value` (#21). They're read straight from the link's query params instead and merged
// into the submitted payload in `onSubmit`, validated against `resetPasswordSchema`'s own
// email/token rules there rather than through RHF's resolver.
const resetPasswordFormSchema = resetPasswordSchema.pick({ newPassword: true });
type ResetPasswordFormValues = z.infer<typeof resetPasswordFormSchema>;

// Maps the backend ResetPasswordCommandValidator's PascalCase property names to this form's
// fields. Only `NewPassword` has a matching rendered field to anchor to — a server-side
// Email/Token error (e.g. "invalid or expired token") has no field left to attach to, so it falls
// through `applyApiErrorToForm`'s "unmapped" path and surfaces as the general error banner below.
const FIELD_MAP = { NewPassword: "newPassword" } as const;

export function ResetPasswordPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const [formError, setFormError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Email/token come only from the `?email=&token=` link ForgotPasswordCommandHandler emails via
  // Resend (see #20) — the token is never something the user is expected to see or type (#21), so
  // there's no manual-entry fallback: if either is missing, we show a message pointing back to
  // /forgot-password instead of rendering the form at all.
  const emailFromLink = searchParams.get("email") ?? "";
  const tokenFromLink = searchParams.get("token") ?? "";
  const hasResetLink = emailFromLink !== "" && tokenFromLink !== "";

  const form = useForm<ResetPasswordFormValues>({
    resolver: zodResolver(resetPasswordFormSchema),
    defaultValues: {
      newPassword: "",
    },
  });

  const onSubmit = async (values: ResetPasswordFormValues) => {
    setFormError(null);

    // Email/token never passed through RHF, so validate them here against the same rules
    // `resetPasswordSchema` would have applied, before they're sent.
    const linkFields = resetPasswordSchema
      .pick({ email: true, token: true })
      .safeParse({ email: emailFromLink, token: tokenFromLink });
    if (!linkFields.success) {
      setFormError("This reset link is invalid. Request a new one from the forgot-password page.");
      return;
    }

    setIsSubmitting(true);
    try {
      await authApi.resetPassword(
        linkFields.data.email,
        linkFields.data.token,
        values.newPassword,
      );
      // No token is returned from this endpoint (unlike login/register), so there's nothing to
      // log in with — send the user to /login instead, with a flag it can show feedback for.
      navigate("/login", { replace: true, state: { resetSuccess: true } });
    } catch (error) {
      setFormError(applyApiErrorToForm(error, form, FIELD_MAP));
    } finally {
      setIsSubmitting(false);
    }
  };

  if (!hasResetLink) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-background text-foreground">
        <div className="w-full max-w-sm space-y-6">
          <div className="space-y-2 text-center">
            <h1 className="text-2xl font-semibold">Reset password</h1>
          </div>

          <Alert>
            <AlertTitle>Use the link from your email</AlertTitle>
            <AlertDescription>
              This page only works when opened from the reset-password link we emailed you.
              Request a new one from the forgot-password page.
            </AlertDescription>
          </Alert>

          <p className="text-center text-sm text-muted-foreground">
            <Link to="/forgot-password" className="hover:text-foreground">
              Back to forgot password
            </Link>
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-background text-foreground">
      <div className="w-full max-w-sm space-y-6">
        <div className="space-y-2 text-center">
          <h1 className="text-2xl font-semibold">Reset password</h1>
          <p className="text-sm text-muted-foreground">Enter a new password for your account.</p>
        </div>

        {formError && (
          <Alert variant="destructive">
            <AlertTitle>Unable to reset password</AlertTitle>
            <AlertDescription>{formError}</AlertDescription>
          </Alert>
        )}

        <form className="space-y-5 md:space-y-4" onSubmit={form.handleSubmit(onSubmit)} noValidate>
          {/* Email/token are read from the link's query params in `onSubmit` above — they are
              never a react-hook-form field and never rendered as any DOM element, hidden or
              otherwise (#21). */}

          <div className="space-y-1.5">
            <Label htmlFor="newPassword">New password</Label>
            <Input
              id="newPassword"
              className="h-11 md:h-8"
              type="password"
              autoComplete="new-password"
              aria-invalid={!!form.formState.errors.newPassword}
              aria-describedby={
                form.formState.errors.newPassword ? "newPassword-error" : undefined
              }
              {...form.register("newPassword")}
            />
            {form.formState.errors.newPassword ? (
              <p id="newPassword-error" className="text-sm text-destructive">
                {form.formState.errors.newPassword.message}
              </p>
            ) : (
              <p className="text-xs text-muted-foreground">
                At least 8 characters, with an uppercase letter, a lowercase letter, and a digit.
              </p>
            )}
          </div>

          <Button type="submit" className="h-11 w-full md:h-8" disabled={isSubmitting}>
            {isSubmitting ? "Resetting…" : "Reset password"}
          </Button>
        </form>

        <p className="text-center text-sm text-muted-foreground">
          <Link to="/login" className="hover:text-foreground">
            Back to log in
          </Link>
        </p>
      </div>
    </div>
  );
}
