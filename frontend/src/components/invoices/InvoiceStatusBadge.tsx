import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { InvoiceStatus } from "@/types/invoice";

// Pure status -> variant/label/color mapping, nothing stateful — kept this simple and
// side-effect-free on purpose since it's also the unit the step-12 test plan targets directly.
// shadcn's Badge only ships default/secondary/destructive/outline/ghost/link variants, none of
// which read as "amber" or "green," so Overdue/Paid layer a small extra className on top of the
// closest base variant rather than inventing new Badge variants.
const STATUS_CONFIG: Record<InvoiceStatus, { label: string; className: string }> = {
  Draft: { label: "Draft", className: "" }, // secondary variant alone reads fine for Draft
  Unpaid: {
    label: "Unpaid",
    className: "border-transparent bg-blue-100 text-blue-800 dark:bg-blue-500/20 dark:text-blue-300",
  },
  Overdue: {
    label: "Overdue",
    className: "border-transparent bg-amber-100 text-amber-800 dark:bg-amber-500/20 dark:text-amber-300",
  },
  Paid: {
    label: "Paid",
    className: "border-transparent bg-green-100 text-green-800 dark:bg-green-500/20 dark:text-green-300",
  },
  Cancelled: { label: "Cancelled", className: "" }, // outline variant alone reads fine for Cancelled
};

export function InvoiceStatusBadge({ status }: { status: InvoiceStatus }) {
  const config = STATUS_CONFIG[status];
  const variant = status === "Draft" ? "secondary" : status === "Cancelled" ? "outline" : "default";

  return (
    <Badge variant={variant} className={cn(config.className)}>
      {config.label}
    </Badge>
  );
}
