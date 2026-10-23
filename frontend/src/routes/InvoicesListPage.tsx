import { MoreHorizontal } from "lucide-react";
import { useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import * as invoicesApi from "@/api/invoicesApi";
import { InvoiceFilters, type InvoiceFiltersValue } from "@/components/invoices/InvoiceFilters";
import { InvoiceStatusBadge } from "@/components/invoices/InvoiceStatusBadge";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { DataTable, type DataTableColumn } from "@/components/common/DataTable";
import { Pagination } from "@/components/common/Pagination";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { useArchiveInvoiceMutation, useInvoicesQuery } from "@/hooks/useInvoices";
import { formatCurrency } from "@/lib/utils";
import type { Invoice, InvoiceStatus } from "@/types/invoice";

const PAGE_SIZE = 20;

// Shape of the navigation state the Dashboard's status cards pass via
// `navigate('/invoices', { state: { initialStatus } })` — see DashboardPage.tsx.
interface InvoicesListNavigationState {
  initialStatus?: InvoiceStatus;
}

export function InvoicesListPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [filters, setFilters] = useState<InvoiceFiltersValue>({});
  const [pageNumber, setPageNumber] = useState(1);
  const [archiveTarget, setArchiveTarget] = useState<Invoice | null>(null);
  const [downloadingIds, setDownloadingIds] = useState<Set<string>>(new Set());

  // One-time seed from Dashboard navigation: a status card passes `initialStatus` via router
  // state so the list opens pre-filtered. Deliberately out of scope: syncing filters to/from the
  // URL in general — this only consumes the one-shot navigation state, then clears it (via
  // `replace`) so it isn't re-applied if the user later navigates back here through history.
  useEffect(() => {
    const navigationState = location.state as InvoicesListNavigationState | null;
    const initialStatus = navigationState?.initialStatus;
    if (initialStatus) {
      setFilters((prev) => ({ ...prev, status: initialStatus }));
      setPageNumber(1);
      navigate(location.pathname, { replace: true, state: null });
    }
    // Intentionally run once on mount only — this consumes a one-shot navigation payload, not a
    // value that should be re-applied on every filters/location change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const { data, isLoading, isError } = useInvoicesQuery({
    ...filters,
    pageNumber,
    pageSize: PAGE_SIZE,
  });

  const archiveMutation = useArchiveInvoiceMutation();

  // "Truly empty business" vs. "filtered to zero" (item 3's empty-state copy): any active filter
  // means an empty result is a filtering outcome, not evidence the business has zero invoices.
  const hasActiveFilters = Boolean(
    filters.status || filters.clientId || filters.dateFrom || filters.dateTo,
  );
  const emptyMessage = hasActiveFilters
    ? "No invoices match these filters. Try a different status or date range."
    : "No invoices yet. Create your first invoice to get started.";

  const handleDownloadPdf = async (invoice: Invoice) => {
    setDownloadingIds((prev) => new Set(prev).add(invoice.id));
    try {
      await invoicesApi.downloadPdf(invoice.id, invoice.invoiceNumber);
    } finally {
      setDownloadingIds((prev) => {
        const next = new Set(prev);
        next.delete(invoice.id);
        return next;
      });
    }
  };

  const columns: DataTableColumn<Invoice>[] = [
    { key: "invoiceNumber", header: "Invoice #", cell: (invoice) => invoice.invoiceNumber },
    {
      key: "client",
      header: "Client",
      cell: (invoice) => invoice.clientName ?? "Unknown client",
    },
    {
      key: "status",
      header: "Status",
      cell: (invoice) => <InvoiceStatusBadge status={invoice.status} />,
    },
    {
      key: "dueDate",
      header: "Due date",
      cell: (invoice) => new Date(invoice.dueDate).toLocaleDateString(),
    },
    { key: "total", header: "Total", cell: (invoice) => formatCurrency(invoice.total) },
    {
      key: "actions",
      header: "",
      className: "w-10 text-right",
      cell: (invoice) => (
        <div onClick={(event) => event.stopPropagation()} className="text-right">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label={`Actions for ${invoice.invoiceNumber}`}
              >
                <MoreHorizontal className="size-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onSelect={() => navigate(`/invoices/${invoice.id}`)}>
                View
              </DropdownMenuItem>
              {invoice.status === "Draft" && (
                <DropdownMenuItem onSelect={() => navigate(`/invoices/${invoice.id}/edit`)}>
                  Edit
                </DropdownMenuItem>
              )}
              <DropdownMenuItem
                disabled={downloadingIds.has(invoice.id)}
                onSelect={() => handleDownloadPdf(invoice)}
              >
                {downloadingIds.has(invoice.id) ? "Downloading…" : "Download PDF"}
              </DropdownMenuItem>
              <DropdownMenuItem
                variant="destructive"
                onSelect={() => setArchiveTarget(invoice)}
              >
                Remove
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-2xl font-semibold">Invoices</h1>
        <Button onClick={() => navigate("/invoices/new")}>New invoice</Button>
      </div>

      <InvoiceFilters
        value={filters}
        onChange={(next) => {
          setFilters(next);
          setPageNumber(1);
        }}
      />

      {isError && (
        <Alert variant="destructive">
          <AlertTitle>Unable to load invoices</AlertTitle>
          <AlertDescription>Something went wrong. Please try again.</AlertDescription>
        </Alert>
      )}

      <DataTable
        columns={columns}
        rows={data?.items ?? []}
        getRowId={(invoice) => invoice.id}
        onRowClick={(invoice) => navigate(`/invoices/${invoice.id}`)}
        isLoading={isLoading}
        emptyMessage={emptyMessage}
      />

      {data && data.totalPages > 1 && (
        <Pagination
          pageNumber={data.pageNumber}
          totalPages={data.totalPages}
          onPageChange={setPageNumber}
        />
      )}

      <ConfirmDialog
        open={!!archiveTarget}
        onOpenChange={(open) => {
          if (!open) setArchiveTarget(null);
        }}
        title="Remove invoice?"
        description={
          archiveTarget
            ? `"${archiveTarget.invoiceNumber}" will be hidden from this list. This can't be undone from here.`
            : ""
        }
        confirmLabel="Remove"
        isConfirming={archiveMutation.isPending}
        onConfirm={async () => {
          if (!archiveTarget) return;
          await archiveMutation.mutateAsync(archiveTarget.id);
          setArchiveTarget(null);
        }}
      />
    </div>
  );
}
