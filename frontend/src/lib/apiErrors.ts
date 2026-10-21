import type { FieldValues, Path, UseFormReturn } from "react-hook-form";
import { ApiError } from "@/api/client";

/**
 * Shape of an auth-endpoint error body, per architecture.md's documented contract:
 * - `{ title }` only — a general failure (e.g. `AuthenticationFailedException`'s "Invalid email
 *   or password.").
 * - `{ title, errors: Record<string,string[]> }` — FluentValidation failures, keyed by the
 *   PascalCase C# command property name (e.g. "Email", "BusinessEmail"), not camelCase.
 * - `{ title, errors: string[] }` — ASP.NET Identity's own flat error list (e.g. duplicate email,
 *   invalid/expired reset token) — never field-keyed, so it can never be mapped to one input.
 */
interface ApiErrorBody {
  title?: string;
  errors?: Record<string, string[]> | string[];
}

const DEFAULT_MESSAGE = "Something went wrong. Please try again.";

/**
 * Applies an error from a failed auth mutation to an RHF form: field-level errors are set
 * directly on matching fields (via `fieldMap`, mapping the backend's PascalCase property name to
 * the form's camelCase field name), and anything that can't be cleanly mapped to a single field
 * — the flat Identity-error array, a title-only failure, or any dict key with no entry in
 * `fieldMap` — is returned as a general banner message instead of being forced onto an input.
 *
 * Returns the banner message to display, or `null` if the error was fully resolved into field
 * errors and no general banner is needed.
 */
export function applyApiErrorToForm<TFieldValues extends FieldValues>(
  error: unknown,
  form: UseFormReturn<TFieldValues>,
  fieldMap: Record<string, Path<TFieldValues>>,
): string | null {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : DEFAULT_MESSAGE;
  }

  const body = error.body as ApiErrorBody | null | undefined;
  if (!body || typeof body !== "object") {
    return DEFAULT_MESSAGE;
  }

  const { title, errors } = body;

  if (!errors) {
    return title ?? DEFAULT_MESSAGE;
  }

  if (Array.isArray(errors)) {
    // Flat Identity-error shape — never field-specific, always a general banner.
    return errors.length > 0 ? errors.join(" ") : title ?? DEFAULT_MESSAGE;
  }

  // Dict shape — map each PascalCase key to its RHF field where possible; anything left over
  // (a key with no entry in fieldMap) is surfaced in the banner instead of being dropped.
  const unmapped: string[] = [];
  for (const [key, messages] of Object.entries(errors)) {
    const fieldName = fieldMap[key];
    if (fieldName) {
      form.setError(fieldName, { type: "server", message: messages.join(" ") });
    } else {
      unmapped.push(...messages);
    }
  }

  return unmapped.length > 0 ? unmapped.join(" ") : null;
}
