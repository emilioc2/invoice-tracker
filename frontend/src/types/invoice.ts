// Mirrors backend `InvoiceDto`/`InvoiceLineItemDto` — System.Text.Json default camelCase
// serialization, same convention as Client. `clientName` is a denormalized display field
// populated only by handlers that join against Clients (list views, e.g. GET /api/invoices) —
// it's `undefined`/`null` on single-invoice reads (GET /api/invoices/{id}), which still only
// carry `clientId`; resolving a display name there remains a frontend concern (see
// `components/invoices/ClientName.tsx`).
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
  /** Denormalized display field — see note above. Populated on list responses, `null`/absent otherwise. */
  clientName?: string | null;
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
  /** Nullable free-text payment instructions — see requirements.md's "Business payment details"
   * section. Snapshotted from `Business.PaymentDetails` at creation unless explicitly overridden;
   * editable only while `status === "Draft"`, same as line items/due date/client. `null`/empty
   * renders as "nothing" everywhere this field is read. */
  paymentDetails: string | null;
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
  /** Always sent as a plain string, same convention as `notes` — an empty string renders as
   * "nothing," same as `null`, so there's no need to distinguish the two at this boundary. On
   * create, this is pre-filled with the business's current default (see `InvoiceFormPage`) but
   * remains a normal editable override from the backend's point of view; on update, it's treated
   * like every other Draft-only-editable field. */
  paymentDetails: string;
  lineItems: InvoiceLineItemInput[];
}

// Mirrors backend `EmailDeliveryResult`/`SendInvoiceResult`/`ResendInvoiceEmailResult` — same
// camelCase convention as the rest of this file. Reported separately from the (always
// independent) Send/Resend action's own success, per architecture.md's "Invoice send/resend
// email delivery" section: the status transition (Send) always succeeds on its own merits, and
// email delivery is a best-effort side effect whose own outcome is surfaced here.
export interface EmailDeliveryResult {
  status: "Sent" | "Failed";
  notificationId: string;
}

// Response shape for POST /api/invoices/{id}/send.
export interface SendInvoiceResult {
  invoice: Invoice;
  emailDelivery: EmailDeliveryResult;
}

// Response shape for POST /api/invoices/{id}/resend-email.
export interface ResendInvoiceEmailResult {
  emailDelivery: EmailDeliveryResult;
}
