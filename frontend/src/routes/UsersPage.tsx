import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { ApiError } from "@/api/client";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { DataTable, type DataTableColumn } from "@/components/common/DataTable";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useAuth } from "@/hooks/useAuth";
import { useCreateStaffUserMutation, useDeactivateUserMutation, useUsersQuery } from "@/hooks/useUsers";
import { applyApiErrorToForm } from "@/lib/apiErrors";
import { createStaffUserSchema, type CreateStaffUserFormValues } from "@/schemas/userSchema";
import type { User } from "@/types/auth";

// Maps the backend CreateStaffUserCommandValidator's PascalCase property name to this form's
// field — same pattern as `ClientForm`'s FIELD_MAP.
const FIELD_MAP = { Email: "email" } as const;

// Display-only: the backend persists and returns the literal role string "Owner" (see
// `architecture.md`'s UI display note), but end users should see it labeled "Admin". Never use
// this for comparisons — role checks must keep comparing against the raw "Owner"/"Staff" values.
function displayRole(role: string): string {
  return role === "Owner" ? "Admin" : role;
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

export function UsersPage() {
  const { user: currentUser } = useAuth();
  const isOwner = currentUser?.role === "Owner";

  const [isAddOpen, setIsAddOpen] = useState(false);
  const [addSuccessMessage, setAddSuccessMessage] = useState<string | null>(null);
  const [deactivateTarget, setDeactivateTarget] = useState<User | null>(null);
  const [deactivateError, setDeactivateError] = useState<string | null>(null);

  const { data: users, isLoading, isError } = useUsersQuery();
  const createMutation = useCreateStaffUserMutation();
  const deactivateMutation = useDeactivateUserMutation();

  const form = useForm<CreateStaffUserFormValues>({
    resolver: zodResolver(createStaffUserSchema),
    defaultValues: { email: "" },
  });

  const closeAddDialog = () => {
    setIsAddOpen(false);
    setAddSuccessMessage(null);
    form.reset({ email: "" });
    createMutation.reset();
  };

  const handleCreate = async (values: CreateStaffUserFormValues) => {
    setAddSuccessMessage(null);
    try {
      await createMutation.mutateAsync(values.email);
      form.reset({ email: "" });
      setAddSuccessMessage("Staff account created — a temporary password has been emailed to them.");
    } catch (error) {
      // `applyApiErrorToForm` either sets field errors directly or, for an unmapped/general
      // failure (e.g. the flat duplicate-email Identity-error array), returns a banner message —
      // RHF's own root-level error slot carries that banner here rather than a separate state.
      const banner = applyApiErrorToForm(error, form, FIELD_MAP);
      if (banner) {
        form.setError("root", { type: "server", message: banner });
      }
    }
  };

  // Shown only when the viewer is Owner AND the target isn't the viewer's own row AND the target
  // isn't an Owner AND the target is currently active — predicting the backend's own rejection
  // rules (`DeactivateUserCommandHandler`) rather than letting the user click and get a 400.
  const canDeactivate = (target: User): boolean =>
    isOwner && target.id !== currentUser?.userId && target.role !== "Owner" && target.isActive;

  const columns: DataTableColumn<User>[] = [
    { key: "email", header: "Email", cell: (u) => u.email },
    { key: "role", header: "Role", cell: (u) => displayRole(u.role) },
    {
      key: "status",
      header: "Status",
      cell: (u) => (
        <Badge variant={u.isActive ? "default" : "secondary"}>
          {u.isActive ? "Active" : "Inactive"}
        </Badge>
      ),
    },
    {
      key: "actions",
      header: "",
      className: "w-10 text-right",
      cell: (u) =>
        canDeactivate(u) ? (
          <div className="text-right">
            <Button
              variant="destructive"
              size="sm"
              onClick={() => {
                setDeactivateError(null);
                setDeactivateTarget(u);
              }}
            >
              Deactivate
            </Button>
          </div>
        ) : null,
    },
  ];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Users</h1>
        {isOwner && (
          <Button
            onClick={() => {
              setAddSuccessMessage(null);
              createMutation.reset();
              setIsAddOpen(true);
            }}
          >
            Add Staff
          </Button>
        )}
      </div>

      {isError && (
        <Alert variant="destructive">
          <AlertTitle>Unable to load users</AlertTitle>
          <AlertDescription>Something went wrong. Please try again.</AlertDescription>
        </Alert>
      )}

      {deactivateError && (
        <Alert variant="destructive">
          <AlertTitle>Unable to deactivate user</AlertTitle>
          <AlertDescription>{deactivateError}</AlertDescription>
        </Alert>
      )}

      <DataTable
        columns={columns}
        rows={users ?? []}
        getRowId={(u) => u.id}
        emptyMessage={isLoading ? "Loading…" : "No users found."}
      />

      <Dialog
        open={isAddOpen}
        onOpenChange={(open) => {
          if (!open) closeAddDialog();
        }}
      >
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Add Staff</DialogTitle>
          </DialogHeader>

          {addSuccessMessage ? (
            <div className="space-y-4">
              <Alert>
                <AlertTitle>Success</AlertTitle>
                <AlertDescription>{addSuccessMessage}</AlertDescription>
              </Alert>
              <div className="flex justify-end">
                <Button onClick={closeAddDialog}>Done</Button>
              </div>
            </div>
          ) : (
            <form className="space-y-4" onSubmit={form.handleSubmit(handleCreate)} noValidate>
              {form.formState.errors.root && (
                <Alert variant="destructive">
                  <AlertTitle>Unable to create staff account</AlertTitle>
                  <AlertDescription>{form.formState.errors.root.message}</AlertDescription>
                </Alert>
              )}

              <div className="space-y-1.5">
                <Label htmlFor="staff-email">Email</Label>
                <Input
                  id="staff-email"
                  type="email"
                  aria-invalid={!!form.formState.errors.email}
                  aria-describedby={form.formState.errors.email ? "staff-email-error" : undefined}
                  {...form.register("email")}
                />
                {form.formState.errors.email && (
                  <p id="staff-email-error" className="text-sm text-destructive">
                    {form.formState.errors.email.message}
                  </p>
                )}
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <Button type="button" variant="outline" onClick={closeAddDialog} disabled={createMutation.isPending}>
                  Cancel
                </Button>
                <Button type="submit" disabled={createMutation.isPending}>
                  {createMutation.isPending ? "Creating…" : "Create"}
                </Button>
              </div>
            </form>
          )}
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={!!deactivateTarget}
        onOpenChange={(open) => {
          if (!open) setDeactivateTarget(null);
        }}
        title="Deactivate user?"
        description={
          deactivateTarget
            ? `"${deactivateTarget.email}" will no longer be able to sign in.`
            : ""
        }
        confirmLabel="Deactivate"
        isConfirming={deactivateMutation.isPending}
        onConfirm={async () => {
          if (!deactivateTarget) return;
          try {
            await deactivateMutation.mutateAsync(deactivateTarget.id);
            setDeactivateTarget(null);
          } catch (error) {
            setDeactivateError(extractErrorTitle(error));
            setDeactivateTarget(null);
          }
        }}
      />
    </div>
  );
}
