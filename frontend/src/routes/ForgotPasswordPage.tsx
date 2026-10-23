import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Link } from "react-router-dom";
import * as authApi from "@/api/authApi";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { applyApiErrorToForm } from "@/lib/apiErrors";
import { forgotPasswordSchema, type ForgotPasswordFormValues } from "@/schemas/authSchemas";

// Maps the backend ForgotPasswordCommandValidator's PascalCase property names to this form's
// fields.
const FIELD_MAP = { Email: "email" } as const;

export function ForgotPasswordPage() {
  const [formError, setFormError] = useState<string | null>(null);
  // The server's exact (deliberately non-committal) success message — shown inline, never a
  // redirect, since there's nowhere meaningful to redirect to from here.
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const form = useForm<ForgotPasswordFormValues>({
    resolver: zodResolver(forgotPasswordSchema),
    defaultValues: { email: "" },
  });

  const onSubmit = async (values: ForgotPasswordFormValues) => {
    setFormError(null);
    setSuccessMessage(null);
    setIsSubmitting(true);
    try {
      const result = await authApi.forgotPassword(values.email);
      setSuccessMessage(result.message);
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
          <h1 className="text-2xl font-semibold">Forgot password</h1>
          <p className="text-sm text-muted-foreground">
            Enter your account email and we&apos;ll send a reset link if it matches an account.
          </p>
        </div>

        {successMessage && (
          <Alert>
            <AlertTitle>Request received</AlertTitle>
            <AlertDescription>{successMessage}</AlertDescription>
          </Alert>
        )}

        {formError && (
          <Alert variant="destructive">
            <AlertTitle>Something went wrong</AlertTitle>
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

          <Button type="submit" className="h-11 w-full md:h-8" disabled={isSubmitting}>
            {isSubmitting ? "Sending…" : "Send reset link"}
          </Button>
        </form>

        <div className="flex flex-col items-center gap-1 text-sm text-muted-foreground">
          <Link to="/login" className="hover:text-foreground">
            Back to log in
          </Link>
        </div>
      </div>
    </div>
  );
}
