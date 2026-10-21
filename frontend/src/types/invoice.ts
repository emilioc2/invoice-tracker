// Mirrors backend `InvoiceDto`/`InvoiceLineItemDto` — System.Text.Json default camelCase
// serialization, same convention as Client. Note there's no client name on the wire, only
// `clientId` — resolving a display name is a frontend concern (see
// `components/invoices/ClientName.tsx`), not a backend join.
export type InvoiceStatus = "Draft" | "Unpaid" | "Overdue" | "Paid" | "Cancelled";

export interface InvoiceLineItem {
  id: string;
  description: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface Invoice {
  id: string;
  clientId: string;
  invoiceNumber: string;
  createdAtDate: string;
  /** `null` while Draft — set automatically (and becomes immutable) the moment it's sent. */
  issueDate: string | null;
  dueDate: string;
  status: InvoiceStatus;
  subtotal: number;
  /** A 0–1 fraction (e.g. `0.08` for 8%), not a percentage. */
  taxRate: number;
  taxAmount: number;
  total: number;
  notes: string | null;
  lastModifiedDate: string;
  archivedAt: string | null;
  lineItems: InvoiceLineItem[];
}

// Request body shape for POST /api/invoices and PUT /api/invoices/{id} — identical fields for
// both, per `CreateInvoiceCommandValidator`/`UpdateInvoiceCommandValidator`.
export interface InvoiceLineItemInput {
  description: string;
  quantity: number;
  unitPrice: number;
}

export interface InvoiceInput {
  clientId: string;
  dueDate: string;
  taxRate: number;
  notes: string;
  lineItems: InvoiceLineItemInput[];
}
