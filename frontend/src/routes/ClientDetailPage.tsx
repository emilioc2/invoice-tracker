import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { ClientForm } from "@/components/clients/ClientForm";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { ApiError } from "@/api/client";
import { useArchiveClientMutation, useClientQuery, useUpdateClientMutation } from "@/hooks/useClients";

export function ClientDetailPage() {
  const { clientId } = useParams<{ clientId: string }>();
  const navigate = useNavigate();
  const [isEditOpen, setIsEditOpen] = useState(false);
  const [isArchiveOpen, setIsArchiveOpen] = useState(false);

  const { data: client, isLoading, error } = useClientQuery(clientId);
  const updateMutation = useUpdateClientMutation(clientId ?? "");
  const archiveMutation = useArchiveClientMutation();

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">Loading…</p>;
  }

  if (error instanceof ApiError && error.status === 404) {
    return (
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold">Client not found</h1>
        <p className="text-sm text-muted-foreground">
          This client doesn&apos;t exist, or it belongs to another business.
        </p>
        <Button variant="outline" onClick={() => navigate("/clients")}>
          Back to clients
        </Button>
      </div>
    );
  }

  if (error || !client) {
    return (
      <p className="text-sm text-destructive">Something went wrong loading this client.</p>
    );
  }

  const isArchived = !!client.archivedAt;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-semibold">{client.name}</h1>
          {isArchived && <Badge variant="secondary">Archived</Badge>}
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => setIsEditOpen(true)}>
            Edit
          </Button>
          {!isArchived && (
            <Button variant="destructive" onClick={() => setIsArchiveOpen(true)}>
              Archive
            </Button>
          )}
        </div>
      </div>

      <dl className="grid grid-cols-1 gap-4 rounded-lg border border-border p-6 sm:grid-cols-2">
        <div>
          <dt className="text-sm text-muted-foreground">Email</dt>
          <dd className="text-sm">{client.email}</dd>
        </div>
        <div>
          <dt className="text-sm text-muted-foreground">Phone</dt>
          <dd className="text-sm">{client.phone || "—"}</dd>
        </div>
        <div>
          <dt className="text-sm text-muted-foreground">Address</dt>
          <dd className="text-sm">{client.address || "—"}</dd>
        </div>
        <div>
          <dt className="text-sm text-muted-foreground">Created</dt>
          <dd className="text-sm">{new Date(client.createdAtDate).toLocaleString()}</dd>
        </div>
        <div>
          <dt className="text-sm text-muted-foreground">Last modified</dt>
          <dd className="text-sm">{new Date(client.lastModifiedDate).toLocaleString()}</dd>
        </div>
        {isArchived && client.archivedAt && (
          <div>
            <dt className="text-sm text-muted-foreground">Archived</dt>
            <dd className="text-sm">{new Date(client.archivedAt).toLocaleString()}</dd>
          </div>
        )}
      </dl>

      <Dialog open={isEditOpen} onOpenChange={setIsEditOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Edit client</DialogTitle>
          </DialogHeader>
          <ClientForm
            defaultValues={client}
            submitLabel="Save changes"
            onCancel={() => setIsEditOpen(false)}
            onSubmit={async (values) => {
              await updateMutation.mutateAsync(values);
              setIsEditOpen(false);
            }}
          />
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={isArchiveOpen}
        onOpenChange={setIsArchiveOpen}
        title="Archive client?"
        description={`"${client.name}" will be hidden from the clients list. This can't be undone from here.`}
        confirmLabel="Archive"
        isConfirming={archiveMutation.isPending}
        onConfirm={async () => {
          await archiveMutation.mutateAsync(client.id);
          setIsArchiveOpen(false);
        }}
      />
    </div>
  );
}
