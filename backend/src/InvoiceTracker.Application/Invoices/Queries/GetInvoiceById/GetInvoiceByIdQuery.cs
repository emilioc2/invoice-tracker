using InvoiceTracker.Application.Invoices.Common;
using MediatR;

namespace InvoiceTracker.Application.Invoices.Queries.GetInvoiceById;

public record GetInvoiceByIdQuery(Guid Id) : IRequest<InvoiceDto>;
