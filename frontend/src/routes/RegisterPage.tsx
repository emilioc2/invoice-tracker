import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useAuth } from "@/hooks/useAuth";
import { applyApiErrorToForm } from "@/lib/apiErrors";
import { registerSchema, type RegisterFormValues } from "@/schemas/authSchemas";

// Maps the backend RegisterCommandValidator's PascalCase property names to this form's fields.
const FIELD_MAP = {
  BusinessName: "businessName",
  BusinessEmail: "businessEmail",
  OwnerEmail: "ownerEmail",
  Password: "password",
} as const;

interface LocationState {
  from?: { pathname?: string };
}

export function RegisterPage() {
  const { register: registerBusiness } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const state = location.state as LocationState | null;

  const [formError, setFormError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const form = useForm<RegisterFormValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: { businessName: "", businessEmail: "", ownerEmail: "", password: "" },
  });

  const onSubmit = async (values: RegisterFormValues) => {
    setFormError(null);
    setIsSubmitting(true);
    try {
      await registerBusiness(
        values.businessName,
        values.businessEmail,
        values.ownerEmail,
        values.password,
      );
      navigate(state?.from?.pathname ?? "/dashboard", { replace: true });
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
          <h1 className="text-2xl font-semibold">Register your business</h1>
          <p className="text-sm text-muted-foreground">
            Create a business account — you&apos;ll be its Admin.
          </p>
        </div>

        {formError && (
          <Alert variant="destructive">
            <AlertTitle>Unable to register</AlertTitle>
            <AlertDescription>{formError}</AlertDescription>
          </Alert>
        )}

        <form className="space-y-4" onSubmit={form.handleSubmit(onSubmit)} noValidate>
          <div className="space-y-1.5">
            <Label htmlFor="businessName">Business name</Label>
            <Input
              id="businessName"
              autoComplete="organization"
              aria-invalid={!!form.formState.errors.businessName}
              aria-describedby={
                form.formState.errors.businessName ? "businessName-error" : undefined
              }
              {...form.register("businessName")}
            />
            {form.formState.errors.businessName && (
              <p id="businessName-error" className="text-sm text-destructive">
                {form.formState.errors.businessName.message}
              </p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="businessEmail">Business contact email</Label>
            <Input
              id="businessEmail"
              type="email"
              autoComplete="email"
              aria-invalid={!!form.formState.errors.businessEmail}
              aria-describedby={
                form.formState.errors.businessEmail ? "businessEmail-error" : undefined
              }
              {...form.register("businessEmail")}
            />
            {form.formState.errors.businessEmail && (
              <p id="businessEmail-error" className="text-sm text-destructive">
                {form.formState.errors.businessEmail.message}
              </p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="ownerEmail">Your (Admin) login email</Label>
            <Input
              id="ownerEmail"
              type="email"
              autoComplete="email"
              aria-invalid={!!form.formState.errors.ownerEmail}
              aria-describedby={form.formState.errors.ownerEmail ? "ownerEmail-error" : undefined}
              {...form.register("ownerEmail")}
            />
            {form.formState.errors.ownerEmail && (
              <p id="ownerEmail-error" className="text-sm text-destructive">
                {form.formState.errors.ownerEmail.message}
              </p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="password">Password</Label>
            <Input
              id="password"
              type="password"
              autoComplete="new-password"
              aria-invalid={!!form.formState.errors.password}
              aria-describedby={form.formState.errors.password ? "password-error" : undefined}
              {...form.register("password")}
            />
            {form.formState.errors.password ? (
              <p id="password-error" className="text-sm text-destructive">
                {form.formState.errors.password.message}
              </p>
            ) : (
              <p className="text-xs text-muted-foreground">
                At least 8 characters, with an uppercase letter, a lowercase letter, and a digit.
              </p>
            )}
          </div>

          <Button type="submit" className="w-full" disabled={isSubmitting}>
            {isSubmitting ? "Registering…" : "Register"}
          </Button>
        </form>

        <p className="text-center text-sm text-muted-foreground">
          Already have an account?{" "}
          <Link to="/login" className="font-medium text-foreground hover:underline">
            Log in
          </Link>
        </p>
      </div>
    </div>
  );
}
