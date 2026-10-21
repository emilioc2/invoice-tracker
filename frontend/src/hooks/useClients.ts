import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as clientsApi from "@/api/clientsApi";
import type { ClientListParams } from "@/api/clientsApi";
import type { ClientInput } from "@/types/client";

export function useClientsQuery(filters: ClientListParams) {
  return useQuery({
    queryKey: ["clients", filters],
    queryFn: () => clientsApi.list(filters),
  });
}

export function useClientQuery(id: string | undefined) {
  return useQuery({
    queryKey: ["clients", id],
    queryFn: () => clientsApi.getById(id as string),
    enabled: !!id,
    retry: false,
  });
}

export function useCreateClientMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: ClientInput) => clientsApi.create(input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["clients"] });
    },
  });
}

export function useUpdateClientMutation(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: ClientInput) => clientsApi.update(id, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["clients"] });
      queryClient.invalidateQueries({ queryKey: ["clients", id] });
    },
  });
}

export function useArchiveClientMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => clientsApi.archive(id),
    onSuccess: (_data, id) => {
      queryClient.invalidateQueries({ queryKey: ["clients"] });
      queryClient.invalidateQueries({ queryKey: ["clients", id] });
    },
  });
}
