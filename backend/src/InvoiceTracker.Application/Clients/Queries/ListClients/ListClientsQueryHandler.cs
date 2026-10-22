using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Clients.Queries.ListClients;

/// <summary>
/// The default list only ever shows active (non-removed) clients for the caller's business —
/// removing (see <c>RemoveClientCommandHandler</c>) hides a client here without deleting it.
/// </summary>
public class ListClientsQueryHandler : IRequestHandler<ListClientsQuery, PaginatedResult<ClientDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ListClientsQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<PaginatedResult<ClientDto>> Handle(ListClientsQuery request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var query = _dbContext.Clients
            .AsNoTracking()
            .Where(c => c.BusinessId == businessId && c.Status == ClientStatus.Active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Provider-agnostic case-insensitive contains (translates to LOWER() on both sides)
            // rather than a Postgres-specific EF.Functions.ILike, which would pull a Npgsql
            // dependency into this provider-neutral Application-layer handler.
            var search = request.Search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(search) ||
                c.Email.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Materialize first, then map — ToDto() is a plain client-side projection, not something
        // the EF Core query translator can inline into SQL.
        var entities = await query
            .OrderBy(c => c.Name)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = entities.Select(c => c.ToDto()).ToList();

        return new PaginatedResult<ClientDto>(items, totalCount, request.PageNumber, request.PageSize);
    }
}
