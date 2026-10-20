using InvoiceTracker.Application.Invoices.Common;
using MediatR;

namespace InvoiceTracker.Application.Invoices.Commands.UpdateInvoice;

/// <summary>
/// Only succeeds while the invoice's Status is Draft — the handler is the enforcement point for
/// the edit-lock rule, rejecting with a <see cref="Common.Exceptions.BadRequestException"/>
/// (mapped to 400) once it's been sent. IssueDate is never part of this payload: it doesn't exist
/// until send, and becomes immutable from that point on.
/// </summary>
public record UpdateInvoiceCommand(
    Guid Id,
    Guid ClientId,
    DateTime DueDate,
    decimal TaxRate,
    string? Notes,
    List<InvoiceLineItemRequest> LineItems) : IRequest<InvoiceDto>;
