using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using MediatR;

namespace InvoiceTracker.Application.Clients.Commands.CreateClient;

public class CreateClientCommandHandler : IRequestHandler<CreateClientCommand, ClientDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public CreateClientCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ClientDto> Handle(CreateClientCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;
        var now = DateTime.UtcNow;

        var client = new Client
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone ?? string.Empty,
            Address = request.Address ?? string.Empty,
            CreatedAtDate = now,
            LastModifiedDate = now
        };

        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return client.ToDto();
    }
}
