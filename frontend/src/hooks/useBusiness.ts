import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as businessApi from "@/api/businessApi";

// `enabled` defaults to `true` (every existing caller, e.g. `SettingsPage`, always wants this) —
// `InvoiceFormPage` passes `false` in edit mode, where the business default is irrelevant (the
// field there pre-fills from the invoice's own `paymentDetails` instead), same `enabled: !!id`-
// style opt-out pattern as `useClientQuery`.
export function useBusinessProfileQuery(enabled = true) {
  return useQuery({
    queryKey: ["business"],
    queryFn: () => businessApi.getProfile(),
    enabled,
  });
}

export function useUpdatePaymentDetailsMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (paymentDetails: string | null) => businessApi.updatePaymentDetails(paymentDetails),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["business"] });
    },
  });
}
