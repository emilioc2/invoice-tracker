import { useNavigate } from "react-router-dom";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Skeleton } from "@/components/ui/skeleton";
import { InvoiceStatusBadge } from "@/components/invoices/InvoiceStatusBadge";
import { useDashboardSummaryQuery } from "@/hooks/useDashboard";
import { cn, formatCurrency } from "@/lib/utils";
import type { InvoiceStatus } from "@/types/invoice";

// Shared affordance for the clickable stat/status cards below: a real <button>, not a div with
// onClick, so it's focusable and keyboard-activatable (Enter/Space) by default — see CLAUDE.md's
// accessibility conventions. Hover/focus styling stays subtle per this project's design conventions.
const CLICKABLE_CARD_CLASSES =
  "w-full text-left transition-colors hover:border-foreground/30 hover:shadow-[0_4px_12px_rgba(0,0,0,0.04)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2";

// requirements.md explicitly scopes "advanced reporting/analytics beyond the basic dashboard
// summary" out — plain stat cards, no chart library, nothing to configure.
export function DashboardPage() {
  const navigate = useNavigate();
  const { data, isLoading, isError } = useDashboardSummaryQuery();

  const goToInvoicesFilteredByStatus = (status: InvoiceStatus) => {
    navigate("/invoices", { state: { initialStatus: status } });
  };

  return (
    <div className="space-y-6">
      <h1 className="font-display text-2xl font-semibold">Dashboard</h1>

      {isError && (
        <Alert variant="destructive">
          <AlertTitle>Unable to load dashboard summary</AlertTitle>
          <AlertDescription>Something went wrong. Please try again.</AlertDescription>
        </Alert>
      )}

      {isLoading && !data && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {Array.from({ length: 2 }).map((_, index) => (
              <div key={index} className="space-y-3 rounded-lg border border-border p-6">
                <Skeleton className="h-4 w-32" />
                <Skeleton className="h-8 w-24" />
                <Skeleton className="h-3 w-40" />
              </div>
            ))}
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3 lg:grid-cols-5">
            {Array.from({ length: 5 }).map((_, index) => (
              <div key={index} className="space-y-3 rounded-lg border border-border p-4">
                <Skeleton className="h-5 w-16 rounded-full" />
                <Skeleton className="h-6 w-10" />
              </div>
            ))}
          </div>
        </>
      )}

      {data && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="rounded-lg border border-border p-6">
              <p className="text-sm text-muted-foreground">Outstanding total</p>
              <p className="mt-2 text-3xl font-semibold">{formatCurrency(data.outstandingTotal)}</p>
              <p className="mt-1 text-xs text-muted-foreground">Sum of Unpaid + Overdue invoices</p>
            </div>

            <button
              type="button"
              onClick={() => goToInvoicesFilteredByStatus("Overdue")}
              className={cn(
                "rounded-lg border p-6",
                CLICKABLE_CARD_CLASSES,
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
            </button>
          </div>

          <div className="space-y-3">
            <h2 className="text-lg font-medium">Invoices by status</h2>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3 lg:grid-cols-5">
              {data.countsByStatus.map(({ status, count }) => (
                <button
                  key={status}
                  type="button"
                  onClick={() => goToInvoicesFilteredByStatus(status as InvoiceStatus)}
                  className={cn("rounded-lg border border-border p-4", CLICKABLE_CARD_CLASSES)}
                >
                  <InvoiceStatusBadge status={status as InvoiceStatus} />
                  <p className="mt-2 text-2xl font-semibold">{count}</p>
                </button>
              ))}
            </div>
          </div>
        </>
      )}
    </div>
  );
}
