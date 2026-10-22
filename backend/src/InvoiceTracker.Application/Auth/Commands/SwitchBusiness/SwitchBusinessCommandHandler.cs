using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Auth.Commands.SwitchBusiness;

public class SwitchBusinessCommandHandler : IRequestHandler<SwitchBusinessCommand, AuthResultDto>
{
    private const string NoMatchingMembershipMessage = "No active membership was found for the requested business.";

    private readonly IApplicationDbContext _dbContext;
    private readonly IIdentityService _identityService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public SwitchBusinessCommandHandler(
        IApplicationDbContext dbContext,
        IIdentityService identityService,
        ICurrentUserService currentUserService,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _identityService = identityService;
        _currentUserService = currentUserService;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResultDto> Handle(SwitchBusinessCommand request, CancellationToken cancellationToken)
    {
        var callerUserId = _currentUserService.UserId!.Value;

        // Never trusts request.BusinessId outright — re-validated here that the caller's own
        // identity actually holds an Active membership in it before anything is issued.
        var membership = await _dbContext.UserBusinessMemberships
            .FirstOrDefaultAsync(
                m => m.UserId == callerUserId && m.BusinessId == request.BusinessId && m.Status == UserStatus.Active,
                cancellationToken);

        if (membership is null)
        {
            throw new NotFoundException(NoMatchingMembershipMessage);
        }

        var identity = await _identityService.FindIdentityByIdAsync(callerUserId);
        if (identity is null)
        {
            // Shouldn't happen in practice — the caller already holds a valid JWT for this same
            // UserId — but fail the same clean way as any other "not a valid target" outcome here.
            throw new NotFoundException(NoMatchingMembershipMessage);
        }

        var token = _jwtTokenGenerator.GenerateToken(callerUserId, membership.BusinessId, membership.Role.ToString(), identity.Email);

        return AuthResultDto.ForToken(
            token, callerUserId, membership.BusinessId, identity.Email, membership.Role.ToString(), identity.MustChangePassword);
    }
}
