using InvoiceTracker.Application.Invoices.Common;
using MediatR;

namespace InvoiceTracker.Application.Invoices.Commands.SendInvoice;

/// <summary>Draft → Unpaid. Sets IssueDate = now server-side; immutable from this point on.</summary>
public record SendInvoiceCommand(Guid Id) : IRequest<InvoiceDto>;
