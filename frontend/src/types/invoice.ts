// Placeholder — real shape lands in step 11 alongside the Invoices screens.
export type InvoiceStatus = "Draft" | "Unpaid" | "Overdue" | "Paid" | "Cancelled";

export interface Invoice {
  id: string;
  invoiceNumber: string;
  clientId: string;
  status: InvoiceStatus;
  totalAmount: number;
  dueDate: string;
}
