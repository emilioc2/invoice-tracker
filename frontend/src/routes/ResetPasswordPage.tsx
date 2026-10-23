import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import * as authApi from "@/api/authApi";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { applyApiErrorToForm } from "@/lib/apiErrors";
import { resetPasswordSchema, type ResetPasswordFormValues } from "@/schemas/authSchemas";

// Maps the backend ResetPasswordCommandValidator's PascalCase property names to this form's
// fields.
const FIELD_MAP = { Email: "email", Token: "token", NewPassword: "newPassword" } as const;

export function ResetPasswordPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const [formError, setFormError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Pre-fill from `?email=&token=` for when a real reset link exists later, but both stay fully
  // editable — no real email is ever sent today (forgot/reset-password use a no-op IEmailSender),
  // so pulling the token straight out of the dev database and pasting it in is the only way this
  // flow is actually testable right now.
  const form = useForm<ResetPasswordFormValues>({
    resolver: zodResolver(resetPasswordSchema),
    defaultValues: {
      email: searchParams.get("email") ?? "",
      token: searchParams.get("token") ?? "",
      newPassword: "",
    },
  });

  const onSubmit = async (values: ResetPasswordFormValues) => {
    setFormError(null);
    setIsSubmitting(true);
    try {
      await authApi.resetPassword(values.email, values.token, values.newPassword);
      // No token is returned from this endpoint (unlike login/register), so there's nothing to
      // log in with — send the user to /login instead, with a flag it can show feedback for.
      navigate("/login", { replace: true, state: { resetSuccess: true } });
    } catch (error) {
      setFormError(applyApiErrorToForm(error, form, FIELD_MAP));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-background text-foreground">
      <div className="w-full max-w-sm space-y-6">
        <div className="space-y-2 text-center">
          <h1 className="text-2xl font-semibold">Reset password</h1>
          <p className="text-sm text-muted-foreground">
            Enter your email, reset token, and a new password.
          </p>
        </div>

        {formError && (
          <Alert variant="destructive">
            <AlertTitle>Unable to reset password</AlertTitle>
            <AlertDescription>{formError}</AlertDescription>
          </Alert>
        )}

        <form className="space-y-5 md:space-y-4" onSubmit={form.handleSubmit(onSubmit)} noValidate>
          <div className="space-y-1.5">
            <Label htmlFor="email">Email</Label>
            <Input
              id="email"
              className="h-11 md:h-8"
              type="email"
              autoComplete="email"
              aria-invalid={!!form.formState.errors.email}
              aria-describedby={form.formState.errors.email ? "email-error" : undefined}
              {...form.register("email")}
            />
            {form.formState.errors.email && (
              <p id="email-error" className="text-sm text-destructive">
                {form.formState.errors.email.message}
              </p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="token">Reset token</Label>
            <Input
              id="token"
              className="h-11 md:h-8"
              autoComplete="off"
              aria-invalid={!!form.formState.errors.token}
              aria-describedby={form.formState.errors.token ? "token-error" : undefined}
              {...form.register("token")}
            />
            {form.formState.errors.token && (
              <p id="token-error" className="text-sm text-destructive">
                {form.formState.errors.token.message}
              </p>
            )}
          </div>

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
