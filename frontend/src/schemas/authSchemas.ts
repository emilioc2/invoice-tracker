import { z } from "zod";

// Mirrors the backend's FluentValidation password rule, used identically by both
// `RegisterCommandValidator` and `ResetPasswordCommandValidator`:
// NotEmpty().MinimumLength(8).Matches("[A-Z]").Matches("[a-z]").Matches("[0-9]")
const passwordSchema = z
  .string()
  .min(1, "Password is required.")
  .min(8, "Password must be at least 8 characters.")
  .regex(/[A-Z]/, "Password must contain at least one uppercase letter.")
  .regex(/[a-z]/, "Password must contain at least one lowercase letter.")
  .regex(/[0-9]/, "Password must contain at least one digit.");

const emailSchema = z
  .string()
  .min(1, "Email is required.")
  .email("Enter a valid email address.");

// Mirrors `LoginCommandValidator`: email must be present + valid, password just non-empty
// (login never re-validates password complexity — that's only enforced when a password is set).
export const loginSchema = z.object({
  email: emailSchema,
  password: z.string().min(1, "Password is required."),
});
export type LoginFormValues = z.infer<typeof loginSchema>;

// Mirrors `RegisterCommandValidator`: BusinessName, BusinessEmail, OwnerEmail, Password.
export const registerSchema = z.object({
  businessName: z
    .string()
    .min(1, "Business name is required.")
    .max(200, "Business name must be 200 characters or fewer."),
  businessEmail: emailSchema.max(256, "Email must be 256 characters or fewer."),
  ownerEmail: emailSchema.max(256, "Email must be 256 characters or fewer."),
  password: passwordSchema,
});
export type RegisterFormValues = z.infer<typeof registerSchema>;

// Mirrors `AddBusinessCommandValidator`: BusinessName, BusinessEmail — the same shape as
// `registerSchema`'s business half, minus any caller-identity fields, since "Add a business"
// reuses the caller's already-authenticated identity rather than creating a new one.
export const addBusinessSchema = z.object({
  businessName: z
    .string()
    .min(1, "Business name is required.")
    .max(200, "Business name must be 200 characters or fewer."),
  businessEmail: emailSchema.max(256, "Email must be 256 characters or fewer."),
});
export type AddBusinessFormValues = z.infer<typeof addBusinessSchema>;

// Mirrors `ForgotPasswordCommandValidator`: Email.
export const forgotPasswordSchema = z.object({
  email: emailSchema,
});
export type ForgotPasswordFormValues = z.infer<typeof forgotPasswordSchema>;

// Mirrors `ResetPasswordCommandValidator`: Email, Token, NewPassword. Token/email are editable
// manually (not just read from a URL) since no real reset email is wired up yet (step 3 used a
// no-op IEmailSender for forgot/reset-password) — the only way to exercise this flow today is by
// pulling the token straight out of the dev database.
export const resetPasswordSchema = z.object({
  email: emailSchema,
  token: z.string().min(1, "Reset token is required."),
  newPassword: passwordSchema,
});
export type ResetPasswordFormValues = z.infer<typeof resetPasswordSchema>;

// Mirrors `ChangePasswordCommandValidator`: NewPassword only. No current-password field — the
// caller already holds a valid JWT issued at the moment they authenticated with the temp
// password, which is proof enough (see requirements.md's "Forced password change for
// temp-password logins"). Distinct from `resetPasswordSchema`'s reset-token flow.
export const changePasswordSchema = z.object({
  newPassword: passwordSchema,
});
export type ChangePasswordFormValues = z.infer<typeof changePasswordSchema>;
