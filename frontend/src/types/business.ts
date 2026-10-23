// Mirrors backend `BusinessProfileDto` (GET /api/business) — System.Text.Json default camelCase
// serialization, same convention as every other type in this folder.
export interface BusinessProfile {
  id: string;
  name: string;
  email: string;
  /** Nullable free-text default payment instructions — see requirements.md's "Business payment
   * details" section. `null`/empty renders as "nothing" everywhere this field is read. */
  paymentDetails: string | null;
}
