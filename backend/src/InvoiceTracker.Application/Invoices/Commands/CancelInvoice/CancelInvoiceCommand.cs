using InvoiceTracker.Application.Invoices.Common;
using MediatR;

namespace InvoiceTracker.Application.Invoices.Commands.CancelInvoice;

/// <summary>Draft/Unpaid → Cancelled. Not allowed once Paid (or already Overdue/Cancelled).</summary>
public record CancelInvoiceCommand(Guid Id) : IRequest<InvoiceDto>;
