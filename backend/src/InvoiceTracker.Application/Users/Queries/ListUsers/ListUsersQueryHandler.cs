using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Users.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Users.Queries.ListUsers;

public class ListUsersQueryHandler : IRequestHandler<ListUsersQuery, List<UserDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IIdentityService _identityService;
    private readonly ICurrentUserService _currentUserService;

    public ListUsersQueryHandler(
        IApplicationDbContext dbContext,
        IIdentityService identityService,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _identityService = identityService;
        _currentUserService = currentUserService;
    }

    public async Task<List<UserDto>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var memberships = await _dbContext.UserBusinessMemberships
            .AsNoTracking()
            .Where(m => m.BusinessId == businessId)
            .ToListAsync(cancellationToken);

        // UserBusinessMembership has no Email column of its own (see its own doc comment) — resolve
        // every listed membership's email in one batched lookup against the identity store rather
        // than one query per row.
        var emailsByUserId = await _identityService.GetEmailsByUserIdsAsync(memberships.Select(m => m.UserId));

        return memberships
            .Select(m => m.ToDto(emailsByUserId.GetValueOrDefault(m.UserId, string.Empty)))
            .OrderBy(u => u.Email)
            .ToList();
    }
}
