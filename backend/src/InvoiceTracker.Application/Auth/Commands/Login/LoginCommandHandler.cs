using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Auth.Commands.Login;

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

        // This step is a pure restructure (1:1 identity-to-membership still holds for every
        // existing account) — multi-membership selection is a later build-order step (see
        // requirements.md's "Multi-business membership"), so exactly one membership is expected
        // here and FirstOrDefaultAsync is deliberately used rather than SingleOrDefaultAsync.
        var membership = await _dbContext.UserBusinessMemberships
            .FirstOrDefaultAsync(m => m.UserId == identity.Id, cancellationToken);

        // Credentials matched the Identity store but there's no correlated (active) membership
        // row — treat identically to a bad password rather than leaking which part failed.
        if (membership is null || membership.Status != UserStatus.Active)
        {
            throw new AuthenticationFailedException(InvalidCredentialsMessage);
        }

        var token = _jwtTokenGenerator.GenerateToken(identity.Id, membership.BusinessId, membership.Role.ToString(), identity.Email);

        return new AuthResultDto(
            token.AccessToken, token.ExpiresAtUtc, identity.Id, membership.BusinessId, identity.Email, membership.Role.ToString(), identity.MustChangePassword);
    }
}
