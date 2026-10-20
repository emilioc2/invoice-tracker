using MediatR;

namespace InvoiceTracker.Application.Invoices.Commands.ArchiveInvoice;

public record ArchiveInvoiceCommand(Guid Id) : IRequest;
