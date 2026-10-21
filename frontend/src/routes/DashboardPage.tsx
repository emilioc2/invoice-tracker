import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { InvoiceStatusBadge } from "@/components/invoices/InvoiceStatusBadge";
import { useDashboardSummaryQuery } from "@/hooks/useDashboard";
import { cn, formatCurrency } from "@/lib/utils";
import type { InvoiceStatus } from "@/types/invoice";

// requirements.md explicitly scopes "advanced reporting/analytics beyond the basic dashboard
// summary" out — plain stat cards, no chart library, nothing to configure.
export function DashboardPage() {
  const { data, isLoading, isError } = useDashboardSummaryQuery();

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Dashboard</h1>

      {isError && (
        <Alert variant="destructive">
          <AlertTitle>Unable to load dashboard summary</AlertTitle>
          <AlertDescription>Something went wrong. Please try again.</AlertDescription>
        </Alert>
      )}

      {isLoading && !data && <p className="text-sm text-muted-foreground">Loading…</p>}

      {data && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="rounded-lg border border-border p-6">
              <p className="text-sm text-muted-foreground">Outstanding total</p>
              <p className="mt-2 text-3xl font-semibold">{formatCurrency(data.outstandingTotal)}</p>
              <p className="mt-1 text-xs text-muted-foreground">Sum of Unpaid + Overdue invoices</p>
            </div>

            <div
              className={cn(
                "rounded-lg border p-6",
                data.overdueCount > 0
                  ? "border-amber-300 bg-amber-50 dark:border-amber-500/40 dark:bg-amber-500/10"
                  : "border-border",
              )}
            >
              <p className="text-sm text-muted-foreground">Overdue invoices</p>
              <p
                className={cn(
                  "mt-2 text-3xl font-semibold",
                  data.overdueCount > 0 && "text-amber-700 dark:text-amber-400",
                )}
              >
                {data.overdueCount}
              </p>
              <p className="mt-1 text-xs text-muted-foreground">Needs attention</p>
            </div>
          </div>

          <div className="space-y-3">
            <h2 className="text-lg font-medium">Invoices by status</h2>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3 lg:grid-cols-5">
              {data.countsByStatus.map(({ status, count }) => (
                <div key={status} className="rounded-lg border border-border p-4">
                  <InvoiceStatusBadge status={status as InvoiceStatus} />
                  <p className="mt-2 text-2xl font-semibold">{count}</p>
                </div>
              ))}
            </div>
          </div>
        </>
      )}
    </div>
  );
}
