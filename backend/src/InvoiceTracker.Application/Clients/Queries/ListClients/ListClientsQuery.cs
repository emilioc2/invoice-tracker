using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Common.Models;
using MediatR;

namespace InvoiceTracker.Application.Clients.Queries.ListClients;

public record ListClientsQuery(
    string? Search,
    int PageNumber = 1,
    int PageSize = 20) : IRequest<PaginatedResult<ClientDto>>;
