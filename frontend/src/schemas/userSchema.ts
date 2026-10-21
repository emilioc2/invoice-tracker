import { z } from "zod";

// Mirrors `CreateStaffUserCommandValidator`: Email required, valid format, max 256.
export const createStaffUserSchema = z.object({
  email: z
    .string()
    .min(1, "Email is required.")
    .email("Enter a valid email address.")
    .max(256, "Email must be 256 characters or fewer."),
});
export type CreateStaffUserFormValues = z.infer<typeof createStaffUserSchema>;
