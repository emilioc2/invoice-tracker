import { apiFetch } from "@/api/client";
import type { BusinessProfile } from "@/types/business";

// Singular, non-plural route (`/api/business`, not `/api/businesses`) since a token is always
// scoped to exactly one business — see architecture.md's REST API routes table. Both routes here
// are reachable by any authenticated user (Owner or Staff), unlike the Owner-only Users routes.
export function getProfile(): Promise<BusinessProfile> {
  return apiFetch<BusinessProfile>("/api/business");
}

export function updatePaymentDetails(paymentDetails: string | null): Promise<BusinessProfile> {
  return apiFetch<BusinessProfile>("/api/business/payment-details", {
    method: "PUT",
    body: { paymentDetails },
  });
}
