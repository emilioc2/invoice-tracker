import { z } from "zod";

// Mirrors `CreateInvoiceCommandValidator`/`UpdateInvoiceCommandValidator` (identical rules for
// both, see InvoiceLineItemRequestValidator too): ClientId required; DueDate required; TaxRate in
// [0, 1] inclusive (a fraction, e.g. 0.08 for 8% — NOT a percentage); Notes optional max 2000;
// LineItems non-empty, each with Description required max 500, Quantity strictly > 0, UnitPrice
// >= 0 (zero-price line items are valid, e.g. goodwill/comped items).
export const invoiceLineItemSchema = z.object({
  description: z
    .string()
    .min(1, "Description is required.")
    .max(500, "Description must be 500 characters or fewer."),
  quantity: z.coerce
    .number({ message: "Quantity is required." })
    .gt(0, "Quantity must be greater than 0."),
  unitPrice: z.coerce
    .number({ message: "Unit price is required." })
    .nonnegative("Unit price can't be negative."),
});
export type InvoiceLineItemFormValues = z.infer<typeof invoiceLineItemSchema>;

export const invoiceSchema = z.object({
  clientId: z.string().min(1, "Client is required."),
  dueDate: z.string().min(1, "Due date is required."),
  // The form displays this as a whole-number percentage (e.g. "8" for 8%) and converts to/from
  // the backend's 0-1 fraction at the form boundary — see InvoiceFormPage.
  taxRatePercent: z.coerce
    .number({ message: "Tax rate is required." })
    .min(0, "Tax rate can't be negative.")
    .max(100, "Tax rate can't exceed 100%."),
  notes: z.string().max(2000, "Notes must be 2000 characters or fewer."),
  // `CreateInvoiceCommandValidator`/`UpdateInvoiceCommandValidator` both have
  // `MaximumLength(2000)` on `PaymentDetails`, same limit as `Notes` above.
  paymentDetails: z.string().max(2000, "Payment details must be 2000 characters or fewer."),
  lineItems: z
    .array(invoiceLineItemSchema)
    .min(1, "At least one line item is required."),
});
export type InvoiceFormValues = z.infer<typeof invoiceSchema>;
// The numeric fields use `z.coerce`, so the *input* shape RHF registers against (raw strings
// from text/number inputs, pre-coercion) differs from the *output* shape zodResolver produces
// after validation (actual numbers) — `InvoiceFormValues` above is the output; this is the input,
// wired into `useForm`'s 3-generic-argument form (input, context, transformed-output) so
// `onSubmit` still receives real numbers while registered inputs stay untyped/`unknown` pre-parse.
export type InvoiceFormInput = z.input<typeof invoiceSchema>;
