using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Auth.Commands.Login;

/// <summary>
/// See requirements.md's "Multi-business membership": zero active memberships fails identically to
/// a bad password; exactly one active membership behaves exactly as before (the common case, zero
/// visible change); more than one active membership returns a preauth token + the list of
/// businesses/roles to choose from instead of a usable access token, and the caller must complete
/// the login via <c>POST /api/auth/select-business</c>.
/// </summary>
public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResultDto>
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    private readonly IApplicationDbContext _dbContext;
    private readonly IIdentityService _identityService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public LoginCommandHandler(
        IApplicationDbContext dbContext,
        IIdentityService identityService,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _identityService = identityService;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResultDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var identity = await _identityService.ValidateCredentialsAsync(request.Email, request.Password);
        if (identity is null)
        {
            throw new AuthenticationFailedException(InvalidCredentialsMessage);
        }

        var activeMemberships = await _dbContext.UserBusinessMemberships
            .Where(m => m.UserId == identity.Id && m.Status == UserStatus.Active)
            .ToListAsync(cancellationToken);

        // Credentials matched the Identity store but there's no correlated active membership —
        // treat identically to a bad password rather than leaking which part failed.
        if (activeMemberships.Count == 0)
        {
            throw new AuthenticationFailedException(InvalidCredentialsMessage);
        }

        if (activeMemberships.Count == 1)
        {
            var membership = activeMemberships[0];
            var token = _jwtTokenGenerator.GenerateToken(identity.Id, membership.BusinessId, membership.Role.ToString(), identity.Email);

            return AuthResultDto.ForToken(
                token, identity.Id, membership.BusinessId, identity.Email, membership.Role.ToString(), identity.MustChangePassword);
        }

        // More than one active membership — don't issue a usable token yet. Resolve each
        // membership's business name for display, issue a preauth token scoped only to this
        // identity, and let the caller complete the login via select-business.
        var businessIds = activeMemberships.Select(m => m.BusinessId).ToList();
        var businessNamesById = await _dbContext.Businesses
            .Where(b => businessIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var membershipOptions = activeMemberships
            .Select(m => new MembershipOptionDto(
                m.BusinessId,
                businessNamesById.GetValueOrDefault(m.BusinessId, string.Empty),
                m.Role.ToString()))
            .ToList();

        var preAuthToken = _jwtTokenGenerator.GeneratePreAuthToken(identity.Id);

        return AuthResultDto.ForBusinessSelection(preAuthToken.Token, membershipOptions);
    }
}
