import { useQuery } from "@tanstack/react-query";
import * as authApi from "@/api/authApi";

/**
 * Lists the caller's own active memberships — backs AppShell's "Switch business" dropdown.
 * `enabled` is tied to whether that dropdown is currently open, so this never fetches on page
 * load, only when a person actually opens the switcher (matching architecture.md's "fetched
 * lazily, only when opened" instruction).
 */
export function useMembershipsQuery(enabled: boolean) {
  return useQuery({
    queryKey: ["auth", "memberships"],
    queryFn: () => authApi.listMyMemberships(),
    enabled,
  });
}
