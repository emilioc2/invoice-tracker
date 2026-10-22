using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Clients.Queries.GetClientById;

/// <summary>
/// Returns a client regardless of <c>ArchivedAt</c> — archiving only hides a client from the
/// default list (see <c>ListClientsQueryHandler</c>), it doesn't make a direct-by-id lookup 404.
/// </summary>
public class GetClientByIdQueryHandler : IRequestHandler<GetClientByIdQuery, ClientDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public GetClientByIdQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ClientDto> Handle(GetClientByIdQuery request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var client = await _dbContext.Clients
            .AsNoTracking()
            .GetForBusinessOrThrowAsync(request.Id, businessId, "Client not found.", cancellationToken);

        return client.ToDto();
    }
}
