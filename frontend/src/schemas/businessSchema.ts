import { z } from "zod";

// Mirrors `UpdatePaymentDetailsCommandValidator`, which does have a `MaximumLength(2000)` rule on
// `PaymentDetails` (architecture.md's prose only mentions the absence of a *format* constraint,
// not length — don't be misled by that into skipping this). The form always submits a string,
// same as `InvoiceFormPage`'s `notes` field — both `null` and `""` render as "nothing" wherever
// this field is read, so the empty-textarea case doesn't need special handling.
export const paymentDetailsSchema = z.object({
  paymentDetails: z.string().max(2000, "Payment details must be 2000 characters or fewer."),
});
export type PaymentDetailsFormValues = z.infer<typeof paymentDetailsSchema>;
