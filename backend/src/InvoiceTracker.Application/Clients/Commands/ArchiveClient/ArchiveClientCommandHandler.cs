using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Clients.Commands.ArchiveClient;

/// <summary>
/// Soft delete: sets <see cref="InvoiceTracker.Domain.Entities.Client.ArchivedAt"/> rather than
/// removing the row. Idempotent — archiving an already-archived client is a no-op, not an error.
/// </summary>
public class ArchiveClientCommandHandler : IRequestHandler<ArchiveClientCommand>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ArchiveClientCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task Handle(ArchiveClientCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var client = await _dbContext.Clients
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.BusinessId == businessId, cancellationToken);

        if (client is null)
        {
            throw new NotFoundException("Client not found.");
        }

        client.ArchivedAt ??= DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
