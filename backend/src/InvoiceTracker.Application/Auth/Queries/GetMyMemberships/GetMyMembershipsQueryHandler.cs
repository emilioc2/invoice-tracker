using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Auth.Queries.GetMyMemberships;

public class GetMyMembershipsQueryHandler : IRequestHandler<GetMyMembershipsQuery, List<MembershipOptionDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public GetMyMembershipsQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<List<MembershipOptionDto>> Handle(GetMyMembershipsQuery request, CancellationToken cancellationToken)
    {
        // UserId is resolved server-side from the caller's own JWT — never a client-supplied value.
        var callerUserId = _currentUserService.UserId!.Value;

        var activeMemberships = await _dbContext.UserBusinessMemberships
            .AsNoTracking()
            .Where(m => m.UserId == callerUserId && m.Status == UserStatus.Active)
            .ToListAsync(cancellationToken);

        if (activeMemberships.Count == 0)
        {
            return new List<MembershipOptionDto>();
        }

        var businessIds = activeMemberships.Select(m => m.BusinessId).ToList();
        var businessNamesById = await _dbContext.Businesses
            .Where(b => businessIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        return activeMemberships
            .Select(m => new MembershipOptionDto(
                m.BusinessId,
                businessNamesById.GetValueOrDefault(m.BusinessId, string.Empty),
                m.Role.ToString()))
            .ToList();
    }
}
