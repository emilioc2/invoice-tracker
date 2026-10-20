using InvoiceTracker.Application.Clients.Common;
using MediatR;

namespace InvoiceTracker.Application.Clients.Queries.GetClientById;

public record GetClientByIdQuery(Guid Id) : IRequest<ClientDto>;
