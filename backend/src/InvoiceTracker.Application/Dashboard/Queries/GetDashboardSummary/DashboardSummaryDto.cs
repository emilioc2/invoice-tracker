namespace InvoiceTracker.Application.Dashboard.Queries.GetDashboardSummary;

/// <summary>
/// Minimal dashboard aggregate per architecture.md's single Dashboard route — deliberately not a
/// general-purpose reporting payload (requirements.md explicitly scopes "advanced
/// reporting/analytics beyond the basic dashboard summary" out).
///
/// <list type="bullet">
/// <item><see cref="OutstandingTotal"/> is the sum of <c>Total</c> across invoices still owed —
/// <c>Unpaid</c> + <c>Overdue</c> only. <c>Draft</c> is excluded because nothing's been sent to
/// the client yet (not really "owed"); <c>Paid</c>/<c>Cancelled</c> are excluded because they're
/// resolved.</item>
/// <item><see cref="OverdueCount"/> is just the <c>Overdue</c> bucket of <see cref="CountsByStatus"/>,
/// surfaced directly since architecture.md calls it out by name.</item>
/// <item><see cref="CountsByStatus"/> always has exactly one entry per <c>InvoiceStatus</c> value
/// (zero-filled when a business has none in a given status) so the frontend can render a full
/// breakdown without special-casing missing buckets.</item>
/// </list>
///
/// Archived invoices (<c>ArchivedAt != null</c>) are excluded from every figure here, matching the
/// same default-list convention as <c>ListInvoicesQueryHandler</c>/<c>ListClientsQueryHandler</c> —
/// an invoice a business chose to archive (e.g. tidying up old Cancelled/Paid records, or even an
/// archived-but-still-Unpaid one) is being deliberately hidden from the business's working view, so
/// it shouldn't resurface in the headline dashboard numbers either.
/// </summary>
public record DashboardSummaryDto(
    decimal OutstandingTotal,
    int OverdueCount,
    IReadOnlyList<StatusCountDto> CountsByStatus);

public record StatusCountDto(string Status, int Count);
