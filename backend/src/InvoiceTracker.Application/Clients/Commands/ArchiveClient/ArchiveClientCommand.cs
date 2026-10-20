using MediatR;

namespace InvoiceTracker.Application.Clients.Commands.ArchiveClient;

public record ArchiveClientCommand(Guid Id) : IRequest;
