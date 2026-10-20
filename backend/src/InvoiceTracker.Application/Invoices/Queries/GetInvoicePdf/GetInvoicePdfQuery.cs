using InvoiceTracker.Application.Invoices.Common;
using MediatR;

namespace InvoiceTracker.Application.Invoices.Queries.GetInvoicePdf;

public record GetInvoicePdfQuery(Guid Id) : IRequest<InvoicePdfResult>;
