import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as usersApi from "@/api/usersApi";

export function useUsersQuery() {
  return useQuery({
    queryKey: ["users"],
    queryFn: () => usersApi.list(),
  });
}

export function useCreateStaffUserMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (email: string) => usersApi.create(email),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["users"] });
    },
  });
}

export function useDeactivateUserMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => usersApi.deactivate(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["users"] });
    },
  });
}

export function useReactivateUserMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => usersApi.reactivate(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["users"] });
    },
  });
}
