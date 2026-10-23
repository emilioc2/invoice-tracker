using InvoiceTracker.Application.Invoices.Common;
using MediatR;

namespace InvoiceTracker.Application.Invoices.Commands.CreateInvoice;

/// <summary>
/// Always creates the invoice as <c>Draft</c> with <c>IssueDate = null</c> — neither is part of
/// this payload because they don't exist yet (see architecture.md's Invoices routes). Subtotal,
/// TaxAmount and Total are likewise not accepted here: the handler always computes them
/// server-side from <see cref="LineItems"/> and <see cref="TaxRate"/>.
///
/// <see cref="PaymentDetails"/> is an optional explicit override, per architecture.md's "Business
/// payment details": when supplied, the handler uses it as-is; when omitted (<c>null</c>), the
/// handler snapshots the caller's current <c>Business.PaymentDetails</c> onto the new invoice
/// instead. There is no way to distinguish "explicitly pass null to opt out of any default" from
/// "didn't supply an override" with a single nullable field — that distinction isn't part of this
/// feature's spec (a business with no default and a caller who wants no payment details both just
/// end up with <c>null</c>), so this is a deliberate, documented simplification rather than an
/// oversight.
/// </summary>
public record CreateInvoiceCommand(
    Guid ClientId,
    DateTime DueDate,
    decimal TaxRate,
    string? Notes,
    List<InvoiceLineItemRequest> LineItems,
    string? PaymentDetails = null) : IRequest<InvoiceDto>;
