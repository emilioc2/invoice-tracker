import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { DataTable, type DataTableColumn } from "@/components/common/DataTable";
import { Pagination } from "@/components/common/Pagination";
import { InvoiceNumber } from "@/components/notifications/InvoiceNumber";
import { NotificationStatusBadge } from "@/components/notifications/NotificationStatusBadge";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { useNotificationsQuery } from "@/hooks/useNotifications";
import type { Notification } from "@/types/notification";

const PAGE_SIZE = 20;

/**
 * Read-only audit view of what the Kafka overdue-notification pipeline has already done — no
 * create/update/delete affordances, see `useNotifications.ts`'s doc comment. Supports an optional
 * `?invoiceId=` query param so a future "view notifications for this invoice" link elsewhere in
 * the app could deep-link here, even though nothing currently builds that link.
 */
export function NotificationsPage() {
  const [searchParams] = useSearchParams();
  const invoiceId = searchParams.get("invoiceId") ?? undefined;
  const [pageNumber, setPageNumber] = useState(1);

  const { data, isLoading, isError } = useNotificationsQuery({
    invoiceId,
    pageNumber,
    pageSize: PAGE_SIZE,
  });

  const columns: DataTableColumn<Notification>[] = [
    {
      key: "invoice",
      header: "Invoice #",
      cell: (notification) => <InvoiceNumber invoiceId={notification.invoiceId} />,
    },
    { key: "channel", header: "Channel", cell: (notification) => notification.channel },
    {
      key: "status",
      header: "Status",
      cell: (notification) => <NotificationStatusBadge status={notification.status} />,
    },
    {
      key: "sentAt",
      header: "Sent at",
      cell: (notification) =>
        notification.sentAt ? new Date(notification.sentAt).toLocaleString() : "—",
    },
    {
      key: "message",
      header: "Message",
      className: "max-w-sm whitespace-pre-wrap break-words",
      cell: (notification) => notification.message,
    },
  ];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold">Notifications</h1>
        <p className="text-sm text-muted-foreground">
          Audit log of overdue-invoice email notifications sent by the system.
        </p>
      </div>

      {isError && (
        <Alert variant="destructive">
          <AlertTitle>Unable to load notifications</AlertTitle>
          <AlertDescription>Something went wrong. Please try again.</AlertDescription>
        </Alert>
      )}

      <DataTable
        columns={columns}
        rows={data?.items ?? []}
        getRowId={(notification) => notification.id}
        emptyMessage={isLoading ? "Loading…" : "No notifications yet."}
      />

      {data && data.totalPages > 1 && (
        <Pagination
          pageNumber={data.pageNumber}
          totalPages={data.totalPages}
          onPageChange={setPageNumber}
        />
      )}
    </div>
  );
}
