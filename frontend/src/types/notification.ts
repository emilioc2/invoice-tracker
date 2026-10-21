// Placeholder — real shape lands in step 11 alongside the Notifications screen.
export interface Notification {
  id: string;
  invoiceId: string;
  channel: string;
  status: "Pending" | "Sent" | "Failed";
  sentAt: string | null;
}
