using InvoiceTracker.Application.Clients.Common;
using MediatR;

namespace InvoiceTracker.Application.Clients.Commands.UpdateClient;

public record UpdateClientCommand(
    Guid Id,
    string Name,
    string Email,
    string? Phone,
    string? Address) : IRequest<ClientDto>;
