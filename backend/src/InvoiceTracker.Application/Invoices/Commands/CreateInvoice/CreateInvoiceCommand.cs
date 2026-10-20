using InvoiceTracker.Application.Invoices.Common;
using MediatR;

namespace InvoiceTracker.Application.Invoices.Commands.CreateInvoice;

/// <summary>
/// Always creates the invoice as <c>Draft</c> with <c>IssueDate = null</c> — neither is part of
/// this payload because they don't exist yet (see architecture.md's Invoices routes). Subtotal,
/// TaxAmount and Total are likewise not accepted here: the handler always computes them
/// server-side from <see cref="LineItems"/> and <see cref="TaxRate"/>.
/// </summary>
public record CreateInvoiceCommand(
    Guid ClientId,
    DateTime DueDate,
    decimal TaxRate,
    string? Notes,
    List<InvoiceLineItemRequest> LineItems) : IRequest<InvoiceDto>;
