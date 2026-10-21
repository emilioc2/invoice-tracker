import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { InvoiceStatusBadge } from "@/components/invoices/InvoiceStatusBadge";
import type { InvoiceStatus } from "@/types/invoice";

// Pure presentational component — no providers/wrapper needed, just render with each possible
// `InvoiceStatus` and assert the label text that actually renders.
describe("InvoiceStatusBadge", () => {
  const cases: Array<{ status: InvoiceStatus; label: string }> = [
    { status: "Draft", label: "Draft" },
    { status: "Unpaid", label: "Unpaid" },
    { status: "Overdue", label: "Overdue" },
    { status: "Paid", label: "Paid" },
    { status: "Cancelled", label: "Cancelled" },
  ];

  it.each(cases)("renders the label '$label' for status '$status'", ({ status, label }) => {
    render(<InvoiceStatusBadge status={status} />);
    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it("renders distinct labels for every status (no two statuses collide)", () => {
    for (const { status, label } of cases) {
      const { unmount } = render(<InvoiceStatusBadge status={status} />);
      expect(screen.getByText(label)).toBeInTheDocument();
      unmount();
    }
  });
});
