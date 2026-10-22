import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { ApiError } from "@/api/client";
import * as invoicesApi from "@/api/invoicesApi";
import { ClientName } from "@/components/invoices/ClientName";
import { InvoiceStatusBadge } from "@/components/invoices/InvoiceStatusBadge";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  useArchiveInvoiceMutation,
  useCancelInvoiceMutation,
  useInvoiceQuery,
  useMarkInvoicePaidMutation,
  useResendInvoiceEmailMutation,
  useSendInvoiceMutation,
} from "@/hooks/useInvoices";
import { formatCurrency } from "@/lib/utils";
import type { EmailDeliveryResult } from "@/types/invoice";

type PendingAction = "send" | "resendEmail" | "markPaid" | "cancel" | "archive" | null;

const ACTION_CONFIG: Record<
  Exclude<PendingAction, null>,
  { title: string; description: string; confirmLabel: string }
> = {
  send: {
    title: "Send invoice?",
    description:
      "This moves the invoice from Draft to Unpaid, sets its issue date, and locks it from further editing.",
    confirmLabel: "Send",
  },
  resendEmail: {
    title: "Resend invoice email?",
    description: "Re-sends the invoice PDF and details to the client.",
    confirmLabel: "Resend",
  },
  markPaid: {
    title: "Mark invoice as paid?",
    description: "This marks the invoice as Paid.",
    confirmLabel: "Mark paid",
  },
  cancel: {
    title: "Cancel invoice?",
    description: "This cancels the invoice. This can't be undone from here.",
    confirmLabel: "Cancel invoice",
  },
  archive: {
    title: "Remove invoice?",
    description: "This hides the invoice from the invoices list. This can't be undone from here.",
    confirmLabel: "Remove",
  },
};

// The Send/Resend action itself always succeeds independently of email delivery (per
// architecture.md's "Invoice send/resend email delivery" section) — a `Failed` outcome here is
// an expected possible result, not an action failure, so it's surfaced via a non-destructive
// info Alert rather than the destructive `actionError` one. `Sent` needs no special message —
// the existing success flow (dialog closes, status badge updates) is enough.
function emailDeliveryMessage(
  action: "send" | "resendEmail",
  emailDelivery: EmailDeliveryResult,
): string | null {
  if (emailDelivery.status !== "Failed") return null;
  return action === "send"
    ? "Invoice sent, but email delivery failed. You can try resending it below."
    : "Email delivery failed. You can try resending it again below.";
}

function extractErrorTitle(error: unknown): string {
  if (error instanceof ApiError) {
    const body = error.body as { title?: string } | null | undefined;
    if (body && typeof body === "object" && body.title) {
      return body.title;
    }
  }
  return "Something went wrong. Please try again.";
}

export function InvoiceDetailPage() {
  const { invoiceId } = useParams<{ invoiceId: string }>();
  const navigate = useNavigate();
  const [pendingAction, setPendingAction] = useState<PendingAction>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [emailInfoMessage, setEmailInfoMessage] = useState<string | null>(null);
  const [isDownloading, setIsDownloading] = useState(false);

  const { data: invoice, isLoading, error } = useInvoiceQuery(invoiceId);
  const sendMutation = useSendInvoiceMutation();
  const resendEmailMutation = useResendInvoiceEmailMutation();
  const markPaidMutation = useMarkInvoicePaidMutation();
  const cancelMutation = useCancelInvoiceMutation();
  const archiveMutation = useArchiveInvoiceMutation();

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">Loading…</p>;
  }

  if (error instanceof ApiError && error.status === 404) {
    return (
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold">Invoice not found</h1>
        <p className="text-sm text-muted-foreground">
          This invoice doesn&apos;t exist, or it belongs to another business.
        </p>
        <Button variant="outline" onClick={() => navigate("/invoices")}>
          Back to invoices
        </Button>
      </div>
    );
  }

  if (error || !invoice) {
    return <p className="text-sm text-destructive">Something went wrong loading this invoice.</p>;
  }

  const isArchived = !!invoice.archivedAt;
  const isConfirming =
    sendMutation.isPending ||
    resendEmailMutation.isPending ||
    markPaidMutation.isPending ||
    cancelMutation.isPending ||
    archiveMutation.isPending;

  const runAction = async () => {
    if (!pendingAction || !invoiceId) return;
    setActionError(null);
    setEmailInfoMessage(null);
    try {
      if (pendingAction === "send") {
        const { emailDelivery } = await sendMutation.mutateAsync(invoiceId);
        setEmailInfoMessage(emailDeliveryMessage("send", emailDelivery));
      }
      if (pendingAction === "resendEmail") {
        const { emailDelivery } = await resendEmailMutation.mutateAsync(invoiceId);
        setEmailInfoMessage(emailDeliveryMessage("resendEmail", emailDelivery));
      }
      if (pendingAction === "markPaid") await markPaidMutation.mutateAsync(invoiceId);
      if (pendingAction === "cancel") await cancelMutation.mutateAsync(invoiceId);
      if (pendingAction === "archive") await archiveMutation.mutateAsync(invoiceId);
      setPendingAction(null);
    } catch (actionErr) {
      setActionError(extractErrorTitle(actionErr));
    }
  };

  const openAction = (action: Exclude<PendingAction, null>) => {
    setActionError(null);
    setEmailInfoMessage(null);
    setPendingAction(action);
  };

  const handleDownloadPdf = async () => {
    setActionError(null);
    setIsDownloading(true);
    try {
      await invoicesApi.downloadPdf(invoice.id, invoice.invoiceNumber);
    } catch (downloadError) {
      setActionError(extractErrorTitle(downloadError));
    } finally {
      setIsDownloading(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-semibold">{invoice.invoiceNumber}</h1>
          <InvoiceStatusBadge status={invoice.status} />
          {isArchived && <Badge variant="secondary">Archived</Badge>}
        </div>
        <div className="flex gap-2">
          {invoice.status === "Draft" && (
            <Button variant="outline" onClick={() => navigate(`/invoices/${invoice.id}/edit`)}>
              Edit
            </Button>
          )}
          {invoice.status === "Draft" && (
            <Button onClick={() => openAction("send")}>Send</Button>
          )}
          {(invoice.status === "Unpaid" || invoice.status === "Overdue") && (
            <Button onClick={() => openAction("markPaid")}>Mark paid</Button>
          )}
          {(invoice.status === "Draft" || invoice.status === "Unpaid") && (
            <Button variant="destructive" onClick={() => openAction("cancel")}>
              Cancel invoice
            </Button>
          )}
          {!isArchived && (
            <Button variant="destructive" onClick={() => openAction("archive")}>
              Remove
            </Button>
          )}
          {invoice.status !== "Draft" && (
            <Button variant="outline" onClick={() => openAction("resendEmail")}>
              Resend email
            </Button>
          )}
          <Button variant="outline" onClick={handleDownloadPdf} disabled={isDownloading}>
            {isDownloading ? "Downloading…" : "Download PDF"}
          </Button>
        </div>
      </div>

      {actionError && (
        <Alert variant="destructive">
          <AlertTitle>Action failed</AlertTitle>
          <AlertDescription>{actionError}</AlertDescription>
        </Alert>
      )}

      {/* Email delivery is a best-effort side effect of Send/Resend, reported separately from
          the (always independent) action's own success — a `Failed` outcome is an expected
          possible result, not an action failure, so it gets this non-destructive info Alert
          rather than the destructive `actionError` one above. */}
      {emailInfoMessage && (
        <Alert>
          <AlertTitle>Email delivery</AlertTitle>
          <AlertDescription>{emailInfoMessage}</AlertDescription>
        </Alert>
      )}

      <dl className="grid grid-cols-1 gap-4 rounded-lg border border-border p-6 sm:grid-cols-2">
        <div>
          <dt className="text-sm text-muted-foreground">Client</dt>
          <dd className="text-sm">
            <ClientName clientId={invoice.clientId} linkToDetail />
          </dd>
        </div>
        <div>
          <dt className="text-sm text-muted-foreground">Created</dt>
          <dd className="text-sm">{new Date(invoice.createdAtDate).toLocaleString()}</dd>
        </div>
        <div>
          <dt className="text-sm text-muted-foreground">Issue date</dt>
          <dd className="text-sm">
            {invoice.issueDate ? new Date(invoice.issueDate).toLocaleDateString() : "— (not yet sent)"}
          </dd>
        </div>
        <div>
          <dt className="text-sm text-muted-foreground">Due date</dt>
          <dd className="text-sm">{new Date(invoice.dueDate).toLocaleDateString()}</dd>
        </div>
        {isArchived && invoice.archivedAt && (
          <div>
            <dt className="text-sm text-muted-foreground">Archived</dt>
            <dd className="text-sm">{new Date(invoice.archivedAt).toLocaleString()}</dd>
          </div>
        )}
      </dl>

      <div className="space-y-3">
        <h2 className="text-lg font-medium">Line items</h2>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border text-left text-muted-foreground">
              <th className="py-2">Description</th>
              <th className="py-2 text-right">Quantity</th>
              <th className="py-2 text-right">Unit price</th>
              <th className="py-2 text-right">Line total</th>
            </tr>
          </thead>
          <tbody>
            {invoice.lineItems.map((item) => (
              <tr key={item.id} className="border-b border-border/50">
                <td className="py-2">{item.description}</td>
                <td className="py-2 text-right">{item.quantity}</td>
                <td className="py-2 text-right">{formatCurrency(item.unitPrice)}</td>
                <td className="py-2 text-right">{formatCurrency(item.lineTotal)}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <div className="flex flex-col items-end gap-1 text-sm">
          <div className="flex w-48 justify-between">
            <span className="text-muted-foreground">Subtotal</span>
            <span>{formatCurrency(invoice.subtotal)}</span>
          </div>
          <div className="flex w-48 justify-between">
            <span className="text-muted-foreground">Tax ({(invoice.taxRate * 100).toFixed(2)}%)</span>
            <span>{formatCurrency(invoice.taxAmount)}</span>
          </div>
          <div className="flex w-48 justify-between font-medium">
            <span>Total</span>
            <span>{formatCurrency(invoice.total)}</span>
          </div>
        </div>
      </div>

      {invoice.notes && (
        <div className="space-y-1">
          <h2 className="text-lg font-medium">Notes</h2>
          <p className="whitespace-pre-wrap text-sm text-muted-foreground">{invoice.notes}</p>
        </div>
      )}

      <ConfirmDialog
        open={!!pendingAction}
        onOpenChange={(open) => {
          if (!open) setPendingAction(null);
        }}
        title={pendingAction ? ACTION_CONFIG[pendingAction].title : ""}
        description={pendingAction ? ACTION_CONFIG[pendingAction].description : ""}
        confirmLabel={pendingAction ? ACTION_CONFIG[pendingAction].confirmLabel : "Confirm"}
        isConfirming={isConfirming}
        onConfirm={runAction}
      />
    </div>
  );
}
