using InvoiceTracker.Application.Clients.Common;
using MediatR;

namespace InvoiceTracker.Application.Clients.Commands.CreateClient;

public record CreateClientCommand(
    string Name,
    string Email,
    string? Phone,
    string? Address) : IRequest<ClientDto>;
