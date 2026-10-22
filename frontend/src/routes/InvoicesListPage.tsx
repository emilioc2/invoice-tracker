import { MoreHorizontal } from "lucide-react";
import { useState } from "react";
import { useNavigate } from "react-router-dom";
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
import type { Invoice } from "@/types/invoice";

const PAGE_SIZE = 20;

export function InvoicesListPage() {
  const navigate = useNavigate();
  const [filters, setFilters] = useState<InvoiceFiltersValue>({});
  const [pageNumber, setPageNumber] = useState(1);
  const [archiveTarget, setArchiveTarget] = useState<Invoice | null>(null);

  const { data, isLoading, isError } = useInvoicesQuery({
    ...filters,
    pageNumber,
    pageSize: PAGE_SIZE,
  });

  const archiveMutation = useArchiveInvoiceMutation();

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
              <DropdownMenuItem
                variant="destructive"
                onSelect={() => setArchiveTarget(invoice)}
              >
                Archive
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
        <h1 className="text-2xl font-semibold">Invoices</h1>
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
        emptyMessage={isLoading ? "Loading…" : "No invoices found."}
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
        title="Archive invoice?"
        description={
          archiveTarget
            ? `"${archiveTarget.invoiceNumber}" will be hidden from this list. This can't be undone from here.`
            : ""
        }
        confirmLabel="Archive"
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
