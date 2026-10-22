// Mirrors backend `ClientDto` — System.Text.Json default camelCase serialization, same
// convention as auth. `phone`/`address` are always non-null strings: the backend coalesces
// `null -> ""` on create/update, so the wire shape never actually sends `null` for them.
//
// `status`/`statusChangedAt` replace the former nullable `archivedAt` timestamp (see
// architecture.md's "Domain invariants: centralized status transitions") — `ClientStatus` is
// `Active | Removed`, set via the backend's guarded `Client.TransitionTo`. The only legal
// transition is Active -> Removed (user-facing "Remove"); there is no reverse transition.
export type ClientStatus = "Active" | "Removed";

export interface Client {
  id: string;
  name: string;
  email: string;
  phone: string;
  address: string;
  createdAtDate: string;
  lastModifiedDate: string;
  status: ClientStatus;
  statusChangedAt: string;
}

// Request body shape for POST /api/clients and PUT /api/clients/{id} — identical fields for
// both, per `CreateClientCommandValidator`/`UpdateClientCommandValidator`.
export interface ClientInput {
  name: string;
  email: string;
  phone: string;
  address: string;
}
