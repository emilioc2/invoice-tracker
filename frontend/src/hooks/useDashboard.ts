import { useQuery } from "@tanstack/react-query";
import * as dashboardApi from "@/api/dashboardApi";

export function useDashboardSummaryQuery() {
  return useQuery({
    queryKey: ["dashboard", "summary"],
    queryFn: () => dashboardApi.getSummary(),
  });
}
