import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { useNavigate } from "react-router-dom";
import * as authApi from "@/api/authApi";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useAuth } from "@/hooks/useAuth";
import { applyApiErrorToForm } from "@/lib/apiErrors";
import { changePasswordSchema, type ChangePasswordFormValues } from "@/schemas/authSchemas";

// Maps the backend ChangePasswordCommandValidator's PascalCase property names to this form's
// fields.
const FIELD_MAP = { NewPassword: "newPassword" } as const;

/**
 * Forced, blocking interstitial for temp-password logins — reached only via
 * `MustChangePasswordGuard`'s redirect (see `App.tsx`), never linked to directly. There is
 * nowhere else to navigate from here: no cancel/skip/back link, by design (see
 * requirements.md's "Forced password change for temp-password logins").
 */
export function ChangePasswordPage() {
  const { clearMustChangePassword } = useAuth();
  const navigate = useNavigate();

  const [formError, setFormError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const form = useForm<ChangePasswordFormValues>({
    resolver: zodResolver(changePasswordSchema),
    defaultValues: { newPassword: "" },
  });

  const onSubmit = async (values: ChangePasswordFormValues) => {
    setFormError(null);
    setIsSubmitting(true);
    try {
      await authApi.changePassword(values.newPassword);
      // No new JWT is issued (none is needed) — just clear the flag so the guard lets the user
      // through, same session.
      clearMustChangePassword();
      navigate("/dashboard", { replace: true });
    } catch (error) {
      setFormError(applyApiErrorToForm(error, form, FIELD_MAP));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="flex flex-1 items-center justify-center bg-background text-foreground">
      <div className="w-full max-w-sm space-y-6">
        <div className="space-y-2 text-center">
          <h1 className="text-2xl font-semibold">Set a new password</h1>
          <p className="text-sm text-muted-foreground">
            You&apos;re using a temporary password. Please set a new password to continue — you
            won&apos;t be able to access the rest of the app until you do.
          </p>
        </div>

        {formError && (
          <Alert variant="destructive">
            <AlertTitle>Unable to change password</AlertTitle>
            <AlertDescription>{formError}</AlertDescription>
          </Alert>
        )}

        <form className="space-y-5 md:space-y-4" onSubmit={form.handleSubmit(onSubmit)} noValidate>
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
            {isSubmitting ? "Updating…" : "Set new password"}
          </Button>
        </form>
      </div>
    </div>
  );
}
