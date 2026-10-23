import { zodResolver } from "@hookform/resolvers/zod";
import { useEffect, useState } from "react";
import { useForm, useWatch, type Path } from "react-hook-form";
import { useNavigate, useParams } from "react-router-dom";
import { ApiError } from "@/api/client";
import { InvoiceLineItemsEditor } from "@/components/invoices/InvoiceLineItemsEditor";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { useBusinessProfileQuery } from "@/hooks/useBusiness";
import { useClientsQuery } from "@/hooks/useClients";
import { useCreateInvoiceMutation, useInvoiceQuery, useUpdateInvoiceMutation } from "@/hooks/useInvoices";
import { applyApiErrorToForm } from "@/lib/apiErrors";
import { dateOnlyToUtcIso } from "@/lib/utils";
import { invoiceSchema, type InvoiceFormInput, type InvoiceFormValues } from "@/schemas/invoiceSchema";
import type { InvoiceInput } from "@/types/invoice";

// Maps the backend CreateInvoiceCommandValidator/UpdateInvoiceCommandValidator's PascalCase
// property names to this form's fields — both validators use identical property names. The
// nested LineItems[i].Field keys are generated dynamically since the index count varies.
const BASE_FIELD_MAP = {
  ClientId: "clientId",
  DueDate: "dueDate",
  TaxRate: "taxRatePercent",
  Notes: "notes",
  PaymentDetails: "paymentDetails",
} as const;

function buildFieldMap(lineItemCount: number): Record<string, Path<InvoiceFormInput>> {
  const map: Record<string, Path<InvoiceFormInput>> = { ...BASE_FIELD_MAP };
  for (let index = 0; index < lineItemCount; index += 1) {
    map[`LineItems[${index}].Description`] = `lineItems.${index}.description` as const;
    map[`LineItems[${index}].Quantity`] = `lineItems.${index}.quantity` as const;
    map[`LineItems[${index}].UnitPrice`] = `lineItems.${index}.unitPrice` as const;
  }
  return map;
}

// Capped at the backend's ListClientsQueryValidator max (InclusiveBetween(1, 100)) — requesting
// more than this 400s. This is still just one page, not a guarantee every active client fits: the
// real fix for a business with >100 active clients is a searchable/combobox-style client select,
// not a bigger fixed page size. The existing shadcn select.tsx primitive (plain Radix Select) has
// no built-in search/combobox behavior, so that's left as follow-up work rather than done here.
const CLIENT_SELECT_PAGE_SIZE = 100;

const EMPTY_DEFAULTS: InvoiceFormInput = {
  clientId: "",
  dueDate: "",
  taxRatePercent: 0,
  notes: "",
  paymentDetails: "",
  lineItems: [{ description: "", quantity: 1, unitPrice: 0 }],
};

export function InvoiceFormPage() {
  const { invoiceId } = useParams<{ invoiceId: string }>();
  const isEditMode = !!invoiceId;
  const navigate = useNavigate();
  const [formError, setFormError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const { data: invoice, isLoading: isInvoiceLoading, error: invoiceError } = useInvoiceQuery(invoiceId);
  const {
    data: clients,
    isError: isClientsError,
  } = useClientsQuery({ pageNumber: 1, pageSize: CLIENT_SELECT_PAGE_SIZE });

  const createMutation = useCreateInvoiceMutation();
  const updateMutation = useUpdateInvoiceMutation(invoiceId ?? "");
  // Only needed in create mode, to pre-fill `paymentDetails` below — edit mode pre-fills from the
  // invoice's own value instead, so there's no reason to fetch the business default there.
  const { data: business } = useBusinessProfileQuery(!isEditMode);

  const form = useForm<InvoiceFormInput, unknown, InvoiceFormValues>({
    resolver: zodResolver(invoiceSchema),
    defaultValues: EMPTY_DEFAULTS,
  });
  const selectedClientId = useWatch({ control: form.control, name: "clientId" });

  // The invoice loads asynchronously in edit mode, so the form is reset once it arrives rather
  // than relying on `defaultValues`, which only applies at mount.
  useEffect(() => {
    if (isEditMode && invoice) {
      form.reset({
        clientId: invoice.clientId,
        dueDate: invoice.dueDate.slice(0, 10),
        // Rounded to avoid floating-point noise (e.g. 0.29 * 100 === 28.999999999999996).
        taxRatePercent: Math.round(invoice.taxRate * 10000) / 100,
        notes: invoice.notes ?? "",
        // Edit mode pre-fills from the invoice's own current value, never the business default —
        // see the create-mode pre-fill effect below for that case.
        paymentDetails: invoice.paymentDetails ?? "",
        lineItems: invoice.lineItems.map((item) => ({
          description: item.description,
          quantity: item.quantity,
          unitPrice: item.unitPrice,
        })),
      });
    }
  }, [isEditMode, invoice, form]);

  // Create mode only: pre-fills `paymentDetails` with the business's current default once it
  // loads, as a starting point the user can edit/clear before submitting — the backend snapshots
  // whatever value is actually submitted (architecture.md's "Business payment details" section),
  // so this is just a normal editable field that happens to start pre-filled. `setValue` (unlike
  // `reset`) doesn't mark the field dirty, and the `dirtyFields` guard here means this never
  // clobbers something the user already typed, even if the query refetches later.
  useEffect(() => {
    if (!isEditMode && business && !form.formState.dirtyFields.paymentDetails) {
      form.setValue("paymentDetails", business.paymentDetails ?? "");
    }
  }, [isEditMode, business, form]);

  if (isEditMode && isInvoiceLoading) {
    return <p className="text-sm text-muted-foreground">Loading…</p>;
  }

  if (isEditMode && invoiceError instanceof ApiError && invoiceError.status === 404) {
    return (
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold">Invoice not found</h1>
        <p className="text-sm text-muted-foreground">
          This invoice doesn&apos;t exist, or it belongs to another business.
        </p>
        <Button variant="outline" className="h-11 md:h-8" onClick={() => navigate("/invoices")}>
          Back to invoices
        </Button>
      </div>
    );
  }

  if (isEditMode && (invoiceError || !invoice)) {
    return <p className="text-sm text-destructive">Something went wrong loading this invoice.</p>;
  }

  // Edit lock: `PUT /api/invoices/{id}` only works while Status = Draft. Rather than let the
  // user fill out a form that's guaranteed to fail on submit, show a clear read-only notice.
  if (isEditMode && invoice && invoice.status !== "Draft") {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold">{invoice.invoiceNumber}</h1>
        <Alert>
          <AlertTitle>This invoice can no longer be edited</AlertTitle>
          <AlertDescription>
            This invoice has already been sent and can no longer be edited — cancel and create a
            new one instead.
          </AlertDescription>
        </Alert>
        <Button
          variant="outline"
          className="h-11 md:h-8"
          onClick={() => navigate(`/invoices/${invoice.id}`)}
        >
          Back to invoice
        </Button>
      </div>
    );
  }

  const handleSubmit = async (values: InvoiceFormValues) => {
    setFormError(null);
    setIsSubmitting(true);
    try {
      const input: InvoiceInput = {
        clientId: values.clientId,
        // See `dateOnlyToUtcIso`'s doc comment: a bare date-only string 500s against the
        // backend's `timestamp with time zone` columns, so this always sends a full UTC instant.
        dueDate: dateOnlyToUtcIso(values.dueDate),
        taxRate: values.taxRatePercent / 100,
        notes: values.notes,
        paymentDetails: values.paymentDetails,
        lineItems: values.lineItems.map((item) => ({
          description: item.description,
          quantity: item.quantity,
          unitPrice: item.unitPrice,
        })),
      };

      if (isEditMode && invoiceId) {
        const result = await updateMutation.mutateAsync(input);
        navigate(`/invoices/${result.id}`);
      } else {
        const result = await createMutation.mutateAsync(input);
        navigate(`/invoices/${result.id}`);
      }
    } catch (error) {
      setFormError(applyApiErrorToForm(error, form, buildFieldMap(values.lineItems.length)));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">{isEditMode ? "Edit invoice" : "New invoice"}</h1>

      <form className="space-y-6" onSubmit={form.handleSubmit(handleSubmit)} noValidate>
        {formError && (
          <Alert variant="destructive">
            <AlertTitle>Unable to save invoice</AlertTitle>
            <AlertDescription>{formError}</AlertDescription>
          </Alert>
        )}

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor="invoice-client">Client</Label>
            <Select
              value={selectedClientId}
              onValueChange={(next) => form.setValue("clientId", next, { shouldValidate: true })}
              disabled={isClientsError}
            >
              <SelectTrigger
                id="invoice-client"
                // 44px-minimum touch target below the ~768px breakpoint; unchanged
                // (data-[size=default]:h-8) at md+. Written against the same `data-[size=...]`
                // selector the base `select.tsx` primitive uses so the override reliably wins.
                className="w-full data-[size=default]:h-11 md:data-[size=default]:h-8"
                aria-invalid={!!form.formState.errors.clientId || isClientsError}
                aria-describedby={
                  form.formState.errors.clientId
                    ? "invoice-client-error"
                    : isClientsError
                      ? "invoice-client-load-error"
                      : undefined
                }
              >
                <SelectValue placeholder="Select a client" />
              </SelectTrigger>
              <SelectContent>
                {clients?.items.map((client) => (
                  <SelectItem key={client.id} value={client.id}>
                    {client.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {isClientsError && (
              <p id="invoice-client-load-error" className="text-sm text-destructive">
                Couldn&apos;t load clients.
              </p>
            )}
            {form.formState.errors.clientId && (
              <p id="invoice-client-error" className="text-sm text-destructive">
                {form.formState.errors.clientId.message}
              </p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="invoice-due-date">Due date</Label>
            <Input
              id="invoice-due-date"
              className="h-11 md:h-8"
              type="date"
              aria-invalid={!!form.formState.errors.dueDate}
              aria-describedby={form.formState.errors.dueDate ? "invoice-due-date-error" : undefined}
              {...form.register("dueDate")}
            />
            {form.formState.errors.dueDate && (
              <p id="invoice-due-date-error" className="text-sm text-destructive">
                {form.formState.errors.dueDate.message}
              </p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="invoice-tax-rate">Tax rate (%)</Label>
            <Input
              id="invoice-tax-rate"
              className="h-11 md:h-8"
              type="number"
              step="any"
              min={0}
              max={100}
              aria-invalid={!!form.formState.errors.taxRatePercent}
              aria-describedby={
                form.formState.errors.taxRatePercent ? "invoice-tax-rate-error" : undefined
              }
              {...form.register("taxRatePercent")}
            />
            {form.formState.errors.taxRatePercent && (
              <p id="invoice-tax-rate-error" className="text-sm text-destructive">
                {form.formState.errors.taxRatePercent.message}
              </p>
            )}
          </div>
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="invoice-notes">Notes</Label>
          <Textarea
            id="invoice-notes"
            aria-invalid={!!form.formState.errors.notes}
            aria-describedby={form.formState.errors.notes ? "invoice-notes-error" : undefined}
            {...form.register("notes")}
          />
          {form.formState.errors.notes && (
            <p id="invoice-notes-error" className="text-sm text-destructive">
              {form.formState.errors.notes.message}
            </p>
          )}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="invoice-payment-details">Payment details</Label>
          <Textarea
            id="invoice-payment-details"
            aria-invalid={!!form.formState.errors.paymentDetails}
            aria-describedby="invoice-payment-details-help"
            {...form.register("paymentDetails")}
          />
          <p id="invoice-payment-details-help" className="text-xs text-muted-foreground">
            {isEditMode
              ? "Shown on the invoice PDF and in the invoice email, if set."
              : "Pre-filled from your business's default payment details — edit or clear it for just this invoice."}
          </p>
          {form.formState.errors.paymentDetails && (
            <p className="text-sm text-destructive">
              {form.formState.errors.paymentDetails.message}
            </p>
          )}
        </div>

        <InvoiceLineItemsEditor
          control={form.control}
          register={form.register}
          errors={form.formState.errors}
        />

        <div className="flex justify-end gap-3 md:gap-2">
          <Button
            type="button"
            variant="outline"
            className="h-11 md:h-8"
            onClick={() => navigate(isEditMode && invoiceId ? `/invoices/${invoiceId}` : "/invoices")}
            disabled={isSubmitting}
          >
            Cancel
          </Button>
          <Button type="submit" className="h-11 md:h-8" disabled={isSubmitting}>
            {isSubmitting ? "Saving…" : isEditMode ? "Save changes" : "Create invoice"}
          </Button>
        </div>
      </form>
    </div>
  );
}
