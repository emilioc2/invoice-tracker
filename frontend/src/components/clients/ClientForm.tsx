import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { applyApiErrorToForm } from "@/lib/apiErrors";
import { clientSchema, type ClientFormValues } from "@/schemas/clientSchema";
import type { Client } from "@/types/client";

// Maps the backend CreateClientCommandValidator/UpdateClientCommandValidator's PascalCase
// property names to this form's fields — both validators use identical property names.
const FIELD_MAP = {
  Name: "name",
  Email: "email",
  Phone: "phone",
  Address: "address",
} as const;

interface ClientFormProps {
  /** Pre-fills the form when editing an existing client; omitted for create mode. */
  defaultValues?: Client;
  /** Parent decides whether this calls create or update — the form itself is mode-agnostic. */
  onSubmit: (values: ClientFormValues) => Promise<void>;
  onCancel?: () => void;
  submitLabel?: string;
}

export function ClientForm({
  defaultValues,
  onSubmit,
  onCancel,
  submitLabel = "Save",
}: ClientFormProps) {
  const [formError, setFormError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const form = useForm<ClientFormValues>({
    resolver: zodResolver(clientSchema),
    defaultValues: {
      name: defaultValues?.name ?? "",
      email: defaultValues?.email ?? "",
      phone: defaultValues?.phone ?? "",
      address: defaultValues?.address ?? "",
    },
  });

  const handleSubmit = async (values: ClientFormValues) => {
    setFormError(null);
    setIsSubmitting(true);
    try {
      await onSubmit(values);
    } catch (error) {
      setFormError(applyApiErrorToForm(error, form, FIELD_MAP));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <form className="space-y-4" onSubmit={form.handleSubmit(handleSubmit)} noValidate>
      {formError && (
        <Alert variant="destructive">
          <AlertTitle>Unable to save client</AlertTitle>
          <AlertDescription>{formError}</AlertDescription>
        </Alert>
      )}

      <div className="space-y-1.5">
        <Label htmlFor="client-name">Name</Label>
        <Input
          id="client-name"
          aria-invalid={!!form.formState.errors.name}
          aria-describedby={form.formState.errors.name ? "client-name-error" : undefined}
          {...form.register("name")}
        />
        {form.formState.errors.name && (
          <p id="client-name-error" className="text-sm text-destructive">
            {form.formState.errors.name.message}
          </p>
        )}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="client-email">Email</Label>
        <Input
          id="client-email"
          type="email"
          aria-invalid={!!form.formState.errors.email}
          aria-describedby={form.formState.errors.email ? "client-email-error" : undefined}
          {...form.register("email")}
        />
        {form.formState.errors.email && (
          <p id="client-email-error" className="text-sm text-destructive">
            {form.formState.errors.email.message}
          </p>
        )}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="client-phone">Phone</Label>
        <Input
          id="client-phone"
          aria-invalid={!!form.formState.errors.phone}
          aria-describedby={form.formState.errors.phone ? "client-phone-error" : undefined}
          {...form.register("phone")}
        />
        {form.formState.errors.phone && (
          <p id="client-phone-error" className="text-sm text-destructive">
            {form.formState.errors.phone.message}
          </p>
        )}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="client-address">Address</Label>
        <Input
          id="client-address"
          aria-invalid={!!form.formState.errors.address}
          aria-describedby={form.formState.errors.address ? "client-address-error" : undefined}
          {...form.register("address")}
        />
        {form.formState.errors.address && (
          <p id="client-address-error" className="text-sm text-destructive">
            {form.formState.errors.address.message}
          </p>
        )}
      </div>

      <div className="flex justify-end gap-2 pt-2">
        {onCancel && (
          <Button type="button" variant="outline" onClick={onCancel} disabled={isSubmitting}>
            Cancel
          </Button>
        )}
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? "Saving…" : submitLabel}
        </Button>
      </div>
    </form>
  );
}
