import { Link } from "react-router-dom";
import { useClientQuery } from "@/hooks/useClients";

/**
 * `InvoiceDto` only has `clientId`, never a client name — resolving a display name is a frontend
 * concern. Wraps the already-cached-and-deduped-per-id `useClientQuery` rather than fetching the
 * whole client list, so repeated rows for the same client only trigger one request.
 */
export function ClientName({ clientId, linkToDetail = false }: { clientId: string; linkToDetail?: boolean }) {
  const { data: client, isLoading } = useClientQuery(clientId);

  if (isLoading) return <span className="text-muted-foreground">Loading…</span>;
  if (!client) return <span className="text-muted-foreground">Unknown client</span>;

  if (linkToDetail) {
    return (
      <Link to={`/clients/${client.id}`} className="underline-offset-2 hover:underline">
        {client.name}
      </Link>
    );
  }

  return <span>{client.name}</span>;
}
