import { z } from "zod";

// Mirrors `CreateClientCommandValidator`/`UpdateClientCommandValidator` (identical rules for
// both): Name required max 200; Email required, valid format, max 256; Phone optional max 50;
// Address optional max 500. `phone`/`address` default to "" rather than being `undefined`-typed
// — sending "" to the backend is equivalent to omitting them, per the handler's own
// `?? string.Empty` coalescing, and it keeps RHF's default-values story simple (controlled
// inputs, no undefined-vs-empty-string juggling).
export const clientSchema = z.object({
  name: z
    .string()
    .min(1, "Name is required.")
    .max(200, "Name must be 200 characters or fewer."),
  email: z
    .string()
    .min(1, "Email is required.")
    .email("Enter a valid email address.")
    .max(256, "Email must be 256 characters or fewer."),
  phone: z.string().max(50, "Phone must be 50 characters or fewer."),
  address: z.string().max(500, "Address must be 500 characters or fewer."),
});
export type ClientFormValues = z.infer<typeof clientSchema>;
