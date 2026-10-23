import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { ClientStatus } from "@/types/client";

// Same pure status -> variant/label/color mapping pattern as `InvoiceStatusBadge`/
// `NotificationStatusBadge` — no shared base helper since the status sets don't overlap, but
// keeping the same shape intentionally. Per the app-wide status-color system, `Removed` is a
// neutral/inactive terminal state, so it reads fine on the secondary variant alone with no extra
// className, same as Draft/Cancelled/Pending elsewhere.
const STATUS_CONFIG: Record<ClientStatus, { label: string; className: string }> = {
  Active: { label: "Active", className: "" }, // not surfaced today (active clients render no badge), kept for completeness
  Removed: { label: "Removed", className: "" }, // secondary variant alone reads fine for Removed
};

export function ClientStatusBadge({ status }: { status: ClientStatus }) {
  const config = STATUS_CONFIG[status];

  return (
    <Badge variant="secondary" className={cn(config.className)}>
      {config.label}
    </Badge>
  );
}
