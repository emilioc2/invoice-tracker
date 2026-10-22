using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Persistence;
using MediatR;

namespace InvoiceTracker.Application.Clients.Commands.UpdateClient;

public class UpdateClientCommandHandler : IRequestHandler<UpdateClientCommand, ClientDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public UpdateClientCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ClientDto> Handle(UpdateClientCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        // Scoped by BusinessId resolved server-side — a client belonging to a different business
        // is indistinguishable from one that doesn't exist, so this returns 404, not 403.
        var client = await _dbContext.Clients
            .GetForBusinessOrThrowAsync(request.Id, businessId, "Client not found.", cancellationToken);

        client.Name = request.Name;
        client.Email = request.Email;
        client.Phone = request.Phone ?? string.Empty;
        client.Address = request.Address ?? string.Empty;
        client.LastModifiedDate = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return client.ToDto();
    }
}
