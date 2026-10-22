using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Auth.Commands.SelectBusiness;

/// <summary>
/// Validates the preauth token's signature/expiry/claim (via <see cref="IJwtTokenGenerator.ValidatePreAuthToken"/>
/// — never trusting any UserId the caller might otherwise supply directly), then re-checks server-side
/// that the identity it names actually holds an active membership in the requested business before
/// issuing a normal, fully-scoped JWT — exactly like a normal single-membership login from here on.
/// </summary>
public class SelectBusinessCommandHandler : IRequestHandler<SelectBusinessCommand, AuthResultDto>
{
    private const string InvalidPreAuthTokenMessage = "Invalid or expired pre-authentication token.";
    private const string NoMatchingMembershipMessage = "No active membership was found for the selected business.";

    private readonly IApplicationDbContext _dbContext;
    private readonly IIdentityService _identityService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public SelectBusinessCommandHandler(
        IApplicationDbContext dbContext,
        IIdentityService identityService,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _identityService = identityService;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResultDto> Handle(SelectBusinessCommand request, CancellationToken cancellationToken)
    {
        var userId = _jwtTokenGenerator.ValidatePreAuthToken(request.PreAuthToken);
        if (userId is null)
        {
            throw new AuthenticationFailedException(InvalidPreAuthTokenMessage);
        }

        // Re-resolved server-side from the validated token's own sub claim — the request's
        // BusinessId is only ever used to look up a membership for *this* UserId, never trusted as
        // a standalone target.
        var membership = await _dbContext.UserBusinessMemberships
            .FirstOrDefaultAsync(
                m => m.UserId == userId.Value && m.BusinessId == request.BusinessId && m.Status == UserStatus.Active,
                cancellationToken);

        if (membership is null)
        {
            throw new NotFoundException(NoMatchingMembershipMessage);
        }

        var identity = await _identityService.FindIdentityByIdAsync(userId.Value);
        if (identity is null)
        {
            // Shouldn't happen in practice (the preauth token's sub only ever names a real
            // identity) — fail the same clean way as any other "not a valid target" outcome here.
            throw new NotFoundException(NoMatchingMembershipMessage);
        }

        var token = _jwtTokenGenerator.GenerateToken(userId.Value, membership.BusinessId, membership.Role.ToString(), identity.Email);

        return AuthResultDto.ForToken(
            token, userId.Value, membership.BusinessId, identity.Email, membership.Role.ToString(), identity.MustChangePassword);
    }
}
