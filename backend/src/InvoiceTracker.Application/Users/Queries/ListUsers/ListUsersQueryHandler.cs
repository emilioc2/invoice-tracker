using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Users.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Users.Queries.ListUsers;

public class ListUsersQueryHandler : IRequestHandler<ListUsersQuery, List<UserDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ListUsersQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<List<UserDto>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var users = await _dbContext.BusinessUsers
            .AsNoTracking()
            .Where(u => u.BusinessId == businessId)
            .OrderBy(u => u.Email)
            .ToListAsync(cancellationToken);

        return users.Select(u => u.ToDto()).ToList();
    }
}
