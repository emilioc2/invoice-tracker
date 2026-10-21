import { Link } from "react-router-dom";
import { useInvoiceQuery } from "@/hooks/useInvoices";

/**
 * `NotificationDto` only has `invoiceId`, never the invoice's own display number — resolving it
 * is a frontend concern, same pattern as `components/invoices/ClientName.tsx` wrapping
 * `useClientQuery`. Wraps the already-cached-and-deduped-per-id `useInvoiceQuery` so repeated rows
 * for the same invoice only trigger one request.
 */
export function InvoiceNumber({ invoiceId }: { invoiceId: string }) {
  const { data: invoice, isLoading } = useInvoiceQuery(invoiceId);

  if (isLoading) return <span className="text-muted-foreground">Loading…</span>;
  if (!invoice) return <span className="text-muted-foreground">Unknown invoice</span>;

  return (
    <Link to={`/invoices/${invoice.id}`} className="underline-offset-2 hover:underline">
      {invoice.invoiceNumber}
    </Link>
  );
}
