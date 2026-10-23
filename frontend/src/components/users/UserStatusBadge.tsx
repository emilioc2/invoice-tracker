import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { UserStatus } from "@/types/auth";

// Same pure status -> variant/label/color mapping pattern as `InvoiceStatusBadge`/
// `NotificationStatusBadge`/`ClientStatusBadge` — no shared base helper since the status sets
// don't overlap, but keeping the same shape intentionally. Per the app-wide status-color system,
// `Active` is the green "good" terminal state (previously the shadcn "default" variant's flat
// primary color, which read the same as a plain action button rather than "this is good") and
// `Deactivated` is neutral/inactive, same family as Draft/Cancelled/Removed/Pending elsewhere.
const STATUS_CONFIG: Record<UserStatus, { label: string; className: string }> = {
  Active: {
    label: "Active",
    className: "border-transparent bg-green-100 text-green-800 dark:bg-green-500/20 dark:text-green-300",
  },
  Deactivated: { label: "Deactivated", className: "" }, // secondary variant alone reads fine for Deactivated
};

export function UserStatusBadge({ status }: { status: UserStatus }) {
  const config = STATUS_CONFIG[status];
  const variant = status === "Active" ? "default" : "secondary";

  return (
    <Badge variant={variant} className={cn(config.className)}>
      {config.label}
    </Badge>
  );
}
