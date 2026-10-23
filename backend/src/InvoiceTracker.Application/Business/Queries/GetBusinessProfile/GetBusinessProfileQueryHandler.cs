using InvoiceTracker.Application.BusinessProfile.Common;
using InvoiceTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.BusinessProfile.Queries.GetBusinessProfile;

public class GetBusinessProfileQueryHandler : IRequestHandler<GetBusinessProfileQuery, BusinessProfileDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public GetBusinessProfileQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<BusinessProfileDto> Handle(GetBusinessProfileQuery request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        // Business is the tenant itself, so there's no cross-tenant "belongs to a different
        // business" case to 404 on here — the JWT's own BusinessId claim is always the caller's
        // business, and it's expected to always exist (every JWT is scoped to a real Business
        // row), so a single-row lookup by id is enough.
        var business = await _dbContext.Businesses
            .AsNoTracking()
            .SingleAsync(b => b.Id == businessId, cancellationToken);

        return business.ToProfileDto();
    }
}
