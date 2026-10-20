using InvoiceTracker.Application.Invoices.Common;
using MediatR;

namespace InvoiceTracker.Application.Invoices.Commands.MarkInvoicePaid;

/// <summary>Unpaid/Overdue → Paid.</summary>
public record MarkInvoicePaidCommand(Guid Id) : IRequest<InvoiceDto>;
