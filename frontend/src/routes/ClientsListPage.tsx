import { MoreHorizontal } from "lucide-react";
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import {
  DataTable,
  type DataTableColumn,
  type DataTableMobileCard,
} from "@/components/common/DataTable";
import { Pagination } from "@/components/common/Pagination";
import { ClientForm } from "@/components/clients/ClientForm";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { useClientsQuery, useCreateClientMutation, useRemoveClientMutation } from "@/hooks/useClients";
import { useDebouncedValue } from "@/hooks/useDebouncedValue";
import type { Client } from "@/types/client";

const PAGE_SIZE = 20;

export function ClientsListPage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const [pageNumber, setPageNumber] = useState(1);
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [removeTarget, setRemoveTarget] = useState<Client | null>(null);

  const debouncedSearch = useDebouncedValue(search, 300);

  const { data, isLoading, isError } = useClientsQuery({
    search: debouncedSearch || undefined,
    pageNumber,
    pageSize: PAGE_SIZE,
  });

  const createMutation = useCreateClientMutation();
  const removeMutation = useRemoveClientMutation();

  const renderActions = (client: Client) => (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon-sm" aria-label={`Actions for ${client.name}`}>
          <MoreHorizontal className="size-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem onSelect={() => navigate(`/clients/${client.id}`)}>View</DropdownMenuItem>
        <DropdownMenuItem variant="destructive" onSelect={() => setRemoveTarget(client)}>
          Remove
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );

  const columns: DataTableColumn<Client>[] = [
    { key: "name", header: "Name", cell: (client) => client.name },
    { key: "email", header: "Email", cell: (client) => client.email },
    { key: "phone", header: "Phone", cell: (client) => client.phone || "—" },
    {
      key: "actions",
      header: "",
      className: "w-10 text-right",
      cell: (client) => (
        <div onClick={(event) => event.stopPropagation()} className="text-right">
          {renderActions(client)}
        </div>
      ),
    },
  ];

  // Card view below the ~768px breakpoint (architecture.md's "Mobile responsiveness"
  // paragraph): name is the card's heading, email/phone surface as labeled fields — the same
  // priority order as the desktop columns above, just without the dedicated header row.
  const mobileCard: DataTableMobileCard<Client> = {
    fields: [
      { key: "name", value: (client) => client.name, emphasis: true },
      { key: "email", label: "Email", value: (client) => client.email },
      { key: "phone", label: "Phone", value: (client) => client.phone || "—" },
    ],
    actions: renderActions,
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-2xl font-semibold">Clients</h1>
        <Button
          onClick={() => {
            createMutation.reset();
            setIsCreateOpen(true);
          }}
        >
          Add client
        </Button>
      </div>

      <Input
        placeholder="Search by name or email…"
        value={search}
        onChange={(event) => {
          setSearch(event.target.value);
          setPageNumber(1);
        }}
        className="max-w-sm"
        aria-label="Search clients"
      />

      {isError && (
        <Alert variant="destructive">
          <AlertTitle>Unable to load clients</AlertTitle>
          <AlertDescription>Something went wrong. Please try again.</AlertDescription>
        </Alert>
      )}

      <DataTable
        columns={columns}
        rows={data?.items ?? []}
        getRowId={(client) => client.id}
        onRowClick={(client) => navigate(`/clients/${client.id}`)}
        isLoading={isLoading}
        emptyMessage="No clients yet. Add your first client to start invoicing them."
        mobileCard={mobileCard}
      />

      {data && data.totalPages > 1 && (
        <Pagination
          pageNumber={data.pageNumber}
          totalPages={data.totalPages}
          onPageChange={setPageNumber}
        />
      )}

      <Dialog open={isCreateOpen} onOpenChange={setIsCreateOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Add client</DialogTitle>
          </DialogHeader>
          <ClientForm
            submitLabel="Create"
            onCancel={() => setIsCreateOpen(false)}
            onSubmit={async (values) => {
              await createMutation.mutateAsync(values);
              setIsCreateOpen(false);
            }}
          />
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={!!removeTarget}
        onOpenChange={(open) => {
          if (!open) setRemoveTarget(null);
        }}
        title="Remove client?"
        description={
          removeTarget
            ? `"${removeTarget.name}" will be hidden from this list. This can't be undone from here.`
            : ""
        }
        confirmLabel="Remove"
        isConfirming={removeMutation.isPending}
        onConfirm={async () => {
          if (!removeTarget) return;
          await removeMutation.mutateAsync(removeTarget.id);
          setRemoveTarget(null);
        }}
      />
    </div>
  );
}
