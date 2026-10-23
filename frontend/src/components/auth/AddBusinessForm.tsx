import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { applyApiErrorToForm } from "@/lib/apiErrors";
import { addBusinessSchema, type AddBusinessFormValues } from "@/schemas/authSchemas";

// Maps the backend AddBusinessCommandValidator's PascalCase property names to this form's fields.
const FIELD_MAP = { BusinessName: "businessName", BusinessEmail: "businessEmail" } as const;

interface AddBusinessFormProps {
  onSubmit: (values: AddBusinessFormValues) => Promise<void>;
  onCancel?: () => void;
}

/** "Add a business" form — used from AppShell's business-switcher dropdown. */
export function AddBusinessForm({ onSubmit, onCancel }: AddBusinessFormProps) {
  const [formError, setFormError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const form = useForm<AddBusinessFormValues>({
    resolver: zodResolver(addBusinessSchema),
    defaultValues: { businessName: "", businessEmail: "" },
  });

  const handleSubmit = async (values: AddBusinessFormValues) => {
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
    <form className="space-y-5 md:space-y-4" onSubmit={form.handleSubmit(handleSubmit)} noValidate>
      {formError && (
        <Alert variant="destructive">
          <AlertTitle>Unable to add business</AlertTitle>
          <AlertDescription>{formError}</AlertDescription>
        </Alert>
      )}

      <div className="space-y-1.5">
        <Label htmlFor="add-business-name">Business name</Label>
        <Input
          id="add-business-name"
          className="h-11 md:h-8"
          autoComplete="organization"
          aria-invalid={!!form.formState.errors.businessName}
          aria-describedby={
            form.formState.errors.businessName ? "add-business-name-error" : undefined
          }
          {...form.register("businessName")}
        />
        {form.formState.errors.businessName && (
          <p id="add-business-name-error" className="text-sm text-destructive">
            {form.formState.errors.businessName.message}
          </p>
        )}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="add-business-email">Business contact email</Label>
        <Input
          id="add-business-email"
          className="h-11 md:h-8"
          type="email"
          autoComplete="email"
          aria-invalid={!!form.formState.errors.businessEmail}
          aria-describedby={
            form.formState.errors.businessEmail ? "add-business-email-error" : undefined
          }
          {...form.register("businessEmail")}
        />
        {form.formState.errors.businessEmail && (
          <p id="add-business-email-error" className="text-sm text-destructive">
            {form.formState.errors.businessEmail.message}
          </p>
        )}
      </div>

      <div className="flex justify-end gap-3 pt-2 md:gap-2">
        {onCancel && (
          <Button
            type="button"
            variant="outline"
            className="h-11 md:h-8"
            onClick={onCancel}
            disabled={isSubmitting}
          >
            Cancel
          </Button>
        )}
        <Button type="submit" className="h-11 md:h-8" disabled={isSubmitting}>
          {isSubmitting ? "Adding…" : "Add business"}
        </Button>
      </div>
    </form>
  );
}
