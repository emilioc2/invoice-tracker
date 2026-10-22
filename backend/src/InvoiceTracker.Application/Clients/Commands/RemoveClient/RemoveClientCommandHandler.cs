using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Persistence;
using InvoiceTracker.Domain.Enums;
using MediatR;

namespace InvoiceTracker.Application.Clients.Commands.RemoveClient;

/// <summary>
/// Soft delete via <c>Client.TransitionTo(ClientStatus.Removed)</c> rather than removing the row
/// (see architecture.md's "Domain invariants: centralized status transitions"). Idempotent —
/// removing an already-removed client is a no-op, not an error, matching the prior
/// ArchiveClientCommandHandler's behavior (the illegal-transition guard inside TransitionTo exists
/// to catch genuinely invalid transitions, not to turn an already-applied removal into a 400 here).
/// </summary>
public class RemoveClientCommandHandler : IRequestHandler<RemoveClientCommand>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public RemoveClientCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task Handle(RemoveClientCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var client = await _dbContext.Clients
            .GetForBusinessOrThrowAsync(request.Id, businessId, "Client not found.", cancellationToken);

        if (client.Status == ClientStatus.Removed)
        {
            return;
        }

        client.TransitionTo(ClientStatus.Removed);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
