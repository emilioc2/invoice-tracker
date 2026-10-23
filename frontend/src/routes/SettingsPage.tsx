import { zodResolver } from "@hookform/resolvers/zod";
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useBusinessProfileQuery, useUpdatePaymentDetailsMutation } from "@/hooks/useBusiness";
import { applyApiErrorToForm } from "@/lib/apiErrors";
import { paymentDetailsSchema, type PaymentDetailsFormValues } from "@/schemas/businessSchema";

// Maps the backend UpdatePaymentDetailsCommandValidator's PascalCase property name to this
// form's field — same pattern as `ClientForm`'s FIELD_MAP. There's no documented validation rule
// for this field, so this only matters if the backend ever returns an unexpected field error.
const FIELD_MAP = { PaymentDetails: "paymentDetails" } as const;

/**
 * Business-wide default payment details (requirements.md's "Business payment details" section) —
 * reachable by any authenticated user (Owner or Staff), not role-filtered, unlike `UsersPage`.
 * Fetches the current value via `GET /api/business` and saves it via
 * `PUT /api/business/payment-details`.
 */
export function SettingsPage() {
  const [formError, setFormError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const { data: business, isLoading, isError } = useBusinessProfileQuery();
  const updateMutation = useUpdatePaymentDetailsMutation();

  const form = useForm<PaymentDetailsFormValues>({
    resolver: zodResolver(paymentDetailsSchema),
    defaultValues: { paymentDetails: "" },
  });

  // The business profile loads asynchronously, so the form is reset once it arrives rather than
  // relying on `defaultValues`, which only applies at mount — same pattern as `InvoiceFormPage`'s
  // edit-mode reset.
  useEffect(() => {
    if (business) {
      form.reset({ paymentDetails: business.paymentDetails ?? "" });
    }
  }, [business, form]);

  const handleSubmit = async (values: PaymentDetailsFormValues) => {
    setFormError(null);
    setSuccessMessage(null);
    try {
      await updateMutation.mutateAsync(values.paymentDetails || null);
      setSuccessMessage("Payment details saved.");
    } catch (error) {
      setFormError(applyApiErrorToForm(error, form, FIELD_MAP));
    }
  };

  return (
    <div className="space-y-6">
      <h1 className="font-display text-2xl font-semibold">Settings</h1>

      {isLoading && <p className="text-sm text-muted-foreground">Loading…</p>}

      {isError && (
        <Alert variant="destructive">
          <AlertTitle>Unable to load business settings</AlertTitle>
          <AlertDescription>Something went wrong. Please try again.</AlertDescription>
        </Alert>
      )}

      {!isLoading && !isError && (
        <form
          className="max-w-2xl space-y-5 md:space-y-4"
          onSubmit={form.handleSubmit(handleSubmit)}
          noValidate
        >
          {formError && (
            <Alert variant="destructive">
              <AlertTitle>Unable to save payment details</AlertTitle>
              <AlertDescription>{formError}</AlertDescription>
            </Alert>
          )}

          {successMessage && (
            <Alert>
              <AlertTitle>Success</AlertTitle>
              <AlertDescription>{successMessage}</AlertDescription>
            </Alert>
          )}

          <div className="space-y-1.5">
            <Label htmlFor="business-payment-details">Default payment details</Label>
            <Textarea
              id="business-payment-details"
              rows={5}
              aria-invalid={!!form.formState.errors.paymentDetails}
              aria-describedby="business-payment-details-help"
              {...form.register("paymentDetails")}
            />
            <p id="business-payment-details-help" className="text-xs text-muted-foreground">
              Shown on invoices and included in the invoice email — e.g. bank account details or a
              payment link. Used as the starting value for new invoices, which can override it
              before sending; changing this default here does not affect invoices already created.
            </p>
            {form.formState.errors.paymentDetails && (
              <p className="text-sm text-destructive">
                {form.formState.errors.paymentDetails.message}
              </p>
            )}
          </div>

          <div className="flex justify-end">
            <Button type="submit" className="h-11 md:h-8" disabled={updateMutation.isPending}>
              {updateMutation.isPending ? "Saving…" : "Save"}
            </Button>
          </div>
        </form>
      )}
    </div>
  );
}
