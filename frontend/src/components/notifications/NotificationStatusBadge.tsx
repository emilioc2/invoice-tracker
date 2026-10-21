import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { Notification } from "@/types/notification";

// Same pure status -> variant/label/color mapping pattern as `InvoiceStatusBadge` — no shared
// base helper since the status sets don't overlap, but keeping the same shape intentionally.
const STATUS_CONFIG: Record<Notification["status"], { label: string; className: string }> = {
  Pending: { label: "Pending", className: "" }, // secondary variant alone reads fine for Pending
  Sent: {
    label: "Sent",
    className: "border-transparent bg-green-100 text-green-800 dark:bg-green-500/20 dark:text-green-300",
  },
  Failed: {
    label: "Failed",
    className: "border-transparent bg-red-100 text-red-800 dark:bg-red-500/20 dark:text-red-300",
  },
};

export function NotificationStatusBadge({ status }: { status: Notification["status"] }) {
  const config = STATUS_CONFIG[status];
  const variant = status === "Pending" ? "secondary" : status === "Failed" ? "destructive" : "default";

  return (
    <Badge variant={variant} className={cn(config.className)}>
      {config.label}
    </Badge>
  );
}
