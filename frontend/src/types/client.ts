// Mirrors backend `ClientDto` — System.Text.Json default camelCase serialization, same
// convention as auth. `phone`/`address` are always non-null strings: the backend coalesces
// `null -> ""` on create/update, so the wire shape never actually sends `null` for them.
export interface Client {
  id: string;
  name: string;
  email: string;
  phone: string;
  address: string;
  createdAtDate: string;
  lastModifiedDate: string;
  archivedAt: string | null;
}

// Request body shape for POST /api/clients and PUT /api/clients/{id} — identical fields for
// both, per `CreateClientCommandValidator`/`UpdateClientCommandValidator`.
export interface ClientInput {
  name: string;
  email: string;
  phone: string;
  address: string;
}
